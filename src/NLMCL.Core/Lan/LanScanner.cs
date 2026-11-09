using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Lan
{
    /// <summary>
    /// 局域网发现 + 状态查询。
    /// 发现：Minecraft 开“对局域网开放”时每秒向 <c>224.0.2.60:4445</c> 广播
    /// <c>[MOTD]标题[/MOTD][AD]端口[/AD]</c>。这里对所有活动 IPv4 网卡加入组播组收包。
    /// 查询：对每个房间走 SLP，拿版本、在线人数、MOTD；查不到也保留房间（标未响应）。
    /// </summary>
    public sealed class LanScanner : ILanScanner, ILanScannerDiagnostics
    {
        public const int DiscoveryPort = 4445;

        public const string MulticastGroup = "224.0.2.60";

        private readonly IServerPinger _pinger;

        public LanScanner(IServerPinger pinger)
        {
            _pinger = pinger ?? throw new ArgumentNullException(nameof(pinger));
        }

        public async Task<IReadOnlyList<LanRoom>> ScanAsync(
            TimeSpan timeout, IProgress<LanRoom>? progress, CancellationToken cancellationToken)
        {
            LanScanReport report = await ScanWithReportAsync(timeout, progress, cancellationToken)
                .ConfigureAwait(false);
            return report.Rooms;
        }

        public async Task<LanScanReport> ScanWithReportAsync(
            TimeSpan timeout, IProgress<LanRoom>? progress, CancellationToken cancellationToken)
        {
            (List<LanRoom> rooms, List<LanInterfaceStatus> interfaces) =
                await DiscoverAsync(timeout, cancellationToken).ConfigureAwait(false);

            if (rooms.Count == 0)
            {
                return new LanScanReport(rooms, interfaces);
            }

            // 逐个 SLP，最多 8 个并发
            using (SemaphoreSlim slots = new SemaphoreSlim(8))
            {
                List<Task> tasks = new List<Task>();

                foreach (LanRoom room in rooms)
                {
                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            LanRoom pinged = await _pinger
                                .PingAsync(room.Address, room.Port, cancellationToken)
                                .ConfigureAwait(false);

                            room.VersionName = pinged.VersionName;
                            room.Online = pinged.Online;
                            room.Max = pinged.Max;
                            room.LatencyMs = pinged.LatencyMs;
                            room.Pinged = true;

                            if (!string.IsNullOrWhiteSpace(pinged.Motd))
                            {
                                room.Motd = pinged.Motd;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            // 房间可能已关闭：保留条目，标未响应
                        }
                        finally
                        {
                            progress?.Report(room);
                            slots.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            List<LanRoom> sorted = rooms
                .OrderByDescending(room => room.Pinged)
                .ThenByDescending(room => room.Online)
                .ToList();

            return new LanScanReport(sorted, interfaces);
        }

        /// <summary>监听组播广播，按 endpoint 去重。</summary>
        internal static async Task<(List<LanRoom> Rooms, List<LanInterfaceStatus> Interfaces)> DiscoverAsync(
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            ConcurrentDictionary<string, LanRoom> found =
                new ConcurrentDictionary<string, LanRoom>(StringComparer.OrdinalIgnoreCase);

            List<LanInterfaceStatus> interfaces = new List<LanInterfaceStatus>();

            using (UdpClient client = new UdpClient(AddressFamily.InterNetwork))
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                try
                {
                    client.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                }
                catch (SocketException ex)
                {
                    interfaces.Add(new LanInterfaceStatus(
                        "UDP " + DiscoveryPort,
                        "0.0.0.0",
                        false,
                        "端口被占用，无法监听：" + ex.Message));
                    return (new List<LanRoom>(), interfaces);
                }

                try
                {
                    client.Client.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.MulticastLoopback,
                        true);
                    client.Client.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.MulticastTimeToLive,
                        1);
                }
                catch (SocketException)
                {
                    // 某些系统不允许改这些选项，继续用默认值。
                }

                IPAddress group = IPAddress.Parse(MulticastGroup);

                foreach ((string name, string address) in EnumerateIPv4Addresses())
                {
                    IPAddress parsed = IPAddress.Parse(address);

                    try
                    {
                        client.Client.SetSocketOption(
                            SocketOptionLevel.IP,
                            SocketOptionName.AddMembership,
                            new MulticastOption(group, parsed));

                        interfaces.Add(new LanInterfaceStatus(name, address, true, null));
                    }
                    catch (SocketException ex)
                    {
                        interfaces.Add(new LanInterfaceStatus(name, address, false, ex.Message));
                    }
                }

                if (interfaces.Count == 0)
                {
                    interfaces.Add(new LanInterfaceStatus(
                        "IPv4 网卡",
                        string.Empty,
                        false,
                        "没有找到可用的 IPv4 网卡"));
                }

                using (CancellationTokenSource deadline =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    deadline.CancelAfter(timeout);

                    while (!deadline.IsCancellationRequested)
                    {
                        Task<UdpReceiveResult> receive = client.ReceiveAsync();

                        Task finished = await Task.WhenAny(
                            receive, Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token))
                            .ConfigureAwait(false);

                        if (finished != receive)
                        {
                            // 500ms 没新包；第一次超时就继续等，直到整体 deadline
                            continue;
                        }

                        UdpReceiveResult result = await receive.ConfigureAwait(false);
                        string text = Encoding.UTF8.GetString(result.Buffer);

                        if (!TryParse(text, out string motd, out int port))
                        {
                            continue;
                        }

                        string address = (result.RemoteEndPoint.Address.IsIPv4MappedToIPv6
                            ? result.RemoteEndPoint.Address.MapToIPv4()
                            : result.RemoteEndPoint.Address).ToString();

                        LanRoom room = found.GetOrAdd(address + ":" + port, _ => new LanRoom(address, port));
                        room.Motd = motd;
                    }
                }
            }

            return (found.Values.ToList(), interfaces);
        }

        /// <summary>活动网卡的 IPv4 地址，含 VPN，排除回环/未连接。</summary>
        internal static IEnumerable<(string Name, string Address)> EnumerateIPv4Addresses()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up
                    || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    string address = unicast.Address.ToString();

                    if (seen.Add(address))
                    {
                        yield return (network.Name, address);
                    }
                }
            }
        }

        /// <summary>解析 <c>[MOTD]...[/MOTD][AD]25565[/AD]</c>。</summary>
        internal static bool TryParse(string message, out string motd, out int port)
        {
            motd = string.Empty;
            port = 0;

            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            int motdStart = message.IndexOf("[MOTD]", StringComparison.Ordinal);

            if (motdStart >= 0)
            {
                int motdEnd = message.IndexOf("[/MOTD]", motdStart + 6, StringComparison.Ordinal);

                if (motdEnd > motdStart)
                {
                    motd = message.Substring(motdStart + 6, motdEnd - motdStart - 6);
                }
            }

            int adStart = message.IndexOf("[AD]", StringComparison.Ordinal);

            if (adStart < 0)
            {
                return false;
            }

            int adEnd = message.IndexOf("[/AD]", adStart + 4, StringComparison.Ordinal);

            if (adEnd <= adStart)
            {
                return false;
            }

            string portText = message.Substring(adStart + 4, adEnd - adStart - 4).Trim();

            return int.TryParse(portText, out port) && port > 0 && port <= 65535;
        }
    }
}

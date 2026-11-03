using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Lan
{
    /// <summary>
    /// 局域网发现 + 状态查询。
    ///
    /// 发现：Minecraft 开"对局域网开放"时每秒向 <c>224.0.2.60:4445</c> 广播
    /// <c>[MOTD]标题[/MOTD][AD]端口[/AD]</c>。这里加入组播组收包，收满 timeout 或安静 1.5 秒就结束。
    ///
    /// 查询：对每个房间走 SLP，拿版本、在线人数、MOTD；查不到也保留房间（标未响应）。
    /// </summary>
    public sealed class LanScanner : ILanScanner
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
            List<LanRoom> rooms = await DiscoverAsync(timeout, cancellationToken).ConfigureAwait(false);

            if (rooms.Count == 0)
            {
                return rooms;
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
                            // 房间可能已关闭：保留条目，标记未响应
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

            return rooms
                .OrderByDescending(room => room.Pinged)
                .ThenByDescending(room => room.Online)
                .ToList();
        }

        /// <summary>监听组播广播，按 endpoint 去重。</summary>
        internal static async Task<List<LanRoom>> DiscoverAsync(
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            ConcurrentDictionary<string, LanRoom> found =
                new ConcurrentDictionary<string, LanRoom>(StringComparer.OrdinalIgnoreCase);

            using (UdpClient client = new UdpClient())
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                try
                {
                    client.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                }
                catch (SocketException)
                {
                    // 端口被占用（例如 PCL 自己也在听）：直接返回空，不抛给界面
                    return new List<LanRoom>();
                }

                try
                {
                    client.JoinMulticastGroup(IPAddress.Parse(MulticastGroup));
                }
                catch (SocketException)
                {
                    // 某些网络（校园网/虚拟网卡）不允许组播，退化成只听单播广播
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

            return found.Values.ToList();
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

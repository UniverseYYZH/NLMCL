using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Networking
{
    /// <summary>
    /// <c>host:port</c> 地址的解析与格式化。
    ///
    /// 移植自 legacy 的 <c>multiplayer.parse_join_address</c>，
    /// 错误信息按"用户看得懂"来写——联机失败最常见的原因就是地址格式不对。
    /// </summary>
    public static class LanAddress
    {
        /// <summary>
        /// 解析联机地址。支持 <c>host:port</c> 与 <c>tcp://host:port</c>。
        /// </summary>
        public static bool TryParse(string? text, out string host, out int port, out string? error)
        {
            host = string.Empty;
            port = 0;
            error = null;

            string value = (text ?? string.Empty).Trim();

            if (value.Length == 0)
            {
                error = "请输入对方提供的联机地址";
                return false;
            }

            if (value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(6);
            }

            if (value.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                error = "联机地址格式应为 host:port";
                return false;
            }

            if (value.IndexOf('/') >= 0 || value.IndexOf(' ') >= 0)
            {
                error = "联机地址不能包含空格或路径";
                return false;
            }

            // 容错：有些人会把端口写成点号（10.21.38.133.56643）。
            // 五段纯数字时，最后一段当端口。
            if (value.IndexOf(':') < 0)
            {
                string[] dotParts = value.Split('.');

                if (dotParts.Length == 5 && dotParts.All(part => part.Length > 0 && part.All(char.IsDigit)))
                {
                    value = string.Join(".", dotParts, 0, 4) + ":" + dotParts[4];
                }
            }

            int separator = value.LastIndexOf(':');
            if (separator < 0)
            {
                // 纯 IP 没写端口：局域网房间端口是随机的，必须让用户补上，否则必然连错。
                // 域名没写端口：按默认 25565 处理，真实端口由 SRV 记录给出（mc2.nxist.top → 46718）。
                if (IPAddress.TryParse(value, out _))
                {
                    error = "联机地址缺少端口，格式应为 host:port";
                    return false;
                }

                host = value;
                port = 25565;
                return true;
            }

            string hostPart = value.Substring(0, separator).Trim();
            string portPart = value.Substring(separator + 1).Trim();

            if (hostPart.Length == 0)
            {
                error = "联机地址缺少主机名";
                return false;
            }

            if (!int.TryParse(portPart, out int parsedPort))
            {
                error = "联机端口不是有效数字";
                return false;
            }

            if (parsedPort < 1 || parsedPort > 65535)
            {
                error = "联机端口必须在 1-65535 之间";
                return false;
            }

            host = hostPart;
            port = parsedPort;
            return true;
        }

        public static string Format(string? host, int port)
        {
            return (string.IsNullOrEmpty(host) ? "127.0.0.1" : host) + ":" + port;
        }
    }

    /// <summary>用 .NET 的网络 API 实现 <see cref="ILocalNetworkInfo"/>。</summary>
    public sealed class LocalNetworkInfo : ILocalNetworkInfo
    {
        public string? GetLocalIPv4()
        {
            // 先问系统"你会从哪个地址出去"，再退回枚举网卡。
            //
            // 为什么这个顺序重要：一台笔记本可能同时连着有线与无线（校园网很常见），
            // 两块网卡在**不同网段**。枚举出来的第一个地址可能是没在用的那块，
            // 发给对方就成了一个连不上的地址。UDP connect 不发包，只让系统按路由表选出口。
            string? routeAddress = GetOutboundIPv4();
            if (!string.IsNullOrEmpty(routeAddress))
            {
                return routeAddress;
            }

            List<string> candidates = new List<string>();

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    // 跳过回环、隧道与虚拟网卡（VPN / Docker / Hyper-V 的地址连不通别人）
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    if (IsVirtual(adapter))
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        string ip = info.Address.ToString();
                        if (ip.StartsWith("169.254.", StringComparison.Ordinal))
                        {
                            continue; // APIPA：没拿到 DHCP，连不通别人
                        }

                        candidates.Add(ip);
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // 拿不到网卡信息时退回下面的兜底逻辑
            }

            // 私有地址优先
            string? privateAddress = candidates.FirstOrDefault(IsPrivate);
            if (privateAddress != null)
            {
                return privateAddress;
            }

            return candidates.FirstOrDefault();
        }

        public async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535)
            {
                return false;
            }

            // 校园网首次连接常卡在 ARP/DNS：1.5 秒会误判，这里给 3 秒并试两次。
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                using (TcpClient client = new TcpClient())
                {
                    try
                    {
                        Task connect = client.ConnectAsync(host, port);
                        Task finished = await Task.WhenAny(
                            connect,
                            Task.Delay(TimeSpan.FromSeconds(3), cancellationToken)).ConfigureAwait(false);

                        if (finished != connect)
                        {
                            continue; // 超时，再试一次
                        }

                        await connect.ConfigureAwait(false);

                        if (client.Connected)
                        {
                            return true;
                        }
                    }
                    catch (SocketException)
                    {
                        // 连接被拒/不可达：立刻失败，不再重试
                        return false;
                    }
                    catch (ObjectDisposedException)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 让系统按路由表选出"出口网卡"的地址。UDP connect 不实际发包。
        /// 失败（无网络 / 无路由）时返回 null，由调用方回退到枚举。
        /// </summary>
        private static string? GetOutboundIPv4()
        {
            try
            {
                using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // 公网地址只用来触发路由选择，不建立连接、不发数据
                    probe.Connect("223.5.5.5", 53);

                    if (probe.LocalEndPoint is IPEndPoint endpoint)
                    {
                        string address = endpoint.Address.ToString();
                        return address == "0.0.0.0" ? null : address;
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            return null;
        }

        private static bool IsPrivate(string ip)
        {
            if (ip.StartsWith("10.", StringComparison.Ordinal))
            {
                return true;
            }

            if (ip.StartsWith("192.168.", StringComparison.Ordinal))
            {
                return true;
            }

            if (ip.StartsWith("172.", StringComparison.Ordinal))
            {
                string[] parts = ip.Split('.');
                if (parts.Length == 4 && int.TryParse(parts[1], out int second))
                {
                    return second >= 16 && second <= 31;
                }
            }

            return false;
        }

        private static bool IsVirtual(NetworkInterface adapter)
        {
            string name = adapter.Name + " " + adapter.Description;

            return name.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("vmware", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("hyper-v", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("vethernet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("docker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("tap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("tun", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

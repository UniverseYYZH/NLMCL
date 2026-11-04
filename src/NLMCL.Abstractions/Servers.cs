using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>常用服务器（快捷入口）。</summary>
    public sealed class ServerBookmark
    {
        public ServerBookmark(string name, string address)
        {
            Name = name ?? string.Empty;
            Address = (address ?? string.Empty).Trim();
        }

        public string Name { get; }

        /// <summary>host 或 host:port。</summary>
        public string Address { get; }

        /// <summary>补上默认端口 25565，得到可以直接传给 <c>--server</c> 的参数。</summary>
        public string Endpoint => Address.IndexOf(':') >= 0 ? Address : Address + ":25565";

        /// <summary>显示用：地址 + 端口（25565 省略）。</summary>
        public string DisplayAddress => Address.IndexOf(':') >= 0 ? Address : Address + ":25565";

        /// <summary>解析 <c>host[:port]</c>；域名与 IPv4 都接受。</summary>
        public static bool TryParse(string? address, out string host, out int port, out string? error)
        {
            host = string.Empty;
            port = 25565;
            error = null;

            if (string.IsNullOrWhiteSpace(address))
            {
                error = "地址为空";
                return false;
            }

            string text = address!.Trim();

            int colon = text.LastIndexOf(':');

            if (colon > 0 && text.IndexOf(':') == colon)
            {
                host = text.Substring(0, colon);
                string portText = text.Substring(colon + 1);

                if (!int.TryParse(portText, out port) || port <= 0 || port > 65535)
                {
                    error = "端口不合法：" + portText;
                    return false;
                }
            }
            else
            {
                host = text;
            }

            if (host.Length == 0)
            {
                error = "地址为空";
                return false;
            }

            return true;
        }
    }

    /// <summary>常用服务器列表的持久化。</summary>
    public interface IServerBookmarkStore
    {
        Task<IReadOnlyList<ServerBookmark>> LoadAsync(CancellationToken cancellationToken);

        Task SaveAsync(IReadOnlyList<ServerBookmark> servers, CancellationToken cancellationToken);
    }
}

    /// <summary>解析后的服务器端点（可能是 SRV 记录给出的真实端口）。</summary>
    public readonly struct ServerEndpoint
    {
        public ServerEndpoint(string host, int port, bool fromSrv)
        {
            Host = host;
            Port = port;
            FromSrv = fromSrv;
        }

        /// <summary>实际连接用的主机（SRV 的 target 或原主机）。</summary>
        public string Host { get; }

        public int Port { get; }

        /// <summary>端口是否来自 SRV 记录。</summary>
        public bool FromSrv { get; }

        public override string ToString() => Host + ":" + Port;
    }

    /// <summary>
    /// 把「域名[:端口]」解析成实际可连的端点。
    ///
    /// 为什么需要：Minecraft 服务器常把真实端口藏在 DNS 的
    /// <c>_minecraft._tcp.&lt;域名&gt;</c> SRV 记录里（例如 mc2.nxist.top 实际是 46718），
    /// 只填 25565 会直接连不上。游戏客户端自己会查 SRV，但我们的端口探测/诊断不会，
    /// 所以需要这一层。
    /// </summary>
    public interface IServerAddressResolver
    {
        /// <summary>解析；查不到 SRV 时原样返回（<c>FromSrv = false</c>）。</summary>
        Task<ServerEndpoint> ResolveAsync(string host, int port, CancellationToken cancellationToken);
    }
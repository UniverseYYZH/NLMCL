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

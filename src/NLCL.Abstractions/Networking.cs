using System.Threading;
using System.Threading.Tasks;

namespace NLCL.Abstractions
{
    /// <summary>
    /// 局域网联机所需的网络信息。实现用 .NET 的 NetworkInterface / TcpClient，
    /// 不涉及 Minecraft 领域知识，因此不需要复用 PCL。
    /// </summary>
    public interface ILocalNetworkInfo
    {
        /// <summary>
        /// 本机在局域网里的 IPv4。优先返回私有地址（10./192.168./172.16-31），
        /// 跳过虚拟网卡与回环；找不到时返回 null。
        /// </summary>
        string? GetLocalIPv4();

        /// <summary>探测某个 TCP 端口是否可连（用于确认对方真的开了房）。</summary>
        Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken);
    }
}

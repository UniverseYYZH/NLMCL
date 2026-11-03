using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>局域网里发现的一个房间（MOTD + SLP 查询结果）。</summary>
    public sealed class LanRoom
    {
        public LanRoom(string address, int port)
        {
            Address = address ?? throw new ArgumentNullException(nameof(address));
            Port = port;
        }

        public string Address { get; }

        public int Port { get; }

        public string Endpoint => Address + ":" + Port;

        /// <summary>MOTD（可能来自 UDP 广播，也可能来自 SLP）。</summary>
        public string Motd { get; set; } = string.Empty;

        public string VersionName { get; set; } = string.Empty;

        public int Online { get; set; }

        public int Max { get; set; }

        public int LatencyMs { get; set; } = -1;

        public bool Pinged { get; set; }

        public string StatusText => Pinged
            ? Online + "/" + Max + " 人 · " + VersionName + " · " + LatencyMs + " ms"
            : "未响应状态查询（可能已关闭或版本过旧）";
    }

    /// <summary>局域网扫描：监听 Minecraft 的 UDP 广播（224.0.2.60:4445）并对每个房间做 SLP。</summary>
    public interface ILanScanner
    {
        Task<IReadOnlyList<LanRoom>> ScanAsync(
            TimeSpan timeout, IProgress<LanRoom>? progress, CancellationToken cancellationToken);
    }

    /// <summary>Server List Ping（现代协议 1.7+）。</summary>
    public interface IServerPinger
    {
        Task<LanRoom> PingAsync(string host, int port, CancellationToken cancellationToken);
    }
}

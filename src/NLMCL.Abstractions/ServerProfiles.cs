using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>服务器档案的登录模式。</summary>
    public enum ServerProfileMode
    {
        /// <summary>只进大厅 / 纯净服：不需要任何 mod。</summary>
        Vanilla,

        /// <summary>需要 AutoModpack 同步的模组服。</summary>
        Modded
    }

    /// <summary>
    /// 一个"服务器档案"：进入某台服务器需要什么。
    ///
    /// 为什么需要它：宝可梦服要求 1.21 + Fabric + AutoModpack，
    /// 玩家自己配环境极易出错（版本不对 → 落到大厅，还以为是启动器坏了）。
    /// 档案把"这台服要什么"写死，玩家点一下就行。
    ///
    /// 一个档案可以有多个地址（服主用来分流），启动器挑一个能连的。
    /// </summary>
    public sealed class ServerProfile
    {
        public ServerProfile(string id, string name, string minecraft)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? string.Empty;
            Minecraft = minecraft ?? string.Empty;
        }

        /// <summary>稳定标识（实例目录、配置键都用它）。</summary>
        public string Id { get; }

        public string Name { get; set; }

        /// <summary>要求的 MC 版本，例如 <c>1.21.1</c>。</summary>
        public string Minecraft { get; set; }

        /// <summary>加载器类型：<c>fabric</c> / <c>forge</c> / <c>neoforge</c> / 空 = 原版。</summary>
        public string Loader { get; set; } = string.Empty;

        /// <summary>加载器版本；空表示用下载页默认推荐版本。</summary>
        public string LoaderVersion { get; set; } = string.Empty;

        /// <summary>AutoModpack 客户端 mod 的版本（仅 <see cref="ServerProfileMode.Modded"/> 需要）。</summary>
        public string AutoModpackVersion { get; set; } = string.Empty;

        public ServerProfileMode Mode { get; set; } = ServerProfileMode.Vanilla;

        /// <summary>服务器地址（可多个，用于分流/容灾）。域名或 IP，可带端口。</summary>
        public List<string> Addresses { get; } = new List<string>();

        /// <summary>备注，展示在界面上。</summary>
        public string Notes { get; set; } = string.Empty;

        /// <summary>地址的展示文本（多个地址用 / 分隔）。</summary>
        public string AddressText => string.Join(" / ", Addresses);

        /// <summary>是否需要 mod（界面上标注"需装模组"）。</summary>
        public bool RequiresMods => Mode == ServerProfileMode.Modded;

        public override string ToString() => Name;
    }

    /// <summary>服务器档案的持久化。</summary>
    public interface IServerProfileStore
    {
        Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken);

        Task SaveAsync(IReadOnlyList<ServerProfile> profiles, CancellationToken cancellationToken);
    }
}

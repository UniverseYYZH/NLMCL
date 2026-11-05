using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>资源类型（决定安装到哪个子目录）。</summary>
    public enum ResourceKind
    {
        Mod = 0,
        ResourcePack = 1,
        ShaderPack = 2,
        DataPack = 3
    }

    /// <summary>一次资源搜索的条件。</summary>
    public sealed class ResourceQuery
    {
        public ResourceQuery(string text, ResourceKind kind)
        {
            Text = text ?? string.Empty;
            Kind = kind;
        }

        public string Text { get; }

        public ResourceKind Kind { get; }

        /// <summary>限定游戏版本，例如 1.20.1；null 表示不限。</summary>
        public string? GameVersion { get; set; }

        /// <summary>限定加载器（fabric / forge / neoforge）；null 表示不限。资源包/光影通常没有加载器。</summary>
        public string? Loader { get; set; }

        public int Limit { get; set; } = 20;

        public int Offset { get; set; }
    }

    /// <summary>搜索结果里的一个项目（模组 / 资源包 / 光影 / 数据包）。</summary>
    public sealed class ResourceSummary
    {
        public ResourceSummary(
            string id, string slug, string title, string description,
            string? iconUrl, string? author, long downloads, ResourceKind kind)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Slug = slug ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            IconUrl = iconUrl;
            Author = author;
            Downloads = downloads;
            Kind = kind;
        }

        public string Id { get; }

        public string Slug { get; }

        public string Title { get; }

        public string Description { get; }

        public string? IconUrl { get; }

        public string? Author { get; }

        public long Downloads { get; }

        public ResourceKind Kind { get; }

        /// <summary>「1.2 万次下载」之类的短标签。</summary>
        public string DownloadsText
        {
            get
            {
                if (Downloads >= 10_000_000)
                {
                    return (Downloads / 10_000_000.0).ToString("0.#") + " 千万";
                }

                if (Downloads >= 10_000)
                {
                    return (Downloads / 10_000.0).ToString("0.#") + " 万";
                }

                return Downloads.ToString();
            }
        }
    }

    /// <summary>某个项目的一个可下载版本（已按游戏版本/加载器筛过）。</summary>
    public sealed class ResourceFile
    {
        public ResourceFile(string name, string url, long size, string? versionName,
            IReadOnlyList<string> gameVersions, string? loader, string? sha1 = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Size = size;
            VersionName = versionName;
            GameVersions = gameVersions ?? Array.Empty<string>();
            Loader = loader;
            Sha1 = sha1;
        }

        /// <summary>Modrinth 提供的 SHA1（可为空；有就必须校验）。</summary>
        public string? Sha1 { get; }

        public string Name { get; }

        public string Url { get; }

        public long Size { get; }

        public string? VersionName { get; }

        public IReadOnlyList<string> GameVersions { get; }

        public string? Loader { get; }
    }

    /// <summary>把下载好的资源文件放进实例目录（mods / resourcepacks / shaderpacks / datapacks）。</summary>
    public interface IResourceInstaller
    {
        /// <summary>返回落地后的完整路径。</summary>
        Task<string> InstallAsync(
            ResourceFile file,
            ResourceKind kind,
            string minecraftDirectory,
            string versionFolder,
            CancellationToken cancellationToken);
    }

    /// <summary>资源搜索端口（实现：Modrinth）。</summary>
    public interface IResourceCatalog
    {
        Task<IReadOnlyList<ResourceSummary>> SearchAsync(ResourceQuery query, CancellationToken cancellationToken);

        /// <summary>取该项目当前条件下的可下载文件，按发布时间倒序。</summary>
        Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
            string projectId, ResourceKind kind, string? gameVersion, string? loader,
            CancellationToken cancellationToken);
    }
    /// <summary>
    /// CurseForge 兜底安装：输入项目链接 → 用第三方只读接口（cfwidget）取文件清单
    /// → 组装 ForgeCDN 直链 → 下载并放进实例 mods。
    ///
    /// 为什么不用官方 API：官方 v1 需要 key，团队暂时没申请。
    /// </summary>
    public interface ICurseForgeInstaller
    {
        /// <summary>
        /// 安装指定项目里最匹配的文件。
        /// 返回落地路径；失败抛异常，消息可直接给用户看。
        /// </summary>
        Task<string> InstallAsync(
            string projectUrl,
            string minecraftDirectory,
            string versionFolder,
            string? gameVersion,
            string? loader,
            CancellationToken cancellationToken);

        /// <summary>
        /// 直接下载一个已知地址的文件到实例目录（搜索结果里点「安装」走这条）。
        ///
        /// 为什么不复用 ResourceInstaller：它依赖 PCL 的 ModNet，
        /// 对 ForgeCDN 的直链会报"通过网络请求直接下载小文件失败"，而自带下载器能正常下。
        /// </summary>
        Task<string> InstallFileAsync(
            string url,
            string fileName,
            ResourceKind kind,
            string minecraftDirectory,
            string versionFolder,
            CancellationToken cancellationToken);
    }
}
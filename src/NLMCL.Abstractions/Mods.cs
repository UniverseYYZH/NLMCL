using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>实例里的一个模组文件。</summary>
    public sealed class ModEntry
    {
        public ModEntry(string filePath, string fileName, long size)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Size = size;
        }

        public string FilePath { get; set; }

        public string FileName { get; set; }

        public long Size { get; }

        /// <summary>模组声明的名字（解析不到时用文件名）。</summary>
        public string Name { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        /// <summary>fabric / forge / neoforge / unknown。</summary>
        public string Loader { get; set; } = "unknown";

        /// <summary>false 表示文件名是 <c>.jar.disabled</c>。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Modrinth 上的最新版本号（检查更新后填充）。</summary>
        public string? LatestVersion { get; set; }

        /// <summary>Modrinth 页面地址（有则显示）。</summary>
        public string? ProjectUrl { get; set; }

        public bool HasUpdate => !string.IsNullOrEmpty(LatestVersion)
            && !string.Equals(LatestVersion, Version, StringComparison.OrdinalIgnoreCase);

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FileName : Name;

        public string Detail => string.IsNullOrEmpty(Version) ? FileName : Name + " " + Version;

        public string StateText => Enabled ? "已启用" : "已禁用";

        public string SizeText => Size >= 1024 * 1024
            ? (Size / 1024.0 / 1024.0).ToString("0.0") + " MB"
            : Math.Max(1, Size / 1024) + " KB";

        public string UpdateText => HasUpdate ? "有新版本 " + LatestVersion : string.Empty;

        /// <summary>模组 ID（fabric.mod.json 的 id；用于依赖检查）。</summary>
        public string ModId { get; set; } = string.Empty;

        /// <summary>缺失的前置模组（id 或 id + 版本要求）。</summary>
        public List<string> MissingDependencies { get; } = new List<string>();

        public bool HasMissingDependencies => MissingDependencies.Count > 0;

        /// <summary>界面上显示的「缺前置：…」。</summary>
        public string DependencyText => HasMissingDependencies
            ? "缺前置：" + string.Join("、", MissingDependencies)
            : string.Empty;
    }

    /// <summary>模组文件管理：列目录、启用/禁用（改后缀）、删除。</summary>
    public interface IModManager
    {
        /// <summary>列出实例的 mods 目录（隔离实例是 <c>versions/&lt;id&gt;/mods</c>）。</summary>
        Task<IReadOnlyList<ModEntry>> ListAsync(string modsDirectory, CancellationToken cancellationToken);

        Task EnableAsync(ModEntry entry, CancellationToken cancellationToken);

        Task DisableAsync(ModEntry entry, CancellationToken cancellationToken);

        Task DeleteAsync(ModEntry entry, CancellationToken cancellationToken);
    }

    /// <summary>更新检查：按文件 SHA1 反查 Modrinth，再比最新版本号。</summary>
    public interface IModUpdateChecker
    {
        Task CheckAsync(ModEntry entry, string? gameVersion, string? loader, CancellationToken cancellationToken);
    }
}

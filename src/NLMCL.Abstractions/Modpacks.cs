using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>整合包依赖的加载器。</summary>
    public enum ModpackLoader
    {
        Vanilla = 0,
        Fabric = 1,
        Forge = 2,
        NeoForge = 3
    }

    /// <summary>一个 .mrpack 的概要（不落地，只读索引）。</summary>
    public sealed class ModpackInfo
    {
        public ModpackInfo(
            string name, string versionId, string minecraftVersion,
            ModpackLoader loader, string? loaderVersion, int fileCount)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            VersionId = versionId ?? string.Empty;
            MinecraftVersion = minecraftVersion ?? string.Empty;
            Loader = loader;
            LoaderVersion = loaderVersion;
            FileCount = fileCount;
        }

        public string Name { get; }

        public string VersionId { get; }

        public string MinecraftVersion { get; }

        public ModpackLoader Loader { get; }

        public string? LoaderVersion { get; }

        public int FileCount { get; }

        public string Summary => MinecraftVersion
            + " · " + Loader
            + (string.IsNullOrEmpty(LoaderVersion) ? string.Empty : " " + LoaderVersion)
            + " · " + FileCount + " 个文件";
    }

    /// <summary>整合包安装：读 .mrpack、装加载器、落文件、铺 overrides。</summary>
    public interface IModpackInstaller
    {
        Task<ModpackInfo> ReadAsync(string mrpackPath, CancellationToken cancellationToken);

        /// <summary>返回创建的版本目录名（可直接在「本地版本」里启动）。</summary>
        Task<string> InstallAsync(
            string mrpackPath,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken);
    }

    /// <summary>已安装整合包的本地元数据；实例目录下的 .nlmcl-modpack.json。</summary>
    public sealed class ModpackManifest
    {
        public int Schema { get; set; } = 1;

        /// <summary>modrinth / curseforge；只有 modrinth 支持更新检查。</summary>
        public string Source { get; set; } = "modrinth";

        public string ProjectId { get; set; } = string.Empty;

        public string VersionId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string MinecraftVersion { get; set; } = string.Empty;

        public string Loader { get; set; } = string.Empty;

        public string? LoaderVersion { get; set; }

        public DateTimeOffset InstalledAt { get; set; }

        /// <summary>整合包自己安装的 mods 相对路径，用于更新时保留玩家额外加的 Mod。</summary>
        public List<string> ModFiles { get; set; } = new List<string>();

        public string Summary => (string.IsNullOrEmpty(MinecraftVersion) ? "未知版本" : MinecraftVersion)
            + " · " + (string.IsNullOrEmpty(Loader) ? "原版" : Loader)
            + (string.IsNullOrEmpty(LoaderVersion) ? string.Empty : " " + LoaderVersion)
            + (string.IsNullOrEmpty(VersionId) ? string.Empty : " · " + VersionId);
    }

    /// <summary>一次可用的整合包更新。</summary>
    public sealed class ModpackUpdateInfo
    {
        public ModpackUpdateInfo(
            ModpackManifest installed,
            string versionId,
            string versionName,
            string downloadUrl,
            string fileName,
            DateTimeOffset? publishedAt)
        {
            Installed = installed;
            VersionId = versionId ?? string.Empty;
            VersionName = versionName ?? string.Empty;
            DownloadUrl = downloadUrl ?? string.Empty;
            FileName = fileName ?? string.Empty;
            PublishedAt = publishedAt;
        }

        public ModpackManifest Installed { get; }

        public string VersionId { get; }

        public string VersionName { get; }

        public string DownloadUrl { get; }

        public string FileName { get; }

        public DateTimeOffset? PublishedAt { get; }

        public string Summary => string.IsNullOrEmpty(VersionName)
            ? VersionId
            : VersionName + "（" + VersionId + "）";
    }

    /// <summary>整合包元数据读取、更新检查与本地备份；不负责直接替换实例。</summary>
    public interface IModpackUpdateService
    {
        Task<ModpackManifest?> ReadAsync(string instanceDirectory, CancellationToken cancellationToken);

        /// <summary>无更新、非 Modrinth 整合包或无法解析来源时返回 null。</summary>
        Task<ModpackUpdateInfo?> CheckAsync(string instanceDirectory, CancellationToken cancellationToken);

        /// <summary>备份实例为 zip，返回备份文件路径。</summary>
        Task<string> BackupAsync(string instanceDirectory, CancellationToken cancellationToken);
    }

    /// <summary>选一个文件（.mrpack）。文件夹选择已有 IFolderPicker。</summary>
    public interface IFilePicker
    {
        string? PickFile(string title, string filter);
    }
}

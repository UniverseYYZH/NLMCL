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

        /// <summary>原地更新已安装实例。下载/解压先写 staging，失败不碰原实例。</summary>
        Task UpdateAsync(
            string mrpackPath,
            string instanceDirectory,
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

        /// <summary>最近一次更新前备份的绝对路径；用于重启后仍能一键回滚。</summary>
        public string? LastBackup { get; set; }

        /// <summary>
        /// CurseForge 整合包的项目 ID。只有导入包显式携带时才写入；
        /// 普通 CurseForge manifest 没有这个字段，更新检查会降级为“暂不支持”。
        /// </summary>
        public long? CurseForgeProjectId { get; set; }

        /// <summary>CurseForge 整合包文件 ID；与项目 ID 同时存在才可用于更新检查。</summary>
        public long? CurseForgeFileId { get; set; }

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
            DateTimeOffset? publishedAt,
            string? sha1 = null,
            string source = "modrinth")
        {
            Installed = installed;
            VersionId = versionId ?? string.Empty;
            VersionName = versionName ?? string.Empty;
            DownloadUrl = downloadUrl ?? string.Empty;
            FileName = fileName ?? string.Empty;
            PublishedAt = publishedAt;
            Sha1 = sha1 ?? string.Empty;
            Source = source ?? string.Empty;
        }

        public ModpackManifest Installed { get; }

        /// <summary>modrinth / curseforge；CurseForge 本轮只检查、不自动安装。</summary>
        public string Source { get; }

        public string VersionId { get; }

        public string VersionName { get; }

        public string DownloadUrl { get; }

        public string FileName { get; }

        public DateTimeOffset? PublishedAt { get; }

        /// <summary>新版文件的 SHA1；为空表示源站没有提供，不能安全更新。</summary>
        public string Sha1 { get; }

        public string Summary => string.IsNullOrEmpty(VersionName)
            ? VersionId
            : VersionName + "（" + VersionId + "）";
    }

    /// <summary>更新检查的三种用户可见结果。</summary>
    public enum ModpackUpdateCheckStatus
    {
        /// <summary>发现可展示的新版。</summary>
        UpdateAvailable = 0,

        /// <summary>当前版本已是最新。</summary>
        UpToDate = 1,

        /// <summary>来源缺少 ID、接口失败或本轮不支持。</summary>
        Unsupported = 2
    }

    /// <summary>带状态的更新检查结果；失败/缺元数据不能抛出到启动路径。</summary>
    public sealed class ModpackUpdateCheckResult
    {
        private ModpackUpdateCheckResult(
            ModpackUpdateCheckStatus status, ModpackUpdateInfo? info, string message)
        {
            Status = status;
            Info = info;
            Message = message ?? string.Empty;
        }

        public ModpackUpdateCheckStatus Status { get; }

        public ModpackUpdateInfo? Info { get; }

        public string Message { get; }

        public static ModpackUpdateCheckResult Available(ModpackUpdateInfo info)
            => new ModpackUpdateCheckResult(
                ModpackUpdateCheckStatus.UpdateAvailable,
                info ?? throw new ArgumentNullException(nameof(info)),
                string.Empty);

        public static ModpackUpdateCheckResult UpToDate(string message = "已是最新版本。")
            => new ModpackUpdateCheckResult(
                ModpackUpdateCheckStatus.UpToDate, null, message);

        public static ModpackUpdateCheckResult Unsupported(string message)
            => new ModpackUpdateCheckResult(
                ModpackUpdateCheckStatus.Unsupported, null, message);
    }

    /// <summary>整合包元数据读取、更新检查与本地备份；不负责直接替换实例。</summary>
    public interface IModpackUpdateService
    {
        Task<ModpackManifest?> ReadAsync(string instanceDirectory, CancellationToken cancellationToken);

        /// <summary>兼容旧调用：只有发现新版时返回信息。</summary>
        Task<ModpackUpdateInfo?> CheckAsync(string instanceDirectory, CancellationToken cancellationToken);

        /// <summary>带“有新版/无更新/暂不支持”的检查结果；不抛网络/限流异常。</summary>
        Task<ModpackUpdateCheckResult> CheckDetailedAsync(
            string instanceDirectory, CancellationToken cancellationToken);

        /// <summary>备份实例为 zip，返回备份文件路径。</summary>
        Task<string> BackupAsync(string instanceDirectory, CancellationToken cancellationToken);
    }

    /// <summary>选一个文件（.mrpack）。文件夹选择已有 IFolderPicker。</summary>
    public interface IFilePicker
    {
        string? PickFile(string title, string filter);
    }
}

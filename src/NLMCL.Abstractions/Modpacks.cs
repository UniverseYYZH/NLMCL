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

    /// <summary>选一个文件（.mrpack）。文件夹选择已有 IFolderPicker。</summary>
    public interface IFilePicker
    {
        string? PickFile(string title, string filter);
    }
}

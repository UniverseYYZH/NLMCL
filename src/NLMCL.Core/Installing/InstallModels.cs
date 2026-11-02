using System.Collections.Generic;

namespace NLMCL.Core.Installing
{
    /// <summary>安装一个版本时需要的全部可配置项。默认为 Mojang 官方源。</summary>
    public sealed class InstallOptions
    {
        public string ManifestUrl { get; set; } = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

        /// <summary>资源文件（assets objects）的根地址。</summary>
        public string ResourcesBaseUrl { get; set; } = "https://resources.download.minecraft.net";

        /// <summary>只下载前 N 个资源文件；0 表示不限（测试用）。</summary>
        public int MaxAssetFiles { get; set; }
    }

    /// <summary>一个待下载的文件。</summary>
    internal sealed class RemoteFile
    {
        public RemoteFile(string url, string targetPath, string? sha1, long size)
        {
            Url = url;
            TargetPath = targetPath;
            Sha1 = sha1;
            Size = size;
        }

        public string Url { get; }

        public string TargetPath { get; }

        /// <summary>小写 SHA1；为空表示不校验。</summary>
        public string? Sha1 { get; }

        public long Size { get; }
    }

    /// <summary>由版本 JSON 推导出的安装计划。</summary>
    internal sealed class VersionInstallPlan
    {
        public VersionInstallPlan(string versionDirectory, string versionJsonPath)
        {
            VersionDirectory = versionDirectory;
            VersionJsonPath = versionJsonPath;
        }

        public string VersionDirectory { get; }

        public string VersionJsonPath { get; }

        /// <summary>先下载、后解析其内容的资源索引；该版本没有资源时为 null。</summary>
        public RemoteFile? AssetIndex { get; set; }

        /// <summary>其余要下载的文件（客户端 JAR、库、以及后面的资源对象）。</summary>
        public List<RemoteFile> Files { get; } = new List<RemoteFile>();

        /// <summary>需要解压出 natives 的库归档。</summary>
        public List<RemoteFile> NativeArchives { get; } = new List<RemoteFile>();

        public string NativesDirectory { get; set; } = string.Empty;
    }
}

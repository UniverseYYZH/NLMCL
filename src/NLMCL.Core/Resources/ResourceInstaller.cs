using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// 把资源文件放进实例目录。
    ///
    /// 目录选择与快照一致：PCL 的版本隔离实例把数据放在 <c>versions\&lt;实例&gt;\</c>，
    /// 非隔离的才在 <c>.minecraft</c> 根；隔离目录存在就优先用它。
    /// </summary>
    public sealed class ResourceInstaller : IResourceInstaller
    {
        private readonly IDownloadService _download;

        public ResourceInstaller(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<string> InstallAsync(
            ResourceFile file,
            ResourceKind kind,
            string minecraftDirectory,
            string versionFolder,
            CancellationToken cancellationToken)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (string.IsNullOrWhiteSpace(minecraftDirectory))
            {
                throw new ArgumentException("游戏目录为空。", nameof(minecraftDirectory));
            }

            string baseDirectory = ResolveBaseDirectory(minecraftDirectory, versionFolder);
            string targetDirectory = Path.Combine(baseDirectory, SubDirectoryOf(kind));

            Directory.CreateDirectory(targetDirectory);

            string target = Path.Combine(targetDirectory, SanitizeFileName(file.Name));
            string temporary = target + ".nlmcl-tmp";

            try
            {
                // 最多两次：第一次校验失败就重下一次，避免"半个文件"被当成好的
                for (int attempt = 1; ; attempt++)
                {
                    await _download.DownloadFileAsync(file.Url, temporary, cancellationToken).ConfigureAwait(false);

                    if (!IsChecksumValid(temporary, file.Sha1, out string actual, out long length))
                    {
                        TryDelete(temporary);

                        if (attempt >= 2)
                        {
                            throw new InvalidDataException(
                                "文件校验失败（两次下载都不匹配）：" + file.Name
                                + "；期望 SHA1 " + file.Sha1
                                + "，实际 " + actual + "，大小 " + length + " 字节。"
                                + "可能是下载被截断或镜像缓存了坏文件，稍后重试或换网络。");
                        }

                        continue;
                    }

                    break;
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temporary, target);

                return target;
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        /// <summary>有 SHA1 就校验；没有就只看"文件非空"。</summary>
        internal static bool IsChecksumValid(string path, string? expectedSha1, out string actual, out long length)
        {
            actual = string.Empty;
            length = 0;

            if (!File.Exists(path))
            {
                return false;
            }

            length = new FileInfo(path).Length;

            if (string.IsNullOrWhiteSpace(expectedSha1))
            {
                return length > 0;
            }

            actual = MeloongCore.CryptographyUtils.ComputeFileHash(
                path, MeloongCore.CryptographyUtils.HashMethod.Sha1);

            return string.Equals(actual, expectedSha1!.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>安装目录：隔离实例优先。</summary>
        internal static string ResolveBaseDirectory(string minecraftDirectory, string versionFolder)
        {
            if (!string.IsNullOrWhiteSpace(versionFolder))
            {
                string isolated = Path.Combine(minecraftDirectory, "versions", versionFolder!);
                if (Directory.Exists(isolated))
                {
                    return isolated;
                }
            }

            return minecraftDirectory;
        }

        internal static string SubDirectoryOf(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.ResourcePack:
                    return "resourcepacks";
                case ResourceKind.ShaderPack:
                    return "shaderpacks";
                case ResourceKind.DataPack:
                    return "datapacks";
                default:
                    return "mods";
            }
        }

        private static string SanitizeFileName(string name)
        {
            string fileName = Path.GetFileName(name);

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? "resource.jar" : fileName;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

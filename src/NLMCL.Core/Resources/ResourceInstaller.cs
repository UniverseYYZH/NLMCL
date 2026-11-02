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
                await _download.DownloadFileAsync(file.Url, temporary, cancellationToken).ConfigureAwait(false);

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

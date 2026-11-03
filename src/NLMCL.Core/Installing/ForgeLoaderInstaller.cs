using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Forge 客户端安装：下载官方安装器 jar，然后 <c>java -jar installer --installClient &lt;目录&gt;</c>。
    ///
    /// 侦察实测（1.20.1 + Forge 47.4.27）：154 秒装完，产物是
    /// <c>versions\1.20.1-forge-47.4.27\1.20.1-forge-47.4.27.json</c> 与 libraries。
    ///
    /// 唯一前置：目标目录里必须有 <c>launcher_profiles.json</c>，否则安装器会报
    /// "There is no Minecraft launcher profile ... you need to run the launcher first"。
    /// </summary>
    public sealed class ForgeLoaderInstaller : ILoaderInstaller
    {
        public const string MavenBase = "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge";

        private readonly IDownloadService _download;
        private readonly IProcessRunner _process;
        private readonly string _cacheDirectory;

        public ForgeLoaderInstaller(
            IDownloadService download, IProcessRunner process, string? cacheDirectory = null)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _process = process ?? throw new ArgumentNullException(nameof(process));

            // 测试可以指定独立目录，避免把桩文件写进真实缓存（曾经因此让 47.4.27 安装失败）
            _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
                ? Path.Combine(Path.GetTempPath(), "nlmcl-forge")
                : cacheDirectory!;
        }

        public async Task<string> InstallForgeAsync(
            string minecraftVersion,
            string loaderVersion,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion))
            {
                throw new ArgumentException("游戏版本为空。", nameof(minecraftVersion));
            }

            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                throw new ArgumentException("Forge 版本为空。", nameof(loaderVersion));
            }

            if (string.IsNullOrWhiteSpace(javaExecutable))
            {
                throw new InvalidOperationException("没有可用的 Java，无法运行 Forge 安装器。");
            }

            string id = minecraftVersion.Trim() + "-forge-" + loaderVersion.Trim();

            progress?.Report(new InstallProgress("准备 Forge " + loaderVersion, 2, 0));

            string jar = await EnsureInstallerAsync(id, minecraftVersion.Trim(), loaderVersion.Trim(), cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(minecraftDirectory);
            EnsureLauncherProfiles(minecraftDirectory);

            progress?.Report(new InstallProgress("运行 Forge 安装器（需要几分钟）", 10, 0));

            int lines = 0;

            ProcessResult result = await _process.RunAsync(
                javaExecutable,
                "-jar \"" + jar + "\" --installClient \"" + minecraftDirectory + "\"",
                minecraftDirectory,
                line =>
                {
                    lines++;
                    int percent = Math.Min(95, 10 + lines / 4);
                    progress?.Report(new InstallProgress("安装中：" + Shorten(line), percent, 0));
                },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Forge 安装器退出码 " + result.ExitCode + "。输出尾部：\n" + Tail(result.Output));
            }

            string versionJson = Path.Combine(minecraftDirectory, "versions", id, id + ".json");

            if (!File.Exists(versionJson))
            {
                throw new InvalidOperationException("安装器没有生成版本 JSON：" + versionJson);
            }

            progress?.Report(new InstallProgress("Forge 安装完成", 100, 0));

            return id;
        }

        /// <summary>安装器 jar 下载到临时目录；已存在就复用（同版本重装不必再下 8 MB）。</summary>
        private async Task<string> EnsureInstallerAsync(string id, string minecraftVersion, string loaderVersion, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_cacheDirectory);

            string path = Path.Combine(_cacheDirectory, id + "-installer.jar");

            if (IsValidJar(path))
            {
                return path;
            }

            // 缓存损坏（半截下载、被别的程序写坏、测试桩写入）时删掉重下，
            // 只校验体积是不够的：一个 2MB 的全零文件同样能通过 ">1MB" 检查。
            TryDelete(path);

            // 注意 Maven 布局：目录是 {mc}-{forge}，文件名才是 forge-{mc}-{forge}-installer.jar
            // （id 里的 "forge-" 只用于版本目录名，不能直接拼进 maven 路径）
            string artifact = minecraftVersion + "-" + loaderVersion;
            string url = MavenBase + "/" + artifact + "/forge-" + artifact + "-installer.jar";

            await _download.DownloadFileAsync(url, path, cancellationToken).ConfigureAwait(false);

            return path;
        }

        /// <summary>真正的 jar 以 PK\003\004 开头；体积也要够（安装器约 8MB）。</summary>
        internal static bool IsValidJar(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < 1024 * 1024)
                {
                    return false;
                }

                using (FileStream stream = File.OpenRead(path))
                {
                    return stream.ReadByte() == 0x50 && stream.ReadByte() == 0x4B;
                }
            }
            catch (IOException)
            {
                return false;
            }
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

        /// <summary>安装器要求目录里已有启动器配置；没有就写一个最小的。</summary>
        internal static void EnsureLauncherProfiles(string minecraftDirectory)
        {
            string path = Path.Combine(minecraftDirectory, "launcher_profiles.json");

            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllText(
                path,
                "{\"profiles\":{},\"selectedProfile\":\"\",\"clientToken\":\"\"}",
                new UTF8Encoding(false));
        }

        private static string Shorten(string line)
        {
            string text = line?.Trim() ?? string.Empty;
            return text.Length <= 60 ? text : text.Substring(0, 60) + "…";
        }

        private static string Tail(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return "(无输出)";
            }

            string[] lines = output.Replace("\r", string.Empty).Split('\n');
            int start = Math.Max(0, lines.Length - 15);
            return string.Join("\n", lines, start, lines.Length - start);
        }
    }
}

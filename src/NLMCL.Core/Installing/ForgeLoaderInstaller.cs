using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Forge / NeoForge 客户端安装：下载官方安装器 jar，然后
    /// <c>java -jar installer --installClient &lt;目录&gt;</c>。
    ///
    /// 侦察实测（1.20.1 + Forge 47.4.27）：154 秒装完，产物是
    /// <c>versions\1.20.1-forge-47.4.27\</c> 与 libraries。
    ///
    /// 唯一前置：目标目录里必须有 <c>launcher_profiles.json</c>，否则安装器会报
    /// "There is no Minecraft launcher profile ... you need to run the launcher first"。
    /// </summary>
    public sealed class ForgeLoaderInstaller : ILoaderInstaller
    {
        public const string ForgeMavenBase = "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge";

        public const string NeoForgeMavenBase = "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge";

        public const string ForgeOfficialMavenBase =
            "https://maven.minecraftforge.net/net/minecraftforge/forge";

        public const string NeoForgeOfficialMavenBase =
            "https://maven.neoforged.net/releases/net/neoforged/neoforge";

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

        public Task<string> InstallForgeAsync(
            string minecraftVersion, string loaderVersion, string minecraftDirectory,
            string javaExecutable, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion))
            {
                throw new ArgumentException("游戏版本为空。", nameof(minecraftVersion));
            }

            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                throw new ArgumentException("Forge 版本为空。", nameof(loaderVersion));
            }

            string id = minecraftVersion.Trim() + "-forge-" + loaderVersion.Trim();
            string artifact = minecraftVersion.Trim() + "-" + loaderVersion.Trim();
            string url = ForgeMavenBase + "/" + artifact + "/forge-" + artifact + "-installer.jar";
            string fallback = ForgeOfficialMavenBase + "/" + artifact + "/forge-" + artifact + "-installer.jar";

            return InstallCoreAsync(
                id, url, fallback, "Forge", minecraftDirectory, javaExecutable, progress, cancellationToken);
        }

        public Task<string> InstallNeoForgeAsync(
            string minecraftVersion, string loaderVersion, string minecraftDirectory,
            string javaExecutable, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                throw new ArgumentException("NeoForge 版本为空。", nameof(loaderVersion));
            }

            string version = loaderVersion.Trim();
            string id = "neoforge-" + version;
            string url = NeoForgeMavenBase + "/" + version + "/neoforge-" + version + "-installer.jar";
            string fallback = NeoForgeOfficialMavenBase + "/" + version
                + "/neoforge-" + version + "-installer.jar";

            return InstallCoreAsync(
                id, url, fallback, "NeoForge", minecraftDirectory, javaExecutable, progress, cancellationToken);
        }

        private async Task<string> InstallCoreAsync(
            string id,
            string installerUrl,
            string fallbackInstallerUrl,
            string displayName,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(javaExecutable))
            {
                throw new InvalidOperationException("没有可用的 Java，无法运行 " + displayName + " 安装器。");
            }

            progress?.Report(new InstallProgress("准备 " + displayName + " " + id, 2, 0));

            string jar = await EnsureInstallerAsync(
                id, installerUrl, fallbackInstallerUrl, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(minecraftDirectory);
            EnsureLauncherProfiles(minecraftDirectory);

            // 官方安装器只认自己的 maven（--mirror 是赞助商镜像，不改下载地址），
            // 所以先把库从国内镜像预置好；安装器看到本地已有文件就跳过下载。
            progress?.Report(new InstallProgress("预置 " + displayName + " 库", 8, 0));

            LoaderLibrarySeeder seeder = new LoaderLibrarySeeder(_download);
            int seeded = await seeder
                .SeedAsync(jar, minecraftDirectory, progress, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new InstallProgress(
                "运行 " + displayName + " 安装器（已预置 " + seeded + " 个库，需要几分钟）", 25, 0));

            int reported = 0;

            ProcessResult result = await _process.RunAsync(
                javaExecutable,
                "-jar \"" + jar + "\" --installClient \"" + minecraftDirectory + "\"",
                minecraftDirectory,
                line =>
                {
                    // 安装器输出很杂（skip: false / Patching ...），只把关键行推到界面
                    if (!ShouldReport(line))
                    {
                        return;
                    }

                    reported++;
                    int percent = Math.Min(95, 10 + reported / 3);
                    progress?.Report(new InstallProgress("安装中：" + Shorten(line), percent, 0));
                },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (result.ExitCode != 0)
            {
                if (result.Output.IndexOf("These libraries failed to download", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        displayName + " 安装器缺少部分库（国内镜像也没有）。通常是网络问题，换时间重试或在设置里调大下载线程。\n输出尾部：\n"
                        + Tail(result.Output));
                }

                throw new InvalidOperationException(
                    displayName + " 安装器退出码 " + result.ExitCode + "。输出尾部：\n" + Tail(result.Output));
            }

            // NeoForge 的版本目录名与 id 未必一致，装完直接扫一遍最新产物更可靠
            string? installed = FindInstalledVersion(minecraftDirectory, id);

            if (installed == null)
            {
                throw new InvalidOperationException(
                    "安装器没有生成版本 JSON（期望 " + id + "）。输出尾部：\n" + Tail(result.Output));
            }

            progress?.Report(new InstallProgress(displayName + " 安装完成", 100, 0));

            return installed;
        }

        /// <summary>安装器 jar 下载到缓存目录；缓存必须是真正的 jar（PK 头）才复用。</summary>
        private async Task<string> EnsureInstallerAsync(
            string id, string installerUrl, string fallbackInstallerUrl,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_cacheDirectory);

            string path = Path.Combine(_cacheDirectory, id + "-installer.jar");

            if (IsValidJar(path))
            {
                return path;
            }

            Exception? firstError = null;
            string[] candidates = { installerUrl, fallbackInstallerUrl };

            foreach (string url in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                try
                {
                    // 缓存损坏（半截下载、被别的程序写坏、测试桩写入）时删掉重下，
                    // 只校验体积是不够的：一个 2MB 的全零文件同样能通过 ">1MB" 检查。
                    TryDelete(path);

                    await _download.DownloadFileAsync(url, path, cancellationToken).ConfigureAwait(false);

                    if (IsValidJar(path))
                    {
                        return path;
                    }

                    firstError ??= new InvalidOperationException("下载到的安装器不是有效 jar：" + url);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    firstError ??= ex;
                    TryDelete(path);
                }
            }

            throw new InvalidOperationException(
                "安装器下载失败，镜像与官方地址都不可用：" + id, firstError);
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

        /// <summary>哪些输出行值得显示：下载、补丁、安装、注入、错误。</summary>
        internal static bool ShouldReport(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string text = line!.Trim();

            if (text.Length < 3)
            {
                return false;
            }

            string[] keywords =
            {
                "Downloading", "Downloaded", "Patching", "Extracting", "Installing", "Building",
                "Injecting", "Processors", "ERROR", "Exception", "Failed", "fail",
                "Successfully installed", "done"
            };

            return keywords.Any(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>在 versions 下找刚生成的版本目录（NeoForge 的目录名未必等于预期 id）。</summary>
        internal static string? FindInstalledVersion(string minecraftDirectory, string expectedId)
        {
            string versions = Path.Combine(minecraftDirectory, "versions");

            if (!Directory.Exists(versions))
            {
                return null;
            }

            string expectedJson = Path.Combine(versions, expectedId, expectedId + ".json");
            if (File.Exists(expectedJson))
            {
                return expectedId;
            }

            // 退路：名字里含 neoforge/forge 且最近修改的那个
            string token = expectedId.Contains("neoforge") ? "neoforge" : "forge";

            return Directory.GetDirectories(versions)
                .Where(dir => Path.GetFileName(dir).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(dir => File.Exists(Path.Combine(dir, Path.GetFileName(dir) + ".json")))
                .OrderByDescending(dir => Directory.GetLastWriteTime(dir))
                .Select(Path.GetFileName)
                .FirstOrDefault();
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

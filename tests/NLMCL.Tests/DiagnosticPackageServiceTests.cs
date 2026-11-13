using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Diagnostics;
using Xunit;

namespace NLMCL.Tests
{
    public class DiagnosticPackageServiceTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-diag-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Creates_zip_with_logs_settings_and_redacted_secrets()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root, portable: true);
            string minecraft = paths.DefaultMinecraftDirectory;
            string version = "fabric-loader-0.20.1-1.21.1";
            string game = Path.Combine(minecraft, "versions", version);
            string mods = Path.Combine(game, "mods");
            string data = paths.DataRoot;
            string zip = Path.Combine(root, "diagnostics.zip");

            try
            {
                Directory.CreateDirectory(Path.Combine(game, "logs"));
                Directory.CreateDirectory(Path.Combine(game, "crash-reports"));
                Directory.CreateDirectory(mods);
                Directory.CreateDirectory(Path.Combine(data, "logs"));

                const string token = "TOPSECRET-TOKEN";
                const string uuid = "2b570d8b-14df-3270-83c9-a36d7bd8f614";

                File.WriteAllText(Path.Combine(game, "logs", "latest.log"),
                    "Starting\n--accessToken " + token + "\n--uuid " + uuid + "\nDone", Encoding.UTF8);
                File.WriteAllText(Path.Combine(game, "crash-reports", "crash-2026-01-01.txt"),
                    "crash report\n--clientId abc123\n--xuid 42", Encoding.UTF8);
                File.WriteAllText(Path.Combine(data, "last-launch.txt"),
                    "--accessToken " + token + Environment.NewLine + "退出码: 1", Encoding.UTF8);
                File.WriteAllText(Path.Combine(data, "logs", "crash-20260101-120000.log"),
                    "launcher crash\nBearer " + token, Encoding.UTF8);
                File.WriteAllBytes(Path.Combine(mods, "fabric-api.jar"), new byte[2048]);
                File.WriteAllBytes(Path.Combine(mods, "disabled.jar.disabled"), new byte[1024]);
                File.WriteAllText(Path.Combine(game, "nlmcl-settings.json"),
                    "{\"memoryMb\":4096,\"useProxy\":true,\"proxyHost\":\"127.0.0.1\",\"proxyPort\":7890," +
                    "\"accessToken\":\"" + token + "\"}", Encoding.UTF8);

                DiagnosticPackageRequest request = new DiagnosticPackageRequest(
                    minecraft, version, game, mods, Path.Combine(game, "nlmcl-settings.json"))
                {
                    AppVersion = "9.9.9-test",
                    JavaHomes = new[] { @"C:\Java\21", @"C:\Java\17" }
                };

                DiagnosticPackageResult result = await new DiagnosticPackageService(paths)
                    .CreateAsync(request, zip, CancellationToken.None);

                Assert.True(File.Exists(zip));
                Assert.Empty(result.Missing);

                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    string[] names = archive.Entries.Select(entry => entry.FullName).ToArray();
                    Assert.Contains("README.txt", names);
                    Assert.Contains("environment.txt", names);
                    Assert.Contains("logs/latest.log", names);
                    Assert.Contains("logs/crash-report.txt", names);
                    Assert.Contains("logs/last-launch.txt", names);
                    Assert.Contains("logs/launcher-crash.log", names);
                    Assert.Contains("instance/mods.txt", names);
                    Assert.Contains("instance/settings.txt", names);

                    string all = string.Join("\n", names.Select(name => ReadEntry(archive, name)));

                    Assert.DoesNotContain(token, all);
                    Assert.DoesNotContain(uuid, all);
                    Assert.Contains("2b570d8b-…", all);
                    Assert.Contains("fabric-api.jar", all);
                    Assert.Contains("禁用", all);
                    Assert.Contains("4096", all);
                    Assert.Contains("\"useProxy\": true", all);
                    Assert.DoesNotContain("\"useProxy\": null", all);
                    Assert.Contains("9.9.9-test", all);
                }
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Missing_files_still_produce_a_readme_and_valid_zip()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root, portable: true);
            string minecraft = Path.Combine(root, "minecraft");
            string game = Path.Combine(minecraft, "versions", "1.21.1");
            string zip = Path.Combine(root, "diagnostics.zip");

            try
            {
                DiagnosticPackageRequest request = new DiagnosticPackageRequest(
                    minecraft, "1.21.1", game, Path.Combine(game, "mods"),
                    Path.Combine(game, "nlmcl-settings.json"));

                DiagnosticPackageResult result = await new DiagnosticPackageService(paths)
                    .CreateAsync(request, zip, CancellationToken.None);

                Assert.True(File.Exists(zip));
                Assert.True(result.Missing.Count >= 3);

                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    string readme = ReadEntry(archive, "README.txt");
                    Assert.Contains("缺失项", readme);
                    Assert.Contains("latest.log", readme);
                }
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Large_log_is_truncated_with_a_marker()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root, portable: true);
            string minecraft = Path.Combine(root, "minecraft");
            string version = "1.21.1";
            string game = Path.Combine(minecraft, "versions", version);
            string zip = Path.Combine(root, "diagnostics.zip");

            try
            {
                Directory.CreateDirectory(Path.Combine(game, "logs"));
                File.WriteAllText(Path.Combine(game, "logs", "latest.log"),
                    new string('x', 2 * 1024 * 1024 + 100), Encoding.UTF8);

                DiagnosticPackageRequest request = new DiagnosticPackageRequest(
                    minecraft, version, game, Path.Combine(game, "mods"),
                    Path.Combine(game, "nlmcl-settings.json"));

                await new DiagnosticPackageService(paths)
                    .CreateAsync(request, zip, CancellationToken.None);

                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    Assert.Contains("已截断", ReadEntry(archive, "logs/latest.log"));
                }
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static string ReadEntry(ZipArchive archive, string name)
        {
            ZipArchiveEntry entry = archive.GetEntry(name);
            if (entry == null)
            {
                return string.Empty;
            }

            using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
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

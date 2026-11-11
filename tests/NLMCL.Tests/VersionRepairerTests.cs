using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;
using NLMCL.Core.Repair;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>版本修复：收集缺失文件、下载补齐、Windows 规则过滤。</summary>
    public class VersionRepairerTests
    {
        private sealed class FakeFiles : IFileDownloader
        {
            public int Downloads { get; private set; }

            public string Content { get; set; } = "fake:";

            public Task DownloadAsync(string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
                => DownloadAsync(url, targetPath, cancellationToken);

            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
            {
                Downloads++;
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, Content == "fake:" ? "fake:" + url : Content);
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }

        private static string BuildFakeMinecraft()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-repair-" + Guid.NewGuid().ToString("N"));
            string versions = Path.Combine(root, "versions");
            Directory.CreateDirectory(Path.Combine(versions, "Test"));
            Directory.CreateDirectory(Path.Combine(versions, "1.20.1"));

            File.WriteAllText(
                Path.Combine(versions, "Test", "Test.json"),
                @"{
                    ""id"": ""Test"",
                    ""inheritsFrom"": ""1.20.1"",
                    ""libraries"": [
                        { ""downloads"": { ""artifact"": { ""path"": ""com/example/lib/1.0/lib-1.0.jar"", ""url"": ""https://example.invalid/lib.jar"" } } }
                    ]
                }");

            File.WriteAllText(
                Path.Combine(versions, "1.20.1", "1.20.1.json"),
                @"{
                    ""id"": ""1.20.1"",
                    ""downloads"": { ""client"": { ""url"": ""https://example.invalid/client.jar"" } }
                }");

            return root;
        }

        [Fact]
        public async Task Inspect_reports_missing_client_jar_and_library()
        {
            string root = BuildFakeMinecraft();

            try
            {
                VersionRepairer repairer = new VersionRepairer(new FakeFiles());
                RepairReport report = await repairer.InspectAsync(root, "Test", CancellationToken.None);

                Assert.Equal(2, report.Missing);
                Assert.Contains(report.Details, item => item.Contains("lib-1.0.jar"));
                Assert.Contains(report.Details, item => item.Contains("1.20.1.jar"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Repair_downloads_missing_files()
        {
            string root = BuildFakeMinecraft();

            try
            {
                FakeFiles files = new FakeFiles();
                VersionRepairer repairer = new VersionRepairer(files);

                RepairReport report = await repairer.RepairAsync(root, "Test", null, CancellationToken.None);

                Assert.Equal(2, report.Missing);
                Assert.Equal(2, report.Repaired);
                Assert.Equal(0, report.Failed);

                Assert.True(File.Exists(Path.Combine(root, "libraries", "com", "example", "lib", "1.0", "lib-1.0.jar")));
                Assert.True(File.Exists(Path.Combine(root, "versions", "1.20.1", "1.20.1.jar")));

                // 第二次检查应该干净
                RepairReport again = await repairer.InspectAsync(root, "Test", CancellationToken.None);
                Assert.Equal(0, again.Missing);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Repair_replaces_a_corrupted_library_with_a_matching_sha1()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-repair-" + Guid.NewGuid().ToString("N"));
            string versions = Path.Combine(root, "versions", "Test");
            Directory.CreateDirectory(versions);

            const string expected = "expected-library-content";
            string sha1;
            using (SHA1 algorithm = SHA1.Create())
            {
                sha1 = BitConverter.ToString(
                    algorithm.ComputeHash(Encoding.UTF8.GetBytes(expected)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }

            File.WriteAllText(
                Path.Combine(versions, "Test.json"),
                @"{
                    ""id"": ""Test"",
                    ""libraries"": [
                        { ""downloads"": { ""artifact"": {
                            ""path"": ""com/example/lib/1.0/lib-1.0.jar"",
                            ""url"": ""https://example.invalid/lib.jar"",
                            ""sha1"": """ + sha1 + @"""
                        } } }
                    ]
                }");

            string target = Path.Combine(root, "libraries", "com", "example", "lib", "1.0", "lib-1.0.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "corrupted");

            try
            {
                VersionRepairer repairer = new VersionRepairer(new FakeFiles { Content = expected });

                RepairReport inspected = await repairer.InspectAsync(root, "Test", CancellationToken.None);
                Assert.Equal(1, inspected.Missing);

                RepairReport repaired = await repairer.RepairAsync(root, "Test", null, CancellationToken.None);
                Assert.Equal(1, repaired.Repaired);
                Assert.Equal(expected, File.ReadAllText(target));

                RepairReport again = await repairer.InspectAsync(root, "Test", CancellationToken.None);
                Assert.Equal(0, again.Missing);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Windows_rules_are_respected()
        {
            JToken allowed = JToken.Parse(@"{ ""name"": ""x"", ""rules"": [ { ""action"": ""allow"", ""os"": { ""name"": ""windows"" } } ] }");
            JToken denied = JToken.Parse(@"{ ""name"": ""x"", ""rules"": [ { ""action"": ""allow"", ""os"": { ""name"": ""linux"" } } ] }");
            JToken noRules = JToken.Parse(@"{ ""name"": ""x"" }");

            Assert.True(VersionRepairer.IsAllowedOnWindows(allowed));
            Assert.False(VersionRepairer.IsAllowedOnWindows(denied));
            Assert.True(VersionRepairer.IsAllowedOnWindows(noRules));
        }
    }
}

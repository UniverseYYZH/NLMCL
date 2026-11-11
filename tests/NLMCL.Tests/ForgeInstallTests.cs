using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>Forge：构建列表解析、安装器流程、Java 版本要求。</summary>
    public class ForgeInstallTests
    {
        [Fact]
        public void Parses_and_sorts_forge_builds_newest_first()
        {
            const string json = @"[
                { ""version"": ""47.0.1"", ""mcversion"": ""1.20.1"" },
                { ""version"": ""47.10.2"", ""mcversion"": ""1.20.1"" },
                { ""version"": ""47.4.27"", ""mcversion"": ""1.20.1"" }
            ]";

            IReadOnlyList<LoaderBuild> builds = ForgeCatalog.Parse(json, "1.20.1");

            Assert.Equal(3, builds.Count);
            Assert.Equal("47.10.2", builds[0].Version); // 数字比较，不是字符串比较
            Assert.Equal("47.4.27", builds[1].Version);
            Assert.Equal("47.0.1", builds[2].Version);
        }

        [Fact]
        public void Parses_official_maven_metadata_for_the_requested_minecraft_version()
        {
            const string xml = @"<metadata>
  <versioning>
    <versions>
      <version>1.20.1-47.0.1</version>
      <version>1.20.1-47.10.2</version>
      <version>1.20.1-47.4.27</version>
      <version>1.20.2-48.0.1</version>
    </versions>
  </versioning>
</metadata>";

            IReadOnlyList<LoaderBuild> builds =
                ForgeCatalog.ParseOfficialMetadata(xml, "1.20.1");

            Assert.Equal(3, builds.Count);
            Assert.Equal("47.10.2", builds[0].Version);
            Assert.Equal("47.4.27", builds[1].Version);
            Assert.Equal("47.0.1", builds[2].Version);
            Assert.All(builds, build => Assert.Equal("1.20.1", build.MinecraftVersion));
        }

        [Fact]
        public void Launcher_profiles_is_created_when_missing()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-forge-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(root);
                ForgeLoaderInstaller.EnsureLauncherProfiles(root);

                string path = Path.Combine(root, "launcher_profiles.json");
                Assert.True(File.Exists(path));
                Assert.Contains("profiles", File.ReadAllText(path));

                // 已存在时不能覆盖（里面可能有用户的真实配置）
                File.WriteAllText(path, "KEEP");
                ForgeLoaderInstaller.EnsureLauncherProfiles(root);
                Assert.Equal("KEEP", File.ReadAllText(path));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class FakeDownload : DownloadServiceStub
        {
            public string? LastUrl { get; private set; }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                LastUrl = url;
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                // 安装器要求 > 1MB 且是合法 jar（PK 魔数），这里造一个 2MB 的假 jar
                byte[] fake = new byte[2 * 1024 * 1024];
                fake[0] = 0x50;
                fake[1] = 0x4B;
                fake[2] = 0x03;
                fake[3] = 0x04;
                File.WriteAllBytes(localPath, fake);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeProcess : IProcessRunner
        {
            public string? Arguments { get; private set; }

            public string? WorkingDirectory { get; private set; }

            public int ExitCode { get; set; }

            public Task<ProcessResult> RunAsync(
                string executable, string arguments, string workingDirectory,
                Action<string>? onOutput, CancellationToken cancellationToken)
            {
                Arguments = arguments;
                WorkingDirectory = workingDirectory;

                onOutput?.Invoke("Patching something 1/1");

                // 模拟安装器产物
                string id = "1.20.1-forge-47.9.9";
                string dir = Path.Combine(workingDirectory, "versions", id);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, id + ".json"), "{}");

                return Task.FromResult(new ProcessResult(ExitCode, "done"));
            }
        }

        [Fact]
        public async Task Installer_runs_java_and_returns_version_id()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-forge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                string cache = Path.Combine(root, "cache");
                FakeDownload download = new FakeDownload();
                FakeProcess process = new FakeProcess();
                ForgeLoaderInstaller installer = new ForgeLoaderInstaller(download, process, cache);

                string id = await installer.InstallForgeAsync(
                    "1.20.1", "47.9.9", root, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);

                Assert.Equal("1.20.1-forge-47.9.9", id);
                Assert.Contains("--installClient", process.Arguments);
                Assert.Contains("forge-1.20.1-47.9.9-installer.jar", download.LastUrl);
                Assert.True(File.Exists(Path.Combine(root, "launcher_profiles.json")));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Nonzero_exit_is_reported_with_output_tail()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-forge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                FakeProcess process = new FakeProcess { ExitCode = 1 };
                ForgeLoaderInstaller installer = new ForgeLoaderInstaller(
                    new FakeDownload(), process, Path.Combine(root, "cache"));

                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    installer.InstallForgeAsync(
                        "1.20.1", "47.4.27", root, @"C:\Java\bin\javaw.exe", null, CancellationToken.None));

                Assert.Contains("退出码 1", error.Message);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Corrupted_cached_installer_is_replaced()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-forge-" + Guid.NewGuid().ToString("N"));
            string cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(cache);

            try
            {
                // 伪造一个"体积够大但内容全零"的坏缓存（就是之前 47.4.27 失败的原因）
                string bad = Path.Combine(cache, "1.20.1-forge-47.9.9-installer.jar");
                File.WriteAllBytes(bad, new byte[2 * 1024 * 1024]);

                Assert.False(ForgeLoaderInstaller.IsValidJar(bad));

                FakeDownload download = new FakeDownload();
                ForgeLoaderInstaller installer = new ForgeLoaderInstaller(
                    download, new FakeProcess(), cache);

                string id = await installer.InstallForgeAsync(
                    "1.20.1", "47.9.9", root, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);

                Assert.Equal("1.20.1-forge-47.9.9", id);
                Assert.NotNull(download.LastUrl); // 说明缓存被判坏并重新下载了
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Theory]
        [InlineData("1.20.1", 17)]
        [InlineData("1.20.4", 17)]
        [InlineData("1.20.6", 21)]
        [InlineData("1.21.2", 21)]
        public void Required_java_matches_minecraft_version(string mc, int expected)
        {
            Assert.Equal(expected, DownloadViewModel.RequiredJavaFor(mc));
        }
    }
}

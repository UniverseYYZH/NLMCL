using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// Java 运行时下载：用固定清单夹具验证「解析 → 落盘 → 识别版本」，
    /// 不发真实网络请求。
    /// </summary>
    public class JavaRuntimeInstallerTests
    {
        private const string IndexJson = @"{
            ""windows-x64"": {
                ""java-runtime-gamma"": [
                    { ""manifest"": { ""url"": ""https://example.invalid/gamma.json"" } }
                ]
            }
        }";

        private const string PlatformManifest = @"{
            ""files"": {
                ""release"": {
                    ""type"": ""file"",
                    ""downloads"": { ""raw"": { ""url"": ""https://example.invalid/release"" } }
                },
                ""bin/javaw.exe"": {
                    ""type"": ""file"",
                    ""downloads"": { ""raw"": { ""url"": ""https://example.invalid/javaw"" } }
                },
                ""bin"": { ""type"": ""directory"" },
                ""lib/targets.txt"": { ""type"": ""link"" }
            }
        }";

        private sealed class FakeDownload : DownloadServiceStub
        {
            public int Downloads { get; private set; }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                if (string.Equals(url, JavaRuntimeInstaller.ManifestUrl, StringComparison.Ordinal))
                {
                    return Task.FromResult(IndexJson);
                }

                if (url.EndsWith("gamma.json", StringComparison.Ordinal))
                {
                    return Task.FromResult(PlatformManifest);
                }

                throw new InvalidOperationException("意外的 URL：" + url);
            }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                Downloads++;
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                if (url.EndsWith("release", StringComparison.Ordinal))
                {
                    File.WriteAllText(localPath, "JAVA_VERSION=\"21.0.7\"", Encoding.ASCII);
                }
                else
                {
                    File.WriteAllBytes(localPath, new byte[] { 0x4D, 0x5A });
                }

                return Task.CompletedTask;
            }
        }

        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-jre-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Installs_runtime_under_minecraft_runtime_folder()
        {
            string root = NewRoot();
            Directory.CreateDirectory(root);

            try
            {
                AppPaths paths = new AppPaths(root);
                FakeDownload download = new FakeDownload();
                JavaRuntimeInstaller installer = new JavaRuntimeInstaller(download, paths);

                JavaRuntime java = await installer.InstallAsync(
                    "java-runtime-gamma", null, CancellationToken.None);

                string expectedHome = Path.Combine(
                    paths.DefaultMinecraftDirectory, "runtime", "java-runtime-gamma");

                Assert.Equal(expectedHome + Path.DirectorySeparatorChar, java.Home);
                Assert.Equal(21, java.Major); // 来自 release 文件，而不是猜的
                Assert.True(File.Exists(Path.Combine(expectedHome, "release")));
                Assert.True(File.Exists(Path.Combine(expectedHome, "bin", "javaw.exe")));

                // directory / link 条目不应被下载
                Assert.Equal(2, download.Downloads);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Second_run_skips_existing_files()
        {
            string root = NewRoot();
            Directory.CreateDirectory(root);

            try
            {
                FakeDownload download = new FakeDownload();
                JavaRuntimeInstaller installer = new JavaRuntimeInstaller(download, new AppPaths(root));

                await installer.InstallAsync("java-runtime-gamma", null, CancellationToken.None);
                int first = download.Downloads;

                await installer.InstallAsync("java-runtime-gamma", null, CancellationToken.None);

                Assert.Equal(first, download.Downloads); // 文件都在，不该重下
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Throws_when_component_is_missing_from_manifest()
        {
            Assert.Throws<InvalidDataException>(() =>
                JavaRuntimeInstaller.ResolvePlatformManifest(IndexJson, "java-runtime-nonexistent"));
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

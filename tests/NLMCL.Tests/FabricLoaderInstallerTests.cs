using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public class FabricLoaderInstallerTests
    {
        private const string Profile = @"{
            ""id"": ""fabric-loader-0.16.9-1.21.1"",
            ""inheritsFrom"": ""1.21.1"",
            ""releaseTime"": ""2024-10-01T00:00:00+00:00"",
            ""type"": ""release"",
            ""mainClass"": ""net.fabricmc.loader.impl.launch.knot.KnotClient"",
            ""libraries"": [
                { ""name"": ""org.ow2.asm:asm:9.6"", ""url"": ""https://maven.fabricmc.net/"" },
                { ""name"": ""net.fabricmc:sponge-mixin:0.15.2+mixin.0.8.7"", ""url"": ""https://maven.fabricmc.net/"" }
            ]
        }";

        private sealed class FakeDownload : DownloadServiceStub
        {
            private readonly string _profile;

            public FakeDownload(string profile)
            {
                _profile = profile;
            }

            public List<string> Files { get; } = new List<string>();

            /// <summary>可选：原版清单（用于测试"父版本缺失时先装原版"）。</summary>
            public string? Manifest { get; set; }

            /// <summary>可选：原版版本 JSON。</summary>
            public string? VanillaJson { get; set; }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.WriteAllBytes(localPath, Encoding.UTF8.GetBytes("jar:" + url));
                Files.Add(localPath);
                return Task.CompletedTask;
            }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                if (Manifest != null && url.IndexOf("version_manifest", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(Manifest);
                }

                if (VanillaJson != null && url.EndsWith(".json", StringComparison.Ordinal)
                    && url.IndexOf("/profile/json", StringComparison.Ordinal) < 0)
                {
                    return Task.FromResult(VanillaJson);
                }

                if (url.IndexOf("/profile/json", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException("意外的 URL：" + url);
                }

                return Task.FromResult(_profile);
            }
        }

        private sealed class FailingDownload : DownloadServiceStub
        {
            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("PCL ModNet 不可用：" + url);
            }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("PCL ModNet 不可用：" + url);
            }
        }

        private sealed class DirectDownload : IFileDownloader
        {
            private readonly string _profile;

            public DirectDownload(string profile)
            {
                _profile = profile;
            }

            public List<string> Files { get; } = new List<string>();

            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "direct:" + url);
                Files.Add(targetPath);
                return Task.CompletedTask;
            }

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                progress?.Report(1);
                return DownloadAsync(url, targetPath, cancellationToken);
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                return Task.FromResult(_profile);
            }
        }

        private static string NewRoot() => Path.Combine(Path.GetTempPath(), "nlmcl-fabric-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Installs_version_json_and_libraries()
        {
            string root = NewRoot();
            WriteParent(root);
            FakeDownload download = new FakeDownload(Profile);
            FabricLoaderInstaller installer = new FabricLoaderInstaller(
                download, new VanillaVersionInstaller(download));
            RecordingProgress progress = new RecordingProgress();

            try
            {
                await installer.InstallAsync(
                    new InstallRequest(root, "1.21.1") { Loader = "fabric", LoaderVersion = "0.16.9" },
                    progress,
                    CancellationToken.None);

                // 1) 版本 JSON 落盘，且保留了 inheritsFrom（启动时 PCL 靠它合并原版）
                string json = Path.Combine(root, "versions", "fabric-loader-0.16.9-1.21.1", "fabric-loader-0.16.9-1.21.1.json");
                Assert.True(File.Exists(json), "应写入版本 JSON：" + json);
                string content = File.ReadAllText(json);
                Assert.Contains("inheritsFrom", content);
                Assert.Contains("fabric-loader-0.16.9-1.21.1", content);

                // 2) 库按 Maven 坐标落到 libraries 下
                string asm = Path.Combine(root, "libraries", "org", "ow2", "asm", "asm", "9.6", "asm-9.6.jar");
                Assert.True(File.Exists(asm), "应下载库文件：" + asm);

                string mixin = Path.Combine(root, "libraries", "net", "fabricmc", "sponge-mixin",
                    "0.15.2+mixin.0.8.7", "sponge-mixin-0.15.2+mixin.0.8.7.jar");
                Assert.True(File.Exists(mixin), "应处理带 + 的版本号：" + mixin);

                // 3) 进度单调到达 100
                Assert.NotEmpty(progress.Items);
                Assert.Equal(100, progress.Items[progress.Items.Count - 1].Percent);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Falls_back_to_direct_http_when_pcl_download_fails()
        {
            string root = NewRoot();
            WriteParent(root);
            DirectDownload fallback = new DirectDownload(Profile);
            FabricLoaderInstaller installer = new FabricLoaderInstaller(
                new FailingDownload(), new VanillaVersionInstaller(new FailingDownload()), fallback);

            try
            {
                await installer.InstallAsync(
                    new InstallRequest(root, "1.21.1") { Loader = "fabric", LoaderVersion = "0.16.9" },
                    new RecordingProgress(),
                    CancellationToken.None);

                string json = Path.Combine(root, "versions", "fabric-loader-0.16.9-1.21.1", "fabric-loader-0.16.9-1.21.1.json");
                Assert.True(File.Exists(json), "元数据回退后仍应写入版本 JSON：" + json);
                Assert.True(File.Exists(Path.Combine(root, "libraries", "org", "ow2", "asm", "asm", "9.6", "asm-9.6.jar")));
                Assert.True(fallback.Files.Count >= 2, "库应由直连下载器补齐");
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Skips_already_valid_files()
        {
            string root = NewRoot();
            WriteParent(root);
            FakeDownload download = new FakeDownload(Profile);
            FabricLoaderInstaller installer = new FabricLoaderInstaller(
                download, new VanillaVersionInstaller(download));

            try
            {
                InstallRequest request = new InstallRequest(root, "1.21.1")
                {
                    Loader = "fabric",
                    LoaderVersion = "0.16.9"
                };

                await installer.InstallAsync(request, new RecordingProgress(), CancellationToken.None);
                int firstRun = download.Files.Count;

                // 第二次：文件已存在且长度大于 0（profile 不带 sha1/size），应被判定为已就绪
                await installer.InstallAsync(request, new RecordingProgress(), CancellationToken.None);

                Assert.Equal(firstRun, download.Files.Count);
                Assert.True(firstRun > 0);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Rejects_mismatched_inherits_from()
        {
            string root = NewRoot();
            const string badProfile = @"{ ""id"": ""x"", ""inheritsFrom"": ""1.20.1"", ""libraries"": [] }";
            FabricLoaderInstaller installer = new FabricLoaderInstaller(
                new FakeDownload(badProfile), new VanillaVersionInstaller(new FakeDownload(badProfile)));

            try
            {
                InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    installer.InstallAsync(
                        new InstallRequest(root, "1.21.1") { Loader = "fabric", LoaderVersion = "0.16.9" },
                        new RecordingProgress(),
                        CancellationToken.None));

                Assert.Contains("inheritsFrom", ex.Message);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Requires_loader_version()
        {
            FabricLoaderInstaller installer = new FabricLoaderInstaller(
                new FakeDownload(Profile), new VanillaVersionInstaller(new FakeDownload(Profile)));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                installer.InstallAsync(
                    new InstallRequest(@"C:\Temp\x", "1.21.1") { Loader = "fabric" },
                    new RecordingProgress(),
                    CancellationToken.None));
        }

        [Fact]
        public void Maven_coordinates_become_repository_paths()
        {
            Assert.Equal("org/ow2/asm/asm/9.6/asm-9.6.jar",
                FabricLoaderInstaller.MavenPath("org.ow2.asm:asm:9.6"));

            Assert.Equal("net/fabricmc/sponge-mixin/0.15.2+mixin.0.8.7/sponge-mixin-0.15.2+mixin.0.8.7.jar",
                FabricLoaderInstaller.MavenPath("net.fabricmc:sponge-mixin:0.15.2+mixin.0.8.7"));

            // 带 classifier 的坐标
            Assert.Equal("net/java/dev/jna/jna/5.13.0/jna-5.13.0-natives-windows.jar",
                FabricLoaderInstaller.MavenPath("net.java.dev.jna:jna:5.13.0:natives-windows"));

            Assert.Null(FabricLoaderInstaller.MavenPath("bad-coordinates"));
        }

        /// <summary>
        /// 回归：Fabric profile 只有加载器，不带原版（client jar / 库 / 资源）。
        /// 父版本缺失时直接落 JSON，得到的实例能被扫到、点启动却起不来。
        /// </summary>
        [Fact]
        public async Task Installs_parent_vanilla_first_when_missing()
        {
            string root = NewRoot();
            FakeDownload download = new FakeDownload(Profile)
            {
                Manifest = @"{ ""versions"": [ { ""id"": ""1.21.1"", ""url"": ""https://example.invalid/1.21.1.json"" } ] }",
                VanillaJson = @"{ ""id"": ""1.21.1"", ""type"": ""release"" }"
            };

            FabricLoaderInstaller installer = new FabricLoaderInstaller(
                download, new VanillaVersionInstaller(download));

            try
            {
                await installer.InstallAsync(
                    new InstallRequest(root, "1.21.1") { Loader = "fabric", LoaderVersion = "0.16.9" },
                    new RecordingProgress(),
                    CancellationToken.None);

                Assert.True(
                    File.Exists(Path.Combine(root, "versions", "1.21.1", "1.21.1.json")),
                    "应先把原版父版本装好");

                Assert.True(
                    File.Exists(Path.Combine(
                        root, "versions", "fabric-loader-0.16.9-1.21.1",
                        "fabric-loader-0.16.9-1.21.1.json")),
                    "应写入 Fabric 版本 JSON");
            }
            finally
            {
                TryDelete(root);
            }
        }

        /// <summary>父版本（原版）已经存在时，Fabric 安装不该再触发一次原版安装。</summary>
        private static void WriteParent(string root, string versionId = "1.21.1")
        {
            string directory = Path.Combine(root, "versions", versionId);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, versionId + ".json"),
                "{\"id\":\"" + versionId + "\",\"type\":\"release\"}");
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

    public class CompositeVersionInstallerTests
    {
        private sealed class CountingInstaller : IVersionInstaller
        {
            public int Calls { get; private set; }

            public InstallRequest? Last { get; private set; }

            public Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress,
                CancellationToken cancellationToken)
            {
                Calls++;
                Last = request;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Routes_vanilla_and_fabric()
        {
            CountingInstaller vanilla = new CountingInstaller();
            CountingInstaller fabric = new CountingInstaller();
            CompositeVersionInstaller composite = new CompositeVersionInstaller(vanilla, fabric);

            await composite.InstallAsync(new InstallRequest(@"C:\m", "1.21.1"), new RecordingProgress(), CancellationToken.None);
            Assert.Equal(1, vanilla.Calls);
            Assert.Equal(0, fabric.Calls);

            await composite.InstallAsync(
                new InstallRequest(@"C:\m", "1.21.1") { Loader = "Fabric", LoaderVersion = "0.16.9" },
                new RecordingProgress(),
                CancellationToken.None);
            Assert.Equal(1, fabric.Calls);

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                composite.InstallAsync(
                    new InstallRequest(@"C:\m", "1.21.1") { Loader = "forge" },
                    new RecordingProgress(),
                    CancellationToken.None));
        }
    }

    /// <summary>记录进度汇报，供多个测试类共用。</summary>
    internal sealed class RecordingProgress : IProgress<InstallProgress>
    {
        public List<InstallProgress> Items { get; } = new List<InstallProgress>();

        public void Report(InstallProgress value)
        {
            Items.Add(value);
        }
    }
}

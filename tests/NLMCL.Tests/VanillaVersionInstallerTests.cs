using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 安装编排器的确定性测试：用内存假下载器喂一份合成的版本清单，
    /// 不访问网络、不依赖真实 Mojang 数据。
    /// </summary>
    public class VanillaVersionInstallerTests : IDisposable
    {
        private static readonly byte[] JarBytes = { 1, 2, 3 };
        private static readonly byte[] LibBytes = { 4, 5, 6 };
        private static readonly byte[] WinLibBytes = { 7, 8, 9 };
        private static readonly byte[] AssetBytes = { 10, 11, 12 };

        private readonly string _root;

        public VanillaVersionInstallerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "nlmcl-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private sealed class FakeDownloadService : DownloadServiceStub, IByteProgressFileDownloader
        {
            public readonly Dictionary<string, byte[]> Content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public readonly List<string> Requested = new List<string>();

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                Requested.Add(url);
                if (!Content.TryGetValue(url, out byte[]? bytes))
                {
                    throw new FileNotFoundException("测试未提供该地址：" + url, url);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.WriteAllBytes(localPath, bytes);
                return Task.CompletedTask;
            }

            public Task DownloadAsync(
                string url,
                string localPath,
                IProgress<FileDownloadProgress>? progress,
                CancellationToken cancellationToken)
            {
                int size = Content.TryGetValue(url, out byte[]? bytes) ? bytes.Length : 0;
                progress?.Report(new FileDownloadProgress(size, size));
                return DownloadFileAsync(url, localPath, cancellationToken);
            }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                Requested.Add(url);
                if (!Content.TryGetValue(url, out byte[]? bytes))
                {
                    throw new FileNotFoundException("测试未提供该地址：" + url, url);
                }

                return Task.FromResult(Encoding.UTF8.GetString(bytes));
            }
        }

        private sealed class ProgressCollector : IProgress<InstallProgress>
        {
            public readonly List<InstallProgress> Items = new List<InstallProgress>();

            public void Report(InstallProgress value)
            {
                Items.Add(value);
            }
        }

        private static string Sha1Hex(byte[] data)
        {
            using (SHA1 sha = SHA1.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private const string ManifestUrl = "https://nlmcl.test/manifest.json";
        private const string VersionJsonUrl = "https://nlmcl.test/versions/1.21.1.json";
        private const string JarUrl = "https://nlmcl.test/client.jar";
        private const string LibUrl = "https://nlmcl.test/lib.jar";
        private const string WinLibUrl = "https://nlmcl.test/winlib.jar";
        private const string IndexUrl = "https://nlmcl.test/assets/index.json";
        private const string ResourcesBase = "https://nlmcl.test/resources";

        private FakeDownloadService BuildFixture()
        {
            string assetHash = Sha1Hex(AssetBytes);

            // 索引内容必须先定下来，才能算出它的哈希写进版本 JSON。
                        string assetIndex = "{\"objects\":{\"minecraft/sounds/test.ogg\":{\"hash\":\"" + assetHash + "\",\"size\":3}}}";
            string indexHash = Sha1Hex(Encoding.UTF8.GetBytes(assetIndex));

            string manifest = "{\"versions\":[{\"id\":\"1.21.1\",\"url\":\"" + VersionJsonUrl + "\"}]}";

            string versionJson = @"{
                ""id"": ""1.21.1"",
                ""downloads"": { ""client"": { ""url"": """ + JarUrl + @""", ""sha1"": """ + Sha1Hex(JarBytes) + @""", ""size"": 3 } },
                ""assetIndex"": { ""id"": ""5"", ""url"": """ + IndexUrl + @""", ""sha1"": """ + indexHash + @""" },
                ""libraries"": [
                    { ""name"": ""a:b:1"", ""downloads"": { ""artifact"": { ""url"": """ + LibUrl + @""", ""sha1"": """ + Sha1Hex(LibBytes) + @""", ""size"": 3, ""path"": ""a/b/1/b-1.jar"" } } },
                    { ""name"": ""mac:only:1"", ""rules"": [ { ""action"": ""allow"", ""os"": { ""name"": ""osx"" } } ], ""downloads"": { ""artifact"": { ""url"": ""https://nlmcl.test/mac.jar"", ""path"": ""mac/mac.jar"" } } },
                    { ""name"": ""win:only:1"", ""rules"": [ { ""action"": ""allow"", ""os"": { ""name"": ""windows"" } } ], ""downloads"": { ""artifact"": { ""url"": """ + WinLibUrl + @""", ""sha1"": """ + Sha1Hex(WinLibBytes) + @""", ""size"": 3, ""path"": ""win/win.jar"" } } }
                ]
              }";

            FakeDownloadService fake = new FakeDownloadService();
            fake.Content[ManifestUrl] = Encoding.UTF8.GetBytes(manifest);
            fake.Content[VersionJsonUrl] = Encoding.UTF8.GetBytes(versionJson);
            fake.Content[JarUrl] = JarBytes;
            fake.Content[LibUrl] = LibBytes;
            fake.Content[WinLibUrl] = WinLibBytes;
            fake.Content[IndexUrl] = Encoding.UTF8.GetBytes(assetIndex);
            fake.Content[ResourcesBase + "/" + assetHash.Substring(0, 2) + "/" + assetHash] = AssetBytes;

            return fake;
        }

        private InstallOptions Options()
        {
            return new InstallOptions
            {
                ManifestUrl = ManifestUrl,
                ResourcesBaseUrl = ResourcesBase
            };
        }

        [Fact]
        public async Task Installs_vanilla_version_end_to_end()
        {
            FakeDownloadService fake = BuildFixture();
            VanillaVersionInstaller installer = new VanillaVersionInstaller(fake, Options());
            ProgressCollector progress = new ProgressCollector();

            await installer.InstallAsync(
                new InstallRequest(_root, "1.21.1"), progress, CancellationToken.None);

            string versionDir = Path.Combine(_root, "versions", "1.21.1");

            // 版本 JSON
            Assert.True(File.Exists(Path.Combine(versionDir, "1.21.1.json")));

            // 客户端 JAR
            Assert.Equal(JarBytes, File.ReadAllBytes(Path.Combine(versionDir, "1.21.1.jar")));

            // 库（含 windows 规则的那一个）
            Assert.Equal(LibBytes, File.ReadAllBytes(Path.Combine(_root, "libraries", "a", "b", "1", "b-1.jar")));
            Assert.True(File.Exists(Path.Combine(_root, "libraries", "win", "win.jar")));

            // 非本机平台的库必须被跳过
            Assert.DoesNotContain("https://nlmcl.test/mac.jar", fake.Requested);

            // 资源索引与资源对象
            Assert.True(File.Exists(Path.Combine(_root, "assets", "indexes", "5.json")));
            string assetHash = Sha1Hex(AssetBytes);
            Assert.Equal(
                AssetBytes,
                File.ReadAllBytes(Path.Combine(_root, "assets", "objects", assetHash.Substring(0, 2), assetHash)));

            // 进度：单调不减，且最终到 100
            Assert.NotEmpty(progress.Items);
            double last = -1;
            foreach (InstallProgress item in progress.Items)
            {
                Assert.True(item.Percent >= last, "进度出现回退：" + item.Percent + " < " + last);
                last = item.Percent;
            }

            Assert.Equal(100d, last);
            Assert.Contains(progress.Items, item => item.BytesTotal > 0 && item.BytesReceived > 0);
        }

        [Fact]
        public async Task Second_run_downloads_nothing_because_hashes_match()
        {
            FakeDownloadService fake = BuildFixture();
            VanillaVersionInstaller installer = new VanillaVersionInstaller(fake, Options());

            await installer.InstallAsync(new InstallRequest(_root, "1.21.1"), new ProgressCollector(), CancellationToken.None);
            int afterFirst = fake.Requested.Count;

            await installer.InstallAsync(new InstallRequest(_root, "1.21.1"), new ProgressCollector(), CancellationToken.None);

            // 第二轮只应重新取清单与版本 JSON（很小），其余文件按哈希跳过
            int delta = fake.Requested.Count - afterFirst;
            Assert.True(delta <= 2, "第二轮仍下载了 " + delta + " 个文件，应只有清单与版本 JSON。");
        }

        [Fact]
        public async Task Rejects_loader_requests_instead_of_silently_installing_vanilla()
        {
            FakeDownloadService fake = BuildFixture();
            VanillaVersionInstaller installer = new VanillaVersionInstaller(fake, Options());

            InstallRequest request = new InstallRequest(_root, "1.21.1")
            {
                Loader = "fabric",
                LoaderVersion = "0.16.9"
            };

            await Assert.ThrowsAsync<NotSupportedException>(
                () => installer.InstallAsync(request, new ProgressCollector(), CancellationToken.None));
        }

        [Fact]
        public async Task Unknown_version_reports_clear_error()
        {
            FakeDownloadService fake = BuildFixture();
            VanillaVersionInstaller installer = new VanillaVersionInstaller(fake, Options());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => installer.InstallAsync(new InstallRequest(_root, "9.9.9"), new ProgressCollector(), CancellationToken.None));
        }

        [Fact]
        public async Task Hash_mismatch_is_rejected_and_leaves_no_partial_file()
        {
            FakeDownloadService fake = BuildFixture();
            // 篡改客户端 JAR 内容，让校验失败
            fake.Content[JarUrl] = new byte[] { 99, 99, 99 };

            VanillaVersionInstaller installer = new VanillaVersionInstaller(fake, Options());

            await Assert.ThrowsAsync<InvalidDataException>(
                () => installer.InstallAsync(new InstallRequest(_root, "1.21.1"), new ProgressCollector(), CancellationToken.None));

            string jar = Path.Combine(_root, "versions", "1.21.1", "1.21.1.jar");
            Assert.False(File.Exists(jar), "校验失败后不应留下正式文件。");
            Assert.False(File.Exists(jar + ".nlmcl-tmp"), "校验失败后不应留下临时文件。");
        }
    }
}

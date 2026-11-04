using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using NLMCL.Abstractions;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>资源安装的哈希校验：正确通过、错误重试后报错、无哈希时只看非空。</summary>
    public class ResourceChecksumTests
    {
        private sealed class FakeDownload : DownloadServiceStub
        {
            private readonly byte[] _content;

            public FakeDownload(byte[] content)
            {
                _content = content;
            }

            public int Calls { get; private set; }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                Calls++;
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.WriteAllBytes(localPath, _content);
                return Task.CompletedTask;
            }
        }

        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-checksum-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string Sha1Of(byte[] content)
        {
            string file = Path.Combine(Path.GetTempPath(), "sha-" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(file, content);
            string hash = CryptographyUtils.ComputeFileHash(file, CryptographyUtils.HashMethod.Sha1);
            File.Delete(file);
            return hash;
        }

        [Fact]
        public async Task Matching_hash_is_accepted()
        {
            string root = NewRoot();
            byte[] content = Encoding.UTF8.GetBytes("good jar content");

            try
            {
                FakeDownload download = new FakeDownload(content);
                ResourceInstaller installer = new ResourceInstaller(download);

                ResourceFile file = new ResourceFile(
                    "sodium.jar", "https://cdn/sodium.jar", content.Length, "0.5.13",
                    new[] { "1.20.1" }, "fabric", Sha1Of(content));

                string target = await installer.InstallAsync(
                    file, ResourceKind.Mod, root, string.Empty, CancellationToken.None);

                Assert.True(File.Exists(target));
                Assert.Equal(1, download.Calls);
                Assert.False(File.Exists(target + ".nlmcl-tmp"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Wrong_hash_retries_then_fails_with_clear_message()
        {
            string root = NewRoot();
            byte[] content = Encoding.UTF8.GetBytes("truncated");

            try
            {
                FakeDownload download = new FakeDownload(content);
                ResourceInstaller installer = new ResourceInstaller(download);

                ResourceFile file = new ResourceFile(
                    "sodium.jar", "https://cdn/sodium.jar", content.Length, "0.5.13",
                    new[] { "1.20.1" }, "fabric", "0000000000000000000000000000000000000000");

                InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    installer.InstallAsync(file, ResourceKind.Mod, root, string.Empty, CancellationToken.None));

                Assert.Contains("校验失败", error.Message);
                Assert.Contains("sodium.jar", error.Message);
                Assert.Equal(2, download.Calls); // 重试过一次

                string target = Path.Combine(root, "mods", "sodium.jar");
                Assert.False(File.Exists(target));
                Assert.False(File.Exists(target + ".nlmcl-tmp"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Missing_hash_only_requires_non_empty_file()
        {
            string root = NewRoot();

            try
            {
                FakeDownload download = new FakeDownload(new byte[] { 1, 2, 3 });
                ResourceInstaller installer = new ResourceInstaller(download);

                ResourceFile file = new ResourceFile(
                    "pack.zip", "https://cdn/pack.zip", 3, "1.0",
                    new[] { "1.20.1" }, null, sha1: null);

                string target = await installer.InstallAsync(
                    file, ResourceKind.ResourcePack, root, string.Empty, CancellationToken.None);

                Assert.True(File.Exists(target));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

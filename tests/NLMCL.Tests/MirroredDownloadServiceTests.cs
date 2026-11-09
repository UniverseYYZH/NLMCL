using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 镜像改写规则 + 回退行为。不做真实网络请求。
    /// </summary>
    public class MirroredDownloadServiceTests
    {
        private sealed class RecordingDownload : DownloadServiceStub, IByteProgressFileDownloader
        {
            public List<string> Requested { get; } = new List<string>();

            public Func<string, bool> Fail { get; set; } = _ => false;

            public Task DownloadAsync(
                string url,
                string localPath,
                IProgress<FileDownloadProgress>? progress,
                CancellationToken cancellationToken)
            {
                progress?.Report(new FileDownloadProgress(10, 10));
                return DownloadFileAsync(url, localPath, cancellationToken);
            }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                Requested.Add(url);

                if (Fail(url))
                {
                    throw new InvalidOperationException("模拟失败：" + url);
                }

                return Task.FromResult("ok:" + url);
            }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                Requested.Add(url);

                if (Fail(url))
                {
                    throw new InvalidOperationException("模拟失败：" + url);
                }

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localPath)!);
                System.IO.File.WriteAllText(localPath, "ok:" + url);
                return Task.CompletedTask;
            }

            public override Task<string> PostJsonAsync(string url, string jsonBody,
                IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            {
                Requested.Add(url);
                return Task.FromResult("posted:" + url);
            }
        }

        [Theory]
        [InlineData("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json",
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json")]
        [InlineData("https://piston-meta.mojang.com/v1/products/java-runtime/x/all.json",
            "https://bmclapi2.bangbang93.com/v1/products/java-runtime/x/all.json")]
        [InlineData("https://libraries.minecraft.net/org/ow2/asm/asm/9.6/asm-9.6.jar",
            "https://bmclapi2.bangbang93.com/maven/org/ow2/asm/asm/9.6/asm-9.6.jar")]
        [InlineData("https://meta.fabricmc.net/v2/versions/loader/1.21.2",
            "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.21.2")]
        [InlineData("https://maven.fabricmc.net/net/fabricmc/intermediary/1.21.2/intermediary-1.21.2.jar",
            "https://bmclapi2.bangbang93.com/maven/net/fabricmc/intermediary/1.21.2/intermediary-1.21.2.jar")]
        public void Rewrites_known_mojang_hosts(string official, string expected)
        {
            Assert.Equal(expected, MojangMirror.TryRewrite(official));
        }

        [Fact]
        public void Binary_files_use_official_cdn()
        {
            Assert.Null(MojangMirror.TryRewrite(
                "https://piston-data.mojang.com/v1/objects/ab/cdef/client.jar"));
            Assert.Null(MojangMirror.TryRewrite(
                "https://launcher.mojang.com/v1/objects/ab/cdef/client.jar"));
            Assert.Null(MojangMirror.TryRewrite(
                "https://resources.download.minecraft.net/ab/abcdef"));

            Assert.Equal(
                "https://bmclapi2.bangbang93.com/v1/objects/ab/cdef/client.jar",
                MojangMirror.TryRewriteForFallback(
                    "https://piston-data.mojang.com/v1/objects/ab/cdef/client.jar"));
            Assert.Equal(
                "https://bmclapi2.bangbang93.com/assets/ab/abcdef",
                MojangMirror.TryRewriteForFallback(
                    "https://resources.download.minecraft.net/ab/abcdef"));
        }

        [Fact]
        public async Task Binary_download_tries_official_first_then_mirror()
        {
            RecordingDownload inner = new RecordingDownload
            {
                Fail = url => url.IndexOf("launcher.mojang.com", StringComparison.Ordinal) >= 0
            };
            MirroredDownloadService service = new MirroredDownloadService(inner);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-mirror-binary-" + Guid.NewGuid().ToString("N") + ".jar");

            try
            {
                await service.DownloadFileAsync(
                    "https://launcher.mojang.com/v1/objects/ab/cdef/client.jar",
                    path,
                    CancellationToken.None);

                Assert.Equal(2, inner.Requested.Count);
                Assert.Contains("launcher.mojang.com", inner.Requested[0]);
                Assert.Contains("bmclapi2.bangbang93.com", inner.Requested[1]);
            }
            finally
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
        }

        [Fact]
        public void Leaves_unknown_hosts_alone()
        {
            // 未知域名（例如第三方皮肤站）不被改写
            Assert.Null(MojangMirror.TryRewrite("https://skin.example.cn/api/yggdrasil"));
            Assert.Null(MojangMirror.TryRewrite("not a url"));
        }

        [Fact]
        public async Task Uses_mirror_first()
        {
            RecordingDownload inner = new RecordingDownload();
            MirroredDownloadService service = new MirroredDownloadService(inner);

            string result = await service.GetStringAsync(
                "https://piston-meta.mojang.com/a.json", CancellationToken.None);

            Assert.Equal("ok:https://bmclapi2.bangbang93.com/a.json", result);
            Assert.Single(inner.Requested);
        }

        [Fact]
        public async Task Falls_back_to_official_when_mirror_fails()
        {
            RecordingDownload inner = new RecordingDownload
            {
                Fail = url => url.IndexOf("bmclapi2", StringComparison.Ordinal) >= 0
            };
            MirroredDownloadService service = new MirroredDownloadService(inner);

            string result = await service.GetStringAsync(
                "https://piston-meta.mojang.com/a.json", CancellationToken.None);

            Assert.Equal("ok:https://piston-meta.mojang.com/a.json", result);
            Assert.Equal(2, inner.Requested.Count);
        }

        [Fact]
        public async Task Passes_byte_progress_through_mirror()
        {
            RecordingDownload inner = new RecordingDownload();
            MirroredDownloadService service = new MirroredDownloadService(inner);
            List<FileDownloadProgress> reports = new List<FileDownloadProgress>();

            await ((IByteProgressFileDownloader)service).DownloadAsync(
                "https://piston-meta.mojang.com/a.jar",
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nlmcl-mirror-test.jar"),
                new InlineProgress(reports.Add),
                CancellationToken.None);

            Assert.NotEmpty(reports);
            Assert.Equal(10, reports[0].Received);
            Assert.Equal("https://bmclapi2.bangbang93.com/a.jar", inner.Requested[0]);
        }

        [Fact]
        public async Task Binary_byte_download_falls_back_to_mirror()
        {
            RecordingDownload inner = new RecordingDownload
            {
                Fail = url => url.IndexOf("launcher.mojang.com", StringComparison.Ordinal) >= 0
            };
            MirroredDownloadService service = new MirroredDownloadService(inner);
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-mirror-binary-bytes-" + Guid.NewGuid().ToString("N") + ".jar");

            try
            {
                await ((IByteProgressFileDownloader)service).DownloadAsync(
                    "https://launcher.mojang.com/v1/objects/ab/cdef/client.jar",
                    path,
                    new InlineProgress(_ => { }),
                    CancellationToken.None);

                Assert.Equal(2, inner.Requested.Count);
                Assert.Contains("launcher.mojang.com", inner.Requested[0]);
                Assert.Contains("bmclapi2.bangbang93.com", inner.Requested[1]);
            }
            finally
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
        }

        private sealed class InlineProgress : IProgress<FileDownloadProgress>
        {
            private readonly Action<FileDownloadProgress> _apply;

            public InlineProgress(Action<FileDownloadProgress> apply)
            {
                _apply = apply;
            }

            public void Report(FileDownloadProgress value) => _apply(value);
        }

        [Fact]
        public async Task Post_requests_are_never_mirrored()
        {
            RecordingDownload inner = new RecordingDownload();
            MirroredDownloadService service = new MirroredDownloadService(inner);

            // 登录端点必须打微软自己，不能进镜像
            string result = await service.PostJsonAsync(
                "https://login.live.com/x", "{}", null, CancellationToken.None);

            Assert.Equal("posted:https://login.live.com/x", result);
            Assert.Equal("https://login.live.com/x", inner.Requested[0]);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.Core.Installing;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// 镜像改写规则 + 回退行为。不做真实网络请求。
    /// </summary>
    public class MirroredDownloadServiceTests
    {
        private sealed class RecordingDownload : DownloadServiceStub
        {
            public List<string> Requested { get; } = new List<string>();

            public Func<string, bool> Fail { get; set; } = _ => false;

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
        [InlineData("https://piston-data.mojang.com/v1/objects/ab/cdef/client.jar",
            "https://bmclapi2.bangbang93.com/v1/objects/ab/cdef/client.jar")]
        [InlineData("https://resources.download.minecraft.net/ab/abcdef",
            "https://bmclapi2.bangbang93.com/assets/ab/abcdef")]
        [InlineData("https://libraries.minecraft.net/org/ow2/asm/asm/9.6/asm-9.6.jar",
            "https://bmclapi2.bangbang93.com/maven/org/ow2/asm/asm/9.6/asm-9.6.jar")]
        public void Rewrites_known_mojang_hosts(string official, string expected)
        {
            Assert.Equal(expected, MojangMirror.TryRewrite(official));
        }

        [Fact]
        public void Leaves_unknown_hosts_alone()
        {
            // Fabric meta 不在镜像范围内：硬改只会 404
            Assert.Null(MojangMirror.TryRewrite("https://meta.fabricmc.net/v2/versions/loader/1.20.1"));
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

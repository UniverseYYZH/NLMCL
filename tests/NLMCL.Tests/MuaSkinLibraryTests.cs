using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>MUA / Blessing Skin 皮肤库解析与下载地址。</summary>
    public sealed class MuaSkinLibraryTests
    {
        private const string Json = @"{
            ""current_page"": 2,
            ""last_page"": 3,
            ""total"": 41,
            ""data"": [
                { ""tid"": 6123, ""name"": ""skin_a"", ""type"": ""alex"", ""nickname"": ""alice"", ""likes"": 7 },
                { ""tid"": 6121, ""name"": ""custom"", ""type"": ""steve"", ""nickname"": ""bob"", ""likes"": 1 }
            ]
        }";

        [Fact]
        public async Task Lists_skins_and_builds_download_urls()
        {
            StubDownload download = new StubDownload { Response = Json };
            MuaSkinLibraryService service = new MuaSkinLibraryService(download);

            MuaSkinPage page = await service.ListAsync("https://skin.example.cn/api/yggdrasil", 2, CancellationToken.None);

            Assert.Equal(2, page.Page);
            Assert.Equal(3, page.LastPage);
            Assert.Equal(41, page.Total);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal(6123, page.Items[0].Tid);
            Assert.Equal("slim", page.Items[0].Model);
            Assert.Equal("https://skin.example.cn/raw/6123", page.Items[0].DownloadUrl);
            Assert.Equal("https://skin.example.cn/avatar/6123", page.Items[0].PreviewUrl);
            Assert.Equal("https://skin.example.cn/skinlib/list?page=2", download.LastUrl);
        }

        [Fact]
        public async Task Download_uses_raw_url()
        {
            StubDownload download = new StubDownload();
            MuaSkinLibraryService service = new MuaSkinLibraryService(download);
            MuaSkin skin = new MuaSkin(6123, "skin", "default", "alice", 1,
                "https://skin.example.cn/raw/6123", "https://skin.example.cn/avatar/6123");

            string path = Path.Combine(Path.GetTempPath(), "mua-test.png");

            await service.DownloadAsync(skin, path, CancellationToken.None);

            Assert.Equal("https://skin.example.cn/raw/6123", download.LastDownloadUrl);
            Assert.Equal(path, download.LastDownloadPath);
        }

        [Theory]
        [InlineData("https://skin.mualliance.ltd/api/yggdrasil", "https://skin.mualliance.ltd")]
        [InlineData("https://skin.example.cn/api/yggdrasil/", "https://skin.example.cn")]
        [InlineData("https://skin.example.cn/yggdrasil", "https://skin.example.cn")]
        [InlineData(null, "https://skin.mualliance.ltd")]
        public void Resolves_site_root(string? authServer, string expected)
        {
            Assert.Equal(expected, MuaSkinLibraryService.ResolveSiteRoot(authServer));
        }

        private sealed class StubDownload : DownloadServiceStub
        {
            public string Response { get; set; } = "{}";

            public string? LastUrl { get; private set; }

            public string? LastDownloadUrl { get; private set; }

            public string? LastDownloadPath { get; private set; }

            public override Task<string> GetStringAsync(
                string url, CancellationToken cancellationToken)
            {
                LastUrl = url;
                return Task.FromResult(Response);
            }

            public override Task DownloadFileAsync(
                string url, string localPath, CancellationToken cancellationToken)
            {
                LastDownloadUrl = url;
                LastDownloadPath = localPath;
                return Task.CompletedTask;
            }
        }
    }
}

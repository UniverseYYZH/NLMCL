using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Updates;
using Xunit;

namespace NLMCL.Tests
{
    public class LauncherUpdateServiceTests
    {
        private const string FeedUrl = "https://example.invalid/nlmcl-update.json";

        [Fact]
        public async Task Newer_version_is_reported()
        {
            var download = new FakeDownload(
                "{\"version\":\"1.2.0\",\"notes\":\"修复若干问题\",\"url\":\"https://example.invalid/download\",\"sha256\":\"abc\",\"publishedAt\":\"2026-11-01T00:00:00Z\"}");
            var service = new LauncherUpdateService(download, "1.0.0");

            LauncherUpdateInfo? info = await service.CheckAsync(FeedUrl, CancellationToken.None);

            Assert.NotNull(info);
            Assert.Equal("1.2.0", info!.Version);
            Assert.Equal("https://example.invalid/download", info.DownloadUrl);
            Assert.Equal("abc", info.Sha256);
            Assert.Contains("发现新版本 1.2.0", info.Summary);
        }

        [Theory]
        [InlineData("1.0.0")]
        [InlineData("0.9.9")]
        [InlineData("v1.0.0")]
        public async Task Same_or_older_version_is_not_an_update(string version)
        {
            var download = new FakeDownload(
                "{\"version\":\"" + version + "\",\"url\":\"https://example.invalid/download\"}");
            var service = new LauncherUpdateService(download, "1.0.0");

            Assert.Null(await service.CheckAsync(FeedUrl, CancellationToken.None));
        }

        [Fact]
        public async Task Malformed_feed_is_ignored()
        {
            var download = new FakeDownload("not json");
            var service = new LauncherUpdateService(download, "1.0.0");

            Assert.Null(await service.CheckAsync(FeedUrl, CancellationToken.None));
        }

        [Fact]
        public async Task Offline_feed_is_ignored_without_throwing()
        {
            var download = new FakeDownload(new InvalidOperationException("网络不可用"));
            var service = new LauncherUpdateService(download, "1.0.0");

            Assert.Null(await service.CheckAsync(FeedUrl, CancellationToken.None));
        }

        [Fact]
        public async Task Non_http_download_url_is_rejected()
        {
            var download = new FakeDownload(
                "{\"version\":\"2.0.0\",\"url\":\"file:///C:/Windows/System32/calc.exe\"}");
            var service = new LauncherUpdateService(download, "1.0.0");

            Assert.Null(await service.CheckAsync(FeedUrl, CancellationToken.None));
        }

        [Fact]
        public async Task Invalid_feed_url_is_not_requested()
        {
            var download = new FakeDownload("{\"version\":\"2.0.0\",\"url\":\"https://example.invalid/download\"}");
            var service = new LauncherUpdateService(download, "1.0.0");

            Assert.Null(await service.CheckAsync("file:///C:/temp/update.json", CancellationToken.None));
            Assert.Equal(0, download.Calls);
        }

        [Fact]
        public async Task Version_comparison_uses_semantic_components()
        {
            var download = new FakeDownload(
                "{\"version\":\"1.10.0\",\"url\":\"https://example.invalid/download\"}");
            var service = new LauncherUpdateService(download, "1.9.9");

            Assert.NotNull(await service.CheckAsync(FeedUrl, CancellationToken.None));
        }

        private sealed class FakeDownload : DownloadServiceStub
        {
            private readonly string? _body;
            private readonly Exception? _error;

            public FakeDownload(string body)
            {
                _body = body;
            }

            public FakeDownload(Exception error)
            {
                _error = error;
            }

            public int Calls { get; private set; }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                Calls++;
                if (_error != null)
                {
                    throw _error;
                }

                return Task.FromResult(_body ?? "{}");
            }
        }
    }
}

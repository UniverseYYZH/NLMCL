using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class SkinPresetTests
    {
        [Fact]
        public void Presets_are_bundled_and_extractable()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-preset-" + Guid.NewGuid().ToString("N"));
            SkinService service = new SkinService(new NoopDownload(), new AppPaths(root));

            var presets = service.GetPresets();

            Assert.True(presets.Count >= 2, "至少要预存 Steve / Alex 两个");

            foreach (SkinPreset preset in presets)
            {
                string path = service.GetPresetPath(preset.Id);

                Assert.True(File.Exists(path), preset.Id + " 应能解出 PNG");
                Assert.True(new FileInfo(path).Length > 256, preset.Id + " 不应是空文件");
            }
        }

        [Fact]
        public async Task Applying_a_preset_copies_it_into_the_skin_folder()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-preset-" + Guid.NewGuid().ToString("N"));
            SkinService service = new SkinService(new NoopDownload(), new AppPaths(root));

            string saved = await service.SavePresetAsync("steve", "Tester", CancellationToken.None);

            Assert.True(File.Exists(saved));
            Assert.Contains("preset-steve", saved);
        }

        private sealed class NoopDownload : IDownloadService
        {
            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");

            public Task<string> GetStringAsync(
                string url, System.Collections.Generic.IReadOnlyDictionary<string, string> headers,
                CancellationToken cancellationToken)
                => Task.FromResult("{}");

            public Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> PostJsonAsync(
                string url, string json,
                System.Collections.Generic.IReadOnlyDictionary<string, string>? headers,
                CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            public Task<string> PostFormAsync(
                string url, System.Collections.Generic.IReadOnlyDictionary<string, string> form,
                CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }
    }
}
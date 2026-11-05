using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using System;
using Newtonsoft.Json.Linq;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class CurseForgeInstallerTests
    {
        [Theory]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei", "jei")]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei/files/123456", "jei")]
        [InlineData("https://legacy.curseforge.com/minecraft/mc-mods/the-shooting-star", "the-shooting-star")]
        [InlineData("the-shooting-star", "the-shooting-star")]
        public void Slug_is_extracted_from_url_or_plain_slug(string input, string expected)
        {
            Assert.Equal(expected, CurseForgeInstaller.ExtractSlug(input));
        }

        [Theory]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei", "mc-mods")]
        [InlineData("https://www.curseforge.com/minecraft/modpacks/the-shooting-star", "modpacks")]
        [InlineData("https://www.curseforge.com/minecraft/texture-packs/faithful-32x", "texture-packs")]
        [InlineData("jei", "mc-mods")]
        public void Project_type_is_detected_from_url(string input, string expected)
        {
            Assert.Equal(expected, CurseForgeInstaller.ExtractType(input));
        }

        [Fact]
        public void Direct_download_url_follows_forgecdn_sharding()
        {
            Assert.Equal(
                "https://mediafilez.forgecdn.net/files/9407/746/jei.jar",
                CurseForgeInstaller.BuildDownloadUrl(9407746, "jei.jar"));
        }

        [Fact]
        public void File_picker_prefers_matching_version_and_loader()
        {
            JArray files = JArray.Parse(@"[
                {""id"":1,""name"":""a.jar"",""versions"":[""Forge"",""1.20.1""]},
                {""id"":2,""name"":""b.jar"",""versions"":[""Fabric"",""1.21.1""]},
                {""id"":3,""name"":""c.jar"",""versions"":[""Fabric"",""1.20.1""]}
            ]");

            JObject? picked = CurseForgeInstaller.PickFile(files, "1.21.1", "fabric");
            Assert.NotNull(picked);
            Assert.Equal(2, picked!.Value<int>("id"));

            // 版本对但加载器不对时，退回"只按版本"
            JObject? fallback = CurseForgeInstaller.PickFile(files, "1.20.1", "fabric");
            Assert.NotNull(fallback);
            Assert.Equal(3, fallback!.Value<int>("id"));
        }

        [Fact]
        public async Task Direct_file_install_lands_in_mods_with_own_downloader()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-cf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                RecordingFiles files = new RecordingFiles();
                CurseForgeInstaller installer = new CurseForgeInstaller(new NoopStrings(), files);

                string path = await installer.InstallFileAsync(
                    "https://edge.forgecdn.net/files/1/2/a.jar", "a.jar",
                    NLMCL.Abstractions.ResourceKind.Mod, root, "1.21.1", CancellationToken.None);

                Assert.Equal(1, files.Calls);                       // 走自带下载器，不走 ModNet
                Assert.Contains("mods", path);
                Assert.EndsWith("a.jar", path);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        private sealed class NoopStrings : IDownloadService
        {
            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");

            public Task<string> GetStringAsync(
                string url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
                => Task.FromResult("{}");

            public Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
                => throw new InvalidOperationException("不应该走到 ModNet 这条路");

            public Task<string> PostJsonAsync(
                string url, string json, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            public Task<string> PostFormAsync(
                string url, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }

        private sealed class RecordingFiles : IFileDownloader
        {
            public int Calls { get; private set; }

            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
            {
                Calls++;
                File.WriteAllText(targetPath, "jar");
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }
    }
}
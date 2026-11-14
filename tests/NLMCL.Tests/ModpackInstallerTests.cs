using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>整合包：索引解析、路径安全、overrides、整包安装（全 fake）。</summary>
    public class ModpackInstallerTests
    {
        private const string FabricIndex = @"{
            ""formatVersion"": 1,
            ""name"": ""Fabulously Optimized"",
            ""versionId"": ""5.0.0"",
            ""files"": [
                { ""path"": ""mods/sodium.jar"", ""downloads"": [""https://cdn/sodium.jar""], ""env"": { ""client"": ""required"" } },
                { ""path"": ""mods/serveronly.jar"", ""downloads"": [""https://cdn/server.jar""], ""env"": { ""client"": ""unsupported"" } }
            ],
            ""dependencies"": { ""minecraft"": ""1.20.1"", ""fabric-loader"": ""0.16.9"" }
        }";

        private const string CurseForgeManifest = @"{
            ""manifestType"": ""minecraftModpack"",
            ""manifestVersion"": 1,
            ""name"": ""Curse Test Pack"",
            ""version"": ""1.2.3"",
            ""minecraft"": {
                ""version"": ""1.20.1"",
                ""modLoaders"": [ { ""id"": ""fabric-0.15.11"", ""primary"": true } ]
            },
            ""files"": [
                { ""projectID"": 238222, ""fileID"": 123456, ""required"": true }
            ],
            ""overrides"": ""overrides""
        }";

        [Fact]
        public void Parses_curseforge_manifest()
        {
            ModpackInfo info = ModpackInstaller.Parse(JObject.Parse(CurseForgeManifest));

            Assert.Equal("Curse Test Pack", info.Name);
            Assert.Equal("1.20.1", info.MinecraftVersion);
            Assert.Equal(ModpackLoader.Fabric, info.Loader);
            Assert.Equal("0.15.11", info.LoaderVersion);
            Assert.Equal(1, info.FileCount);
        }

        [Fact]
        public void Parses_fabric_pack()
        {
            ModpackInfo info = ModpackInstaller.Parse(JObject.Parse(FabricIndex));

            Assert.Equal("Fabulously Optimized", info.Name);
            Assert.Equal("1.20.1", info.MinecraftVersion);
            Assert.Equal(ModpackLoader.Fabric, info.Loader);
            Assert.Equal("0.16.9", info.LoaderVersion);
            Assert.Equal(2, info.FileCount);
        }

        [Theory]
        [InlineData(@"{ ""dependencies"": { ""minecraft"": ""1.20.1"" } }", ModpackLoader.Vanilla, null)]
        [InlineData(@"{ ""dependencies"": { ""minecraft"": ""1.20.1"", ""forge"": ""47.2.0"" } }", ModpackLoader.Forge, "47.2.0")]
        [InlineData(@"{ ""dependencies"": { ""minecraft"": ""1.21.1"", ""neoforge"": ""21.1.265"" } }", ModpackLoader.NeoForge, "21.1.265")]
        public void Detects_loader_from_dependencies(string json, ModpackLoader loader, string? version)
        {
            ModpackInfo info = ModpackInstaller.Parse(JObject.Parse(json));

            Assert.Equal(loader, info.Loader);
            Assert.Equal(version, info.LoaderVersion);
        }

        [Fact]
        public void Rejects_pack_without_minecraft_version()
        {
            Assert.Throws<InvalidDataException>(() =>
                ModpackInstaller.Parse(JObject.Parse(@"{ ""dependencies"": {} }")));
        }

        [Fact]
        public void Rejects_quilt_for_now()
        {
            Assert.Throws<NotSupportedException>(() =>
                ModpackInstaller.Parse(JObject.Parse(
                    @"{ ""dependencies"": { ""minecraft"": ""1.20.1"", ""quilt-loader"": ""0.20.0"" } }")));
        }

        [Theory]
        [InlineData("mods/a.jar")]
        [InlineData("config/sub/b.json")]
        public void Safe_combine_allows_paths_inside_root(string relative)
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-mrp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                string result = ModpackInstaller.SafeCombine(root, relative);
                Assert.StartsWith(Path.GetFullPath(root), result, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Theory]
        [InlineData("../escape.txt")]
        [InlineData("mods/../../escape.txt")]
        public void Safe_combine_blocks_path_traversal(string relative)
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-mrp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                Assert.Throws<InvalidDataException>(() => ModpackInstaller.SafeCombine(root, relative));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class FakeDownload : DownloadServiceStub
        {
            public List<string> Downloaded { get; } = new List<string>();

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                Downloaded.Add(url);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                File.WriteAllText(localPath, "fake");
                return Task.CompletedTask;
            }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                if (url.IndexOf("/files/", StringComparison.Ordinal) >= 0)
                {
                    return Task.FromResult(
                        @"{ ""data"": { ""downloadUrl"": ""https://cdn.curseforge/cf.jar"", ""fileName"": ""cf.jar"", ""fileLength"": 4, ""hashes"": { ""sha1"": """" } } }");
                }

                // 项目查询：classId=12 资源包，验证会落 resourcepacks 而不是 mods。
                return Task.FromResult(@"{ ""data"": { ""classId"": 12 } }");
            }
        }

        private sealed class FakeFileDownloader : IFileDownloader
        {
            public List<string> Downloaded { get; } = new List<string>();

        public Task DownloadAsync(string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(1);
            return DownloadAsync(url, targetPath, cancellationToken);
        }

            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
            {
                lock (Downloaded)
                {
                    Downloaded.Add(url);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "fake");
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }

        private sealed class ThrowingFileDownloader : IFileDownloader
        {
            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
                => throw new HttpRequestException("模拟下载失败");

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
                => throw new HttpRequestException("模拟下载失败");

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }

        private sealed class InlineInstallProgress : IProgress<InstallProgress>
        {
            private readonly Action<InstallProgress> _apply;

            public InlineInstallProgress(Action<InstallProgress> apply)
            {
                _apply = apply;
            }

            public void Report(InstallProgress value) => _apply(value);
        }

        private sealed class FakeVersions : IVersionInstaller
        {
            public InstallRequest? LastRequest { get; private set; }

            public Task InstallAsync(
                InstallRequest request, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeLoaders : ILoaderInstaller
        {
            public Task<string> InstallForgeAsync(
                string minecraftVersion, string loaderVersion, string minecraftDirectory,
                string javaExecutable, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => Task.FromResult(minecraftVersion + "-forge-" + loaderVersion);

            public Task<string> InstallNeoForgeAsync(
                string minecraftVersion, string loaderVersion, string minecraftDirectory,
                string javaExecutable, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => Task.FromResult("neoforge-" + loaderVersion);
        }

        private static string BuildMrpack(string directory, string index = FabricIndex)
        {
            string path = Path.Combine(directory, "pack.mrpack");

            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Add(string name, string content)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name);

                    using (Stream stream = entry.Open())
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(content);
                    }
                }

                Add("modrinth.index.json", index);
                Add("overrides/config/mod.toml", "a=1");
                Add("overrides/", string.Empty);
            }

            return path;
        }

        private static string BuildCurseForgePack(string directory)
        {
            string path = Path.Combine(directory, "curse.zip");

            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Add(string name, string content)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name);

                    using (Stream stream = entry.Open())
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(content);
                    }
                }

                Add("manifest.json", CurseForgeManifest);
                Add("overrides/config/curse.toml", "a=1");
                Add("overrides/", string.Empty);
            }

            return path;
        }

        [Fact]
        public async Task Installs_curseforge_pack_into_isolated_instance()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-cf-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(mc);

            try
            {
                string pack = BuildCurseForgePack(root);
                FakeDownload download = new FakeDownload();
                FakeFileDownloader files = new FakeFileDownloader();
                FakeVersions versions = new FakeVersions();
                ModpackInstaller installer = new ModpackInstaller(
                    download, versions, new FakeLoaders(), files);
                List<InstallProgress> reports = new List<InstallProgress>();

                string versionId = await installer.InstallAsync(
                    pack, mc, @"C:\Java\bin\javaw.exe", new InlineInstallProgress(reports.Add),
                    CancellationToken.None);

                Assert.Equal("Curse-Test-Pack", versionId);
                Assert.Equal("fabric", versions.LastRequest!.Loader);
                Assert.Equal("0.15.11", versions.LastRequest.LoaderVersion);

                string instanceDir = Path.Combine(mc, "versions", versionId);
                Assert.True(File.Exists(Path.Combine(instanceDir, "resourcepacks", "cf.jar")));
                Assert.True(File.Exists(Path.Combine(instanceDir, "config", "curse.toml")));
                ModpackManifest? manifest = ModpackManifestStore.Read(instanceDir);
                Assert.NotNull(manifest);
                Assert.Equal("curseforge", manifest!.Source);
                Assert.Equal("1.2.3", manifest.VersionId);
                Assert.Contains(files.Downloaded, url => url.Contains("cf.jar"));
                Assert.Contains(reports, item => item.BytesTotal > 0 && item.BytesReceived > 0);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Failed_download_does_not_leave_an_empty_instance()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-mrp-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(mc);

            try
            {
                string pack = BuildCurseForgePack(root);
                ModpackInstaller installer = new ModpackInstaller(
                    new FakeDownload(), new FakeVersions(), new FakeLoaders(),
                    new ThrowingFileDownloader());

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    installer.InstallAsync(
                        pack, mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None));

                string instance = Path.Combine(mc, "versions", "Curse-Test-Pack");
                Assert.False(Directory.Exists(instance), "失败后不应留下空壳实例目录");
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Updates_in_place_removes_old_pack_mods_and_keeps_player_mods()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-update-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(mc);

            try
            {
                ModpackInstaller installer = new ModpackInstaller(
                    new FakeDownload(), new FakeVersions(), new FakeLoaders(), new FakeFileDownloader());
                string versionId = await installer.InstallAsync(
                    BuildMrpack(root), mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);
                string instance = Path.Combine(mc, "versions", versionId);
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                File.WriteAllText(Path.Combine(mods, "player.jar"), "player");

                const string nextIndex = @"{
                    ""formatVersion"": 1,
                    ""name"": ""Fabulously Optimized"",
                    ""versionId"": ""5.0.1"",
                    ""files"": [
                        { ""path"": ""mods/sodium-new.jar"", ""downloads"": [""https://cdn/sodium-new.jar""] }
                    ],
                    ""dependencies"": { ""minecraft"": ""1.20.1"", ""fabric-loader"": ""0.16.9"" }
                }";
                string nextDir = Path.Combine(root, "next");
                Directory.CreateDirectory(nextDir);

                await installer.UpdateAsync(
                    BuildMrpack(nextDir, nextIndex), instance, null, CancellationToken.None);

                Assert.False(File.Exists(Path.Combine(mods, "sodium.jar")), "旧整合包 mod 应被移除");
                Assert.True(File.Exists(Path.Combine(mods, "sodium-new.jar")), "新版整合包 mod 应安装");
                Assert.True(File.Exists(Path.Combine(mods, "player.jar")), "玩家自加 mod 必须保留");

                ModpackManifest? manifest = ModpackManifestStore.Read(instance);
                Assert.NotNull(manifest);
                Assert.Equal("5.0.1", manifest!.VersionId);
                Assert.Contains("mods/sodium-new.jar", manifest.ModFiles);
                Assert.True(File.Exists(Path.Combine(instance, versionId + ".json")));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Updates_fabric_loader_version_in_place()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-loader-update-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(mc);

            try
            {
                FakeVersions versions = new FakeVersions();
                ModpackInstaller installer = new ModpackInstaller(
                    new FakeDownload(), versions, new FakeLoaders(), new FakeFileDownloader());
                string versionId = await installer.InstallAsync(
                    BuildMrpack(root), mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);
                string instance = Path.Combine(mc, "versions", versionId);

                const string nextIndex = @"{ 
                    ""formatVersion"": 1,
                    ""name"": ""Fabulously Optimized"",
                    ""versionId"": ""5.1.0"",
                    ""files"": [
                        { ""path"": ""mods/sodium-new.jar"", ""downloads"": [""https://cdn/sodium-new.jar""] }
                    ],
                    ""dependencies"": { ""minecraft"": ""1.20.1"", ""fabric-loader"": ""0.19.3"" }
                }";
                string nextDir = Path.Combine(root, "next");
                Directory.CreateDirectory(nextDir);

                string newLoaderId = "fabric-loader-0.19.3-1.20.1";
                string newLoaderDir = Path.Combine(mc, "versions", newLoaderId);
                Directory.CreateDirectory(newLoaderDir);
                File.WriteAllText(
                    Path.Combine(newLoaderDir, newLoaderId + ".json"),
                    @"{""id"":""fabric-loader-0.19.3-1.20.1"",""inheritsFrom"":""1.20.1"",
                       ""mainClass"":""new.LoaderMain"",""libraries"":[{""name"":""new:loader""}]}");

                await installer.UpdateAsync(
                    BuildMrpack(nextDir, nextIndex), instance, null, CancellationToken.None);

                ModpackManifest? manifest = ModpackManifestStore.Read(instance);
                Assert.NotNull(manifest);
                Assert.Equal("0.19.3", manifest!.LoaderVersion);

                JObject versionJson = JObject.Parse(
                    File.ReadAllText(Path.Combine(instance, versionId + ".json")));
                Assert.Equal("1.20.1", versionJson.Value<string>("inheritsFrom"));
                Assert.Equal("new.LoaderMain", versionJson.Value<string>("mainClass"));
                Assert.Equal("0.19.3", versions.LastRequest!.LoaderVersion);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Failed_update_leaves_the_original_instance_intact()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-update-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(mc);

            try
            {
                ModpackInstaller installer = new ModpackInstaller(
                    new FakeDownload(), new FakeVersions(), new FakeLoaders(), new FakeFileDownloader());
                string versionId = await installer.InstallAsync(
                    BuildMrpack(root), mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);
                string instance = Path.Combine(mc, "versions", versionId);
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                File.WriteAllText(Path.Combine(mods, "player.jar"), "player");

                const string nextIndex = @"{
                    ""formatVersion"": 1,
                    ""name"": ""Fabulously Optimized"",
                    ""versionId"": ""5.0.1"",
                    ""files"": [
                        { ""path"": ""mods/sodium-new.jar"", ""downloads"": [""https://cdn/sodium-new.jar""] }
                    ],
                    ""dependencies"": { ""minecraft"": ""1.20.1"", ""fabric-loader"": ""0.16.9"" }
                }";
                string nextDir = Path.Combine(root, "next-fail");
                Directory.CreateDirectory(nextDir);
                string nextPack = BuildMrpack(nextDir, nextIndex);

                ModpackInstaller failingInstaller = new ModpackInstaller(
                    new FakeDownload(), new FakeVersions(), new FakeLoaders(), new ThrowingFileDownloader());

                await Assert.ThrowsAnyAsync<Exception>(() => failingInstaller.UpdateAsync(
                    nextPack, instance, null, CancellationToken.None));

                Assert.True(File.Exists(Path.Combine(mods, "sodium.jar")), "失败更新必须保留旧版 mod");
                Assert.True(File.Exists(Path.Combine(mods, "player.jar")), "失败更新必须保留玩家 mod");
                Assert.False(File.Exists(Path.Combine(mods, "sodium-new.jar")), "失败更新不能留下半套新 mod");

                ModpackManifest? manifest = ModpackManifestStore.Read(instance);
                Assert.NotNull(manifest);
                Assert.Equal("5.0.0", manifest!.VersionId);

                string[] staging = Directory.GetDirectories(
                    Path.Combine(mc, "versions"), "*.nlmcl-staging-*", SearchOption.TopDirectoryOnly);
                Assert.Empty(staging);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Clones_loader_json_so_pcl_can_merge_it()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-mrp-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            string loaderId = "fabric-loader-0.16.9-1.20.1";
            string loaderDir = Path.Combine(mc, "versions", loaderId);
            Directory.CreateDirectory(loaderDir);

            // 模拟已安装的 Fabric：完整的 profile JSON
            File.WriteAllText(
                Path.Combine(loaderDir, loaderId + ".json"),
                @"{
                    ""id"": ""fabric-loader-0.16.9-1.20.1"",
                    ""inheritsFrom"": ""1.20.1"",
                    ""mainClass"": ""net.fabricmc.loader.impl.launch.knot.KnotClient"",
                    ""libraries"": [ { ""name"": ""net.fabricmc:fabric-loader:0.16.9"" } ]
                }");

            try
            {
                string pack = BuildMrpack(root);
                ModpackInstaller installer = new ModpackInstaller(
                    new FakeDownload(), new FakeVersions(), new FakeLoaders(), new FakeFileDownloader());

                string versionId = await installer.InstallAsync(
                    pack, mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);

                string jsonPath = Path.Combine(mc, "versions", versionId, versionId + ".json");
                JObject json = JObject.Parse(File.ReadAllText(jsonPath));

                Assert.Equal(versionId, json.Value<string>("id"));
                Assert.Equal("1.20.1", json.Value<string>("inheritsFrom")); // 继承原版，不再指向加载器
                Assert.NotNull(json["libraries"]);
                Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", json.Value<string>("mainClass"));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }
        [Fact]
        public async Task Installs_pack_into_isolated_instance()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-mrp-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(mc);

            try
            {
                string pack = BuildMrpack(root);

                FakeFileDownloader downloader = new FakeFileDownloader();
                FakeVersions versions = new FakeVersions();
                ModpackInstaller installer = new ModpackInstaller(
                    new FakeDownload(), versions, new FakeLoaders(), downloader);

                string versionId = await installer.InstallAsync(
                    pack, mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);

                Assert.Equal("Fabulously-Optimized", versionId);
                Assert.Equal("fabric", versions.LastRequest!.Loader);

                string instanceDir = Path.Combine(mc, "versions", versionId);
                Assert.True(File.Exists(Path.Combine(instanceDir, versionId + ".json")));
                Assert.True(File.Exists(Path.Combine(instanceDir, "mods", "sodium.jar")));
                Assert.True(File.Exists(Path.Combine(instanceDir, "config", "mod.toml")));

                ModpackManifest? manifest = ModpackManifestStore.Read(instanceDir);
                Assert.NotNull(manifest);
                Assert.Equal("modrinth", manifest!.Source);
                Assert.Equal("5.0.0", manifest.VersionId);
                Assert.Contains("mods/sodium.jar", manifest.ModFiles);
                Assert.DoesNotContain("mods/serveronly.jar", manifest.ModFiles);

                // server-only 文件不应该被下载
                Assert.DoesNotContain(downloader.Downloaded, url => url.Contains("server.jar"));
                Assert.Contains(downloader.Downloaded, url => url.Contains("sodium.jar"));

                JObject json = JObject.Parse(File.ReadAllText(Path.Combine(instanceDir, versionId + ".json")));
                Assert.Equal("fabric-loader-0.16.9-1.20.1", json.Value<string>("inheritsFrom"));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

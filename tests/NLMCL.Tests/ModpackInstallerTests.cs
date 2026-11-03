using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

        private static string BuildMrpack(string directory)
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

                Add("modrinth.index.json", FabricIndex);
                Add("overrides/config/mod.toml", "a=1");
                Add("overrides/", string.Empty);
            }

            return path;
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

                FakeDownload download = new FakeDownload();
                FakeVersions versions = new FakeVersions();
                ModpackInstaller installer = new ModpackInstaller(download, versions, new FakeLoaders());

                string versionId = await installer.InstallAsync(
                    pack, mc, @"C:\Java\bin\javaw.exe", null, CancellationToken.None);

                Assert.Equal("Fabulously-Optimized", versionId);
                Assert.Equal("fabric", versions.LastRequest!.Loader);

                string instanceDir = Path.Combine(mc, "versions", versionId);
                Assert.True(File.Exists(Path.Combine(instanceDir, versionId + ".json")));
                Assert.True(File.Exists(Path.Combine(instanceDir, "mods", "sodium.jar")));
                Assert.True(File.Exists(Path.Combine(instanceDir, "config", "mod.toml")));

                // server-only 文件不应该被下载
                Assert.DoesNotContain(download.Downloaded, url => url.Contains("server.jar"));

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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Pcl;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 集成测试：在临时目录里造一个假的 .minecraft，验证 PCL 的 McVersion 真的能识别出版本。
    /// 这是 M2 的验收依据——不是 mock，是走真实的 JSON 解析路径。
    /// </summary>
    public class PclVersionCatalogTests : IDisposable
    {
        private readonly string _root;

        public PclVersionCatalogTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "nlmcl-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "versions"));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响测试结论。
            }
        }

        private void WriteVersion(string folderName, string json, bool createJar)
        {
            string folder = Path.Combine(_root, "versions", folderName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, folderName + ".json"), json);

            if (createJar)
            {
                File.WriteAllBytes(Path.Combine(folder, folderName + ".jar"), new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            }
        }

        private const string Vanilla1211 =
            @"{
                ""id"": ""1.21.1"",
                ""type"": ""release"",
                ""releaseTime"": ""2024-08-08T14:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"",
                ""libraries"": []
              }";

        [Fact]
        public async Task Scans_vanilla_version_from_real_json()
        {
            WriteVersion("1.21.1", Vanilla1211, createJar: true);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(_root, CancellationToken.None);

            MinecraftVersion only = Assert.Single(versions);
            Assert.Equal("1.21.1", only.Id);
            Assert.Equal("1.21.1", only.FolderName);
            Assert.Equal(VersionKind.Release, only.Kind);
            Assert.True(only.HasClientJar);
            Assert.NotNull(only.ReleasedAt);
            Assert.Equal(2024, only.ReleasedAt!.Value.Year);
        }

        [Fact]
        public async Task Detects_snapshot_by_json_type()
        {
            WriteVersion("24w44a", @"{
                ""id"": ""24w44a"",
                ""type"": ""snapshot"",
                ""releaseTime"": ""2024-10-30T10:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"",
                ""libraries"": []
              }", createJar: true);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(_root, CancellationToken.None);

            Assert.Equal(VersionKind.Snapshot, Assert.Single(versions).Kind);
        }

        [Fact]
        public async Task Resolves_loader_version_through_inheritsFrom()
        {
            WriteVersion("1.21.1", Vanilla1211, createJar: true);
            WriteVersion("fabric-loader-0.16.9-1.21.1", @"{
                ""id"": ""fabric-loader-0.16.9-1.21.1"",
                ""inheritsFrom"": ""1.21.1"",
                ""type"": ""release"",
                ""releaseTime"": ""2024-08-09T00:00:00+00:00"",
                ""mainClass"": ""net.fabricmc.loader.impl.launch.knot.KnotClient"",
                ""libraries"": [{ ""name"": ""net.fabricmc:fabric-loader:0.16.9"" }]
              }", createJar: false);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(_root, CancellationToken.None);

            Assert.Equal(2, versions.Count);

            MinecraftVersion loader = versions.Single(v => v.FolderName.StartsWith("fabric-loader"));
            // 关键在于 PCL 把加载器版本归一到原版版本号 1.21.1
            Assert.Equal("1.21.1", loader.Id);
            Assert.Equal(VersionKind.Release, loader.Kind);
            // 父版本有 jar，子实例不算缺 jar（这条以前断言的正是那个误报）
            Assert.True(loader.HasClientJar);
        }

        [Fact]
        public async Task Broken_json_is_skipped_without_failing_the_scan()
        {
            WriteVersion("1.21.1", Vanilla1211, createJar: true);
            WriteVersion("broken", "{ this is not json", createJar: false);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(_root, CancellationToken.None);

            Assert.Single(versions);
            Assert.Equal("1.21.1", versions[0].Id);
        }

        [Fact]
        public async Task Missing_versions_folder_returns_empty()
        {
            string bare = Path.Combine(_root, "no-minecraft-here");
            Directory.CreateDirectory(bare);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(bare, CancellationToken.None);

            Assert.Empty(versions);
        }

        [Fact]
        public async Task Results_are_sorted_by_release_time_descending()
        {
            WriteVersion("1.20.1", @"{
                ""id"": ""1.20.1"", ""type"": ""release"",
                ""releaseTime"": ""2023-06-12T12:00:00+00:00"",
                ""mainClass"": ""m"", ""libraries"": []
              }", createJar: true);
            WriteVersion("1.21.1", Vanilla1211, createJar: true);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(_root, CancellationToken.None);

            Assert.Equal("1.21.1", versions[0].Id);
            Assert.Equal("1.20.1", versions[1].Id);
        }

        /// <summary>
        /// 回归：Fabric/Forge 这类 inheritsFrom 实例的客户端 JAR 在**父版本**目录里。
        /// 只查自己目录会给它们误报"未找到客户端 JAR，可能无法启动"。
        /// </summary>
        [Fact]
        public async Task Inherits_from_instance_reuses_parents_client_jar()
        {
            WriteVersion("1.20.1", @"{
                ""id"": ""1.20.1"", ""type"": ""release"",
                ""releaseTime"": ""2023-06-12T12:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"", ""libraries"": []
              }", createJar: true);

            WriteVersion("fabric-loader-0.16.9-1.20.1", @"{
                ""id"": ""fabric-loader-0.16.9-1.20.1"", ""type"": ""release"",
                ""releaseTime"": ""2026-10-29T12:00:00+00:00"",
                ""inheritsFrom"": ""1.20.1"",
                ""mainClass"": ""net.fabricmc.loader.impl.launch.knot.KnotClient"", ""libraries"": []
              }", createJar: false);

            IReadOnlyList<MinecraftVersion> versions = await new PclVersionCatalog()
                .GetInstalledAsync(_root, CancellationToken.None);

            MinecraftVersion fabric = versions.Single(v => v.FolderName.StartsWith("fabric-loader"));
            Assert.True(fabric.HasClientJar, "父版本有 jar 时，子实例不该被判成缺 jar");
        }
    }
}

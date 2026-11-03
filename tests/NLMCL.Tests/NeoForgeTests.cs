using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>NeoForge 版本映射、安装器输出过滤、安装产物发现。</summary>
    public class NeoForgeTests
    {
        [Theory]
        [InlineData("net.jodah:typetools:0.6.3", "net/jodah/typetools/0.6.3/typetools-0.6.3.jar")]
        [InlineData("net.neoforged:neoforge:21.1.265:universal", "net/neoforged/neoforge/21.1.265/neoforge-21.1.265-universal.jar")]
        [InlineData("net.neoforged:neoforge:21.1.265:clientdata@lzma",
            "net/neoforged/neoforge/21.1.265/neoforge-21.1.265-clientdata.lzma")]
        [InlineData("bad", null)]
        public void Builds_maven_path_from_coordinate(string coordinate, string? expected)
        {
            Assert.Equal(expected, LoaderLibrarySeeder.ArtifactPath(coordinate));
        }

        [Fact]
        public void Reads_bracketed_data_coordinates()
        {
            Assert.Equal(
                "net/neoforged/neoforge/21.1.265/neoforge-21.1.265-clientdata.lzma",
                LoaderLibrarySeeder.BracketedArtifactPath("[net.neoforged:neoforge:21.1.265:clientdata@lzma]"));

            Assert.Null(LoaderLibrarySeeder.BracketedArtifactPath("22.2.0"));
        }

        [Fact]
        public void Reads_library_paths_from_installer_jar()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-jar-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                string jar = Path.Combine(root, "fake-installer.jar");

                using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(
                    jar, System.IO.Compression.ZipArchiveMode.Create))
                {
                    void Add(string name, string content)
                    {
                        System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(name);
                        using StreamWriter writer = new StreamWriter(entry.Open());
                        writer.Write(content);
                    }

                    Add("install_profile.json", @"{
                        ""libraries"": [ { ""name"": ""net.neoforged.fancymodloader:earlydisplay:4.0.45"" } ],
                        ""data"": { ""client.lzma"": { ""client"": ""[net.neoforged:neoforge:21.1.265:clientdata@lzma]"" } }
                    }");

                    Add("version.json", @"{
                        ""libraries"": [ { ""downloads"": { ""artifact"": { ""path"": ""net/jodah/typetools/0.6.3/typetools-0.6.3.jar"" } } } ]
                    }");
                }

                IReadOnlyList<string> paths = LoaderLibrarySeeder.ReadLibraryPaths(jar);

                Assert.Contains("net/neoforged/fancymodloader/earlydisplay/4.0.45/earlydisplay-4.0.45.jar", paths);
                Assert.Contains("net/jodah/typetools/0.6.3/typetools-0.6.3.jar", paths);
                Assert.Contains("net/neoforged/neoforge/21.1.265/neoforge-21.1.265-clientdata.lzma", paths);
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
        [InlineData("21.1.80", "1.21.1")]
        [InlineData("20.4.253", "1.20.4")]
        [InlineData("20.2.86", "1.20.2")]
        [InlineData("21.5.0", "1.21.5")]
        [InlineData("garbage", null)]
        public void Maps_neoforge_version_to_minecraft(string neoforge, string? expected)
        {
            Assert.Equal(expected, NeoForgeCatalog.MinecraftOf(neoforge));
        }

        [Fact]
        public void Filters_builds_by_minecraft_version_and_sorts_desc()
        {
            const string json = @"{
                ""name"": ""net.neoforged:neoforge"",
                ""files"": [
                    { ""name"": ""21.1.80"" },
                    { ""name"": ""21.1.9"" },
                    { ""name"": ""20.4.253"" },
                    { ""name"": ""maven-metadata.xml"" }
                ]
            }";

            IReadOnlyList<LoaderBuild> builds = NeoForgeCatalog.Parse(json, "1.21.1");

            Assert.Equal(2, builds.Count);
            Assert.Equal("21.1.80", builds[0].Version); // 数字比较：80 > 9
            Assert.Equal("21.1.9", builds[1].Version);
        }

        [Fact]
        public void Empty_when_minecraft_version_has_no_neoforge()
        {
            const string json = @"{ ""files"": [ { ""name"": ""21.1.80"" } ] }";
            Assert.Empty(NeoForgeCatalog.Parse(json, "1.20.1"));
        }

        [Theory]
        [InlineData("skip: false", false)]
        [InlineData("Patching net/minecraft/Util 1/1", true)]
        [InlineData("Downloading library com.example:foo:1.0", true)]
        [InlineData("Injecting profile", true)]
        [InlineData("Successfully installed client into launcher.", true)]
        [InlineData("  ", false)]
        public void Output_filter_keeps_only_meaningful_lines(string line, bool expected)
        {
            Assert.Equal(expected, ForgeLoaderInstaller.ShouldReport(line));
        }

        [Fact]
        public void Finds_installed_version_when_name_differs_from_expected()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-neo-" + Guid.NewGuid().ToString("N"));

            try
            {
                string versions = Path.Combine(root, "versions");
                Directory.CreateDirectory(Path.Combine(versions, "neoforge-21.1.80"));
                File.WriteAllText(
                    Path.Combine(versions, "neoforge-21.1.80", "neoforge-21.1.80.json"), "{}");

                // 期望 id 与真实目录名不同，也能靠扫描找到
                Assert.Equal("neoforge-21.1.80",
                    ForgeLoaderInstaller.FindInstalledVersion(root, "neoforge-other"));
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

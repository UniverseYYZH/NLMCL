using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>在线资源：Modrinth 解析、URL 构造、安装落点。</summary>
    public class ModrinthResourceTests
    {
        [Fact]
        public void Parses_search_hits()
        {
            const string json = @"{
                ""hits"": [
                    {
                        ""project_id"": ""AANobbMI"",
                        ""slug"": ""sodium"",
                        ""title"": ""Sodium"",
                        ""description"": ""Modern rendering engine"",
                        ""icon_url"": ""https://cdn.modrinth.com/sodium.png"",
                        ""author"": ""jellysquid3"",
                        ""downloads"": 123456789
                    },
                    {
                        ""project_id"": ""YL57xq9U"",
                        ""slug"": ""iris"",
                        ""title"": ""Iris Shaders"",
                        ""description"": ""Shaders"",
                        ""downloads"": 5000
                    }
                ]
            }";

            IReadOnlyList<ResourceSummary> hits = ModrinthCatalog.ParseSearch(json, ResourceKind.Mod);

            Assert.Equal(2, hits.Count);
            Assert.Equal("Sodium", hits[0].Title);
            Assert.Equal("https://cdn.modrinth.com/sodium.png", hits[0].IconUrl);
            Assert.Equal("12.3 千万", hits[0].DownloadsText);
            Assert.Equal("5000", hits[1].DownloadsText);
            Assert.Null(hits[1].IconUrl);
        }

        [Fact]
        public void Search_url_carries_type_version_and_loader_facets()
        {
            ResourceQuery query = new ResourceQuery("sodium", ResourceKind.Mod)
            {
                GameVersion = "1.20.1",
                Loader = "Fabric"
            };

            string url = ModrinthCatalog.BuildSearchUrl(query);

            Assert.Contains("project_type%3Amod", url);
            Assert.Contains("versions%3A1.20.1", url);
            Assert.Contains("categories%3Afabric", url);
            Assert.Contains("query=sodium", url);
        }

        [Fact]
        public void Resource_packs_do_not_filter_by_loader()
        {
            ResourceQuery query = new ResourceQuery("faithful", ResourceKind.ResourcePack)
            {
                Loader = "Fabric"
            };

            string url = ModrinthCatalog.BuildSearchUrl(query);

            Assert.Contains("project_type%3Aresourcepack", url);
            Assert.DoesNotContain("categories", url);
        }

        [Fact]
        public void Parses_versions_and_prefers_primary_file()
        {
            const string json = @"[
                {
                    ""version_number"": ""0.5.13"",
                    ""loaders"": [ ""fabric"" ],
                    ""game_versions"": [ ""1.20.1"" ],
                    ""files"": [
                        { ""filename"": ""extra.jar"", ""url"": ""https://cdn/extra.jar"", ""size"": 10, ""primary"": false },
                        { ""filename"": ""sodium.jar"", ""url"": ""https://cdn/sodium.jar"", ""size"": 930000, ""primary"": true }
                    ]
                }
            ]";

            IReadOnlyList<ResourceFile> files = ModrinthCatalog.ParseFiles(json);

            Assert.Single(files);
            Assert.Equal("sodium.jar", files[0].Name);
            Assert.Equal("https://cdn/sodium.jar", files[0].Url);
            Assert.Equal("fabric", files[0].Loader);
            Assert.Equal("0.5.13", files[0].VersionName);
        }

        [Fact]
        public void Parses_required_dependencies_from_a_version()
        {
            const string json = @"[
                {
                    ""version_number"": ""2.0"",
                    ""loaders"": [ ""fabric"" ],
                    ""game_versions"": [ ""1.21.1"" ],
                    ""dependencies"": [
                        { ""project_id"": ""P7dR8mSH"", ""version_id"": ""abc"", ""dependency_type"": ""required"" },
                        { ""project_id"": ""Ha28R6CL"", ""dependency_type"": ""optional"" }
                    ],
                    ""files"": [
                        { ""filename"": ""mod.jar"", ""url"": ""https://cdn/mod.jar"", ""size"": 10, ""primary"": true }
                    ]
                }
            ]";

            IReadOnlyList<ResourceFile> files = ModrinthCatalog.ParseFiles(json);

            Assert.Single(files);
            Assert.Equal(2, files[0].Dependencies.Count);
            Assert.True(files[0].Dependencies[0].IsRequired);
            Assert.Equal("P7dR8mSH", files[0].Dependencies[0].ProjectId);
            Assert.False(files[0].Dependencies[1].IsRequired);
        }

        [Theory]
        [InlineData("1.20.1", "1.20.1")]
        [InlineData("1.20.1-Fabric 0.20.1", "1.20.1")]
        [InlineData("fabric-loader-0.16.9-1.20.1", "1.20.1")]
        [InlineData("1.21.2-Fabric 0.20.1", "1.21.2")]
        public void Extracts_game_version_from_instance_folder(string folder, string expected)
        {
            Assert.Equal(expected, NLMCL.App.ViewModels.ResourceViewModel.ExtractGameVersion(folder));
        }
        [Fact]
        public void Installer_prefers_isolated_instance_directory()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-res-" + Guid.NewGuid().ToString("N"));
            string mc = Path.Combine(root, ".minecraft");
            string isolated = Path.Combine(mc, "versions", "1.20.1-Fabric 0.20.1");

            try
            {
                Directory.CreateDirectory(isolated);

                Assert.Equal(isolated, ResourceInstaller.ResolveBaseDirectory(mc, "1.20.1-Fabric 0.20.1"));
                Assert.Equal(mc, ResourceInstaller.ResolveBaseDirectory(mc, "不存在的版本"));
                Assert.Equal(mc, ResourceInstaller.ResolveBaseDirectory(mc, string.Empty));
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
        [InlineData(ResourceKind.Mod, "mods")]
        [InlineData(ResourceKind.ResourcePack, "resourcepacks")]
        [InlineData(ResourceKind.ShaderPack, "shaderpacks")]
        [InlineData(ResourceKind.DataPack, "datapacks")]
        public void Subdirectory_matches_kind(ResourceKind kind, string expected)
        {
            Assert.Equal(expected, ResourceInstaller.SubDirectoryOf(kind));
        }
    }
}

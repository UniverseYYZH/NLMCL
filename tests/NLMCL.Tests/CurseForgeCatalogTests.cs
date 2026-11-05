using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class CurseForgeCatalogTests
    {
        [Theory]
        [InlineData(ResourceKind.Mod, 6)]
        [InlineData(ResourceKind.ResourcePack, 12)]
        [InlineData(ResourceKind.ShaderPack, 6552)]
        [InlineData(ResourceKind.DataPack, 6945)]
        public void Class_ids_match_curseforge_enum(ResourceKind kind, int expected)
        {
            Assert.Equal(expected, CurseForgeCatalog.ClassIdOf(kind));
        }

        [Theory]
        [InlineData("fabric", 4)]
        [InlineData("forge", 1)]
        [InlineData("neoforge", 6)]
        [InlineData("quilt", 5)]
        public void Loader_types_match_curseforge_enum(string loader, int expected)
        {
            Assert.Equal(expected, CurseForgeCatalog.LoaderTypeOf(loader));
        }

        [Fact]
        public async Task Search_parses_mcim_payload_into_summaries()
        {
            string json = @"{
                ""data"": [
                    {
                        ""id"": 238222,
                        ""name"": ""Just Enough Items (JEI)"",
                        ""slug"": ""jei"",
                        ""summary"": ""View items and recipes"",
                        ""downloadCount"": 123456789,
                        ""logo"": { ""thumbnailUrl"": ""https://media.forgecdn.net/avatars/jei.png"" }
                    }
                ],
                ""pagination"": { ""totalCount"": 1 }
            }";

            CurseForgeCatalog catalog = new CurseForgeCatalog(new StubDownload(json));
            IReadOnlyList<ResourceSummary> found = await catalog.SearchAsync(
                new ResourceQuery("jei", ResourceKind.Mod), CancellationToken.None);

            Assert.Single(found);
            Assert.Equal("jei", found[0].Slug);
            Assert.Equal("238222", found[0].Id);
            Assert.Equal("https://media.forgecdn.net/avatars/jei.png", found[0].IconUrl);
        }

        [Fact]
        public async Task Files_without_download_url_are_skipped()
        {
            string json = @"{
                ""data"": [
                    { ""id"": 1, ""fileName"": ""a.jar"", ""downloadUrl"": null, ""fileLength"": 10, ""gameVersions"": [""1.21.1""] },
                    { ""id"": 2, ""fileName"": ""b.jar"", ""downloadUrl"": ""https://edge.forgecdn.net/files/1/2/b.jar"", ""fileLength"": 20, ""gameVersions"": [""1.21.1"", ""Fabric""] }
                ]
            }";

            CurseForgeCatalog catalog = new CurseForgeCatalog(new StubDownload(json));
            IReadOnlyList<ResourceFile> files = await catalog.GetFilesAsync(
                "238222", ResourceKind.Mod, "1.21.1", "fabric", CancellationToken.None);

            Assert.Single(files);
            Assert.Equal("b.jar", files[0].Name);
            Assert.Equal(20, files[0].Size);
        }

        private sealed class StubDownload : IDownloadService
        {
            private readonly string _json;

            public StubDownload(string json)
            {
                _json = json;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult(_json);

            public Task<string> GetStringAsync(
                string url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
                => Task.FromResult(_json);

            public Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task<string> PostJsonAsync(
                string url, string json, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            public Task<string> PostFormAsync(
                string url, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }
    }
}
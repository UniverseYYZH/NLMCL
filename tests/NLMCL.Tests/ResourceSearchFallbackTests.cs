using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>中文别名与"搜不到就放宽筛选"的行为。</summary>
    public class ResourceSearchFallbackTests
    {
        [Theory]
        [InlineData("史蒂夫模型", "Yes Steve Model")]
        [InlineData("是！史蒂夫模型", "Yes Steve Model")]
        [InlineData("机械动力", "Create")]
        [InlineData("钠", "Sodium")]
        [InlineData("钠光影", "Iris Shaders")]
        [InlineData("sodium", null)]
        public void Maps_known_chinese_names(string query, string? expected)
        {
            Assert.Equal(expected, ChineseAliases.TryMap(query));
        }

        [Fact]
        public void Detects_chinese_characters()
        {
            Assert.True(ChineseAliases.ContainsChinese("史蒂夫模型"));
            Assert.False(ChineseAliases.ContainsChinese("Yes Steve Model"));
        }

        private sealed class VersionSensitiveCatalog : IResourceCatalog
        {
            public List<string> RequestedVersions { get; } = new List<string>();

            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
            {
                RequestedVersions.Add(query.GameVersion ?? "(无)");

                // 只有不带版本筛选时才返回结果，模拟"模组只更新到旧版本"
                if (query.GameVersion != null)
                {
                    return Task.FromResult<IReadOnlyList<ResourceSummary>>(Array.Empty<ResourceSummary>());
                }

                return Task.FromResult<IReadOnlyList<ResourceSummary>>(new[]
                {
                    new ResourceSummary("86xjpqqS", "yes-steve-model", "Yes Steve Model",
                        "Steve model", null, "作者", 1000, ResourceKind.Mod)
                });
            }

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(Array.Empty<ResourceFile>());
        }

        private sealed class NoopInstaller : IResourceInstaller
        {
            public Task<string> InstallAsync(
                ResourceFile file, ResourceKind kind, string minecraftDirectory, string versionFolder,
                CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }

        [Fact]
        public async Task Ignores_version_filter_when_nothing_matches()
        {
            VersionSensitiveCatalog catalog = new VersionSensitiveCatalog();
            ResourceViewModel vm = new ResourceViewModel(catalog, new NoopInstaller());

            (IReadOnlyList<ResourceSummary> found, string relaxed) =
                await vm.SearchWithFallbackAsync("Yes Steve Model", "1.21.2", "fabric");

            Assert.Single(found);
            Assert.Contains("忽略版本", relaxed);
            Assert.Contains("(无)", catalog.RequestedVersions);
            Assert.Contains("1.21.2", catalog.RequestedVersions);
        }
    }
}

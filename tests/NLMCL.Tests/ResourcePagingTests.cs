using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ResourcePagingTests
    {
        [Fact]
        public async Task First_page_of_20_marks_has_more_and_load_more_appends()
        {
            PagedCatalog catalog = new PagedCatalog(total: 45);
            ResourceViewModel vm = new ResourceViewModel(catalog, new NoopInstaller());

            await vm.SearchAsync();

            Assert.Equal(20, vm.Results.Count);
            Assert.True(vm.HasMore);
            Assert.Contains("20", vm.ResultsCountText);

            await vm.LoadMoreAsync();
            Assert.Equal(40, vm.Results.Count);

            await vm.LoadMoreAsync();
            Assert.Equal(45, vm.Results.Count);
            Assert.False(vm.HasMore);          // 最后一页不满 20 → 没更多了
        }

        private sealed class NoopInstaller : IResourceInstaller
        {
            public Task<string> InstallAsync(
                ResourceFile file, ResourceKind kind, string minecraftDirectory, string versionFolder,
                CancellationToken cancellationToken)
                => Task.FromResult(versionFolder);
        }

        private sealed class PagedCatalog : IResourceCatalog
        {
            private readonly int _total;

            public PagedCatalog(int total)
            {
                _total = total;
            }

            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
            {
                List<ResourceSummary> items = new List<ResourceSummary>();
                int end = Math.Min(_total, query.Offset + query.Limit);

                for (int i = query.Offset; i < end; i++)
                {
                    items.Add(new ResourceSummary("id" + i, "slug" + i, "资源 " + i, "描述", "mod", null, 0, ResourceKind.Mod));
                }

                return Task.FromResult<IReadOnlyList<ResourceSummary>>(items);
            }

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(Array.Empty<ResourceFile>());
        }
    }
}
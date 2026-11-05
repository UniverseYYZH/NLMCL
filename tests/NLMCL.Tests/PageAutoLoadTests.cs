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
    public sealed class PageAutoLoadTests
    {
        [Fact]
        public async Task Entering_download_page_loads_version_list_once()
        {
            CountingProvider provider = new CountingProvider();

            DownloadViewModel vm = new DownloadViewModel(
                provider, new NoopInstaller(), new AppPaths(@"C:\Temp\roaming"));

            await vm.EnsureLoadedAsync();
            await vm.EnsureLoadedAsync();

            // 第二次进入不应重复请求
            Assert.Equal(1, provider.Calls);
            Assert.Single(vm.Versions);
        }

        private sealed class CountingProvider : IVersionListProvider
        {
            public int Calls { get; private set; }

            public Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult<IReadOnlyList<VanillaVersion>>(
                    new[] { new VanillaVersion("1.21.1", "release", null) });
            }
        }

        private sealed class NoopInstaller : IVersionInstaller
        {
            public Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress,
                CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
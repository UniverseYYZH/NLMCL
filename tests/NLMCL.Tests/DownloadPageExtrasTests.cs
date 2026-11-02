using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>下载页：Fabric 加载器版本列表与"已安装"标记。</summary>
    public class DownloadPageExtrasTests
    {
        [Fact]
        public void Parses_loader_versions_from_fabric_meta()
        {
            const string json = @"[
                { ""separator"": ""."", ""build"": 1, ""maven"": ""x"", ""version"": ""0.16.9"", ""stable"": true },
                { ""separator"": ""."", ""build"": 2, ""maven"": ""x"", ""version"": ""0.16.8"", ""stable"": true },
                { ""separator"": ""."", ""build"": 3, ""maven"": ""x"", ""version"": ""0.16.9"", ""stable"": true }
            ]";

            IReadOnlyList<string> versions = FabricLoaderVersionCatalog.Parse(json);

            Assert.Equal(2, versions.Count);
            Assert.Equal("0.16.9", versions[0]); // 保持 meta 顺序，且去重
            Assert.Equal("0.16.8", versions[1]);
        }

        private sealed class FakeLoaderCatalog : IFabricLoaderCatalog
        {
            public string? RequestedGameVersion { get; private set; }

            public Task<IReadOnlyList<string>> GetLoaderVersionsAsync(
                string gameVersion, CancellationToken cancellationToken)
            {
                RequestedGameVersion = gameVersion;
                return Task.FromResult<IReadOnlyList<string>>(new[] { "0.16.9", "0.16.8" });
            }
        }

        private sealed class EmptyProvider : IVersionListProvider
        {
            public Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<VanillaVersion>>(Array.Empty<VanillaVersion>());
        }

        private sealed class NoopInstaller : IVersionInstaller
        {
            public Task InstallAsync(InstallRequest request,
                IProgress<InstallProgress> progress, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        [Fact]
        public async Task Download_page_loads_loader_versions_for_fabric()
        {
            FakeLoaderCatalog catalog = new FakeLoaderCatalog();
            DownloadViewModel vm = new DownloadViewModel(
                new EmptyProvider(), new NoopInstaller(), new AppPaths(@"C:\Temp\roaming"), catalog);

            vm.SelectedVersion = new VanillaVersion("1.21.1", "release", null);
            vm.SelectedLoader = "Fabric";

            await vm.ReloadLoaderVersionsAsync();

            Assert.Equal("1.21.1", catalog.RequestedGameVersion);
            Assert.Equal(2, vm.LoaderVersions.Count);
            Assert.Equal("0.16.9", vm.LoaderVersion);
        }

        [Fact]
        public void Mark_installed_flags_matching_versions()
        {
            DownloadViewModel vm = new DownloadViewModel(
                new EmptyProvider(), new NoopInstaller(), new AppPaths(@"C:\Temp\roaming"));

            vm.Versions.Add(new VanillaVersion("1.20.1", "release", null));
            vm.Versions.Add(new VanillaVersion("1.21.1", "release", null));

            vm.MarkInstalled(new[] { "1.20.1" });

            Assert.True(vm.Versions[0].IsInstalled);
            Assert.False(vm.Versions[1].IsInstalled);
        }
    }
}

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
    public sealed class ResourceInstallCommandTests
    {
        [Fact]
        public void Install_command_exists_for_the_view()
        {
            ResourceViewModel vm = new ResourceViewModel(new StubCatalog(), new StubInstaller());

            // XAML 里绑的是 InstallCommand；为 null 的话按钮点了不会有任何反应
            Assert.NotNull(vm.InstallCommand);
        }

        [Fact]
        public async Task Executing_install_downloads_the_first_matching_file()
        {
            StubInstaller installer = new StubInstaller();
            ResourceViewModel vm = new ResourceViewModel(new StubCatalog(), installer);

            vm.MinecraftDirectory = @"C:\Temp\roaming\.minecraft";
            vm.VersionFolder = "1.21.1";

            ResourceSummary item = new ResourceSummary(
                "238222", "jei", "JEI", "desc", null, null, 10, ResourceKind.Mod);

            await vm.InstallAsync(item);

            Assert.Equal(1, installer.Calls);
            Assert.Contains("已安装", vm.StatusText);
        }

        private sealed class StubCatalog : IResourceCatalog
        {
            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceSummary>>(Array.Empty<ResourceSummary>());

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(new[]
                {
                    new ResourceFile("jei.jar", "https://example.com/jei.jar", 100, "1", new[] { "1.21.1" }, "fabric")
                });
        }

        private sealed class StubInstaller : IResourceInstaller
        {
            public int Calls { get; private set; }

            public Task<string> InstallAsync(
                ResourceFile file, ResourceKind kind, string minecraftDirectory, string versionFolder,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(minecraftDirectory + "/" + file.Name);
            }
        }
    }
}
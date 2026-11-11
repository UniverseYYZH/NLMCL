using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        [Fact]
        public async Task Rejects_a_downloaded_file_with_a_wrong_sha1()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-res-" + Guid.NewGuid().ToString("N"));
            try
            {
                ResourceViewModel vm = new ResourceViewModel(
                    new ShaCatalog(),
                    new StubInstaller(),
                    fileDownloader: new WritingDownloader());

                vm.MinecraftDirectory = root;
                vm.VersionFolder = "1.21.1";

                ResourceSummary item = new ResourceSummary(
                    "238222", "jei", "JEI", "desc", null, null, 10, ResourceKind.Mod);

                await vm.InstallAsync(item);

                Assert.Contains("校验失败", vm.StatusText);
                Assert.False(File.Exists(
                    Path.Combine(root, "versions", "1.21.1", "mods", "jei.jar")));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class ShaCatalog : IResourceCatalog
        {
            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceSummary>>(Array.Empty<ResourceSummary>());

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(new[]
                {
                    new ResourceFile(
                        "jei.jar", "https://example.com/jei.jar", 5, "1",
                        new[] { "1.21.1" }, "fabric",
                        "0000000000000000000000000000000000000000")
                });
        }

        private sealed class WritingDownloader : IFileDownloader
        {
            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
                => DownloadAsync(url, targetPath, null, cancellationToken);

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "not-the-expected-content", Encoding.UTF8);
                progress?.Report(1);
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
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
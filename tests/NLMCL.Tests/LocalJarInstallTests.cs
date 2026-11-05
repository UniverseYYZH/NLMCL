using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class LocalJarInstallTests
    {
        [Fact]
        public async Task Installing_a_local_jar_copies_it_into_instance_mods()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-jar-" + Guid.NewGuid().ToString("N"));
            string mods = Path.Combine(root, "versions", "1.21.1", "mods");
            Directory.CreateDirectory(mods);

            string jar = Path.Combine(root, "TheShootingStar.jar");
            File.WriteAllBytes(jar, new byte[] { 1, 2, 3 });

            try
            {
                ResourceViewModel vm = new ResourceViewModel(
                    new EmptyCatalog(), new NoopInstaller(), filePicker: new FixedPicker(jar));

                vm.MinecraftDirectory = root;
                vm.VersionFolder = "1.21.1";

                await vm.InstallLocalJarAsync();

                string target = Path.Combine(mods, "TheShootingStar.jar");
                Assert.True(File.Exists(target));
                Assert.Equal(3, new FileInfo(target).Length);
                Assert.Contains("已安装", vm.StatusText);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        private sealed class FixedPicker : IFilePicker
        {
            private readonly string _path;

            public FixedPicker(string path)
            {
                _path = path;
            }

            public string? PickFile(string title, string filter) => _path;
        }

        private sealed class EmptyCatalog : IResourceCatalog
        {
            public Task<System.Collections.Generic.IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<ResourceSummary>>(
                    Array.Empty<ResourceSummary>());

            public Task<System.Collections.Generic.IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<ResourceFile>>(
                    Array.Empty<ResourceFile>());
        }

        private sealed class NoopInstaller : IResourceInstaller
        {
            public Task<string> InstallAsync(
                ResourceFile file, ResourceKind kind, string minecraftDirectory, string versionFolder,
                CancellationToken cancellationToken)
                => Task.FromResult(versionFolder);
        }
    }
}
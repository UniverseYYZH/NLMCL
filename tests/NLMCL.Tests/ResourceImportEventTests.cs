using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ResourceImportEventTests
    {
        [Fact]
        public async Task Import_raises_the_imported_version_event()
        {
            var root = TestSupport.CreateTemporaryDirectory("nlmcl-import-event-");
            var minecraft = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(minecraft);

            var picker = new FixedPicker(Path.Combine(root, "pack.mrpack"));
            var installer = new EventInstaller("Imported-Pack");
            var java = new TestSupport.NoopJavaLocator();

            var resource = new ResourceViewModel(
                new TestSupport.NoopResourceCatalog(),
                new TestSupport.NoopResourceInstaller(),
                filePicker: picker,
                modpackInstaller: installer,
                javaLocator: java);
            resource.SyncFromInstance(minecraft, "1.21.1");

            string? raised = null;
            resource.ModpackImported += (_, versionId) => raised = versionId;

            await resource.ImportModpackAsync();

            Assert.Equal("Imported-Pack", raised);
            Assert.Contains("已创建实例", resource.StatusText);
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

        private sealed class EventInstaller : IModpackInstaller
        {
            private readonly string _versionId;

            public EventInstaller(string versionId)
            {
                _versionId = versionId;
            }

            public Task<ModpackInfo> ReadAsync(string mrpackPath, CancellationToken cancellationToken)
                => Task.FromResult(new ModpackInfo(
                    "Imported Pack", "1.0.0", "1.21.1", ModpackLoader.Fabric, "0.16.10", 1));

            public Task<string> InstallAsync(
                string mrpackPath, string minecraftDirectory, string javaExecutable,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => Task.FromResult(_versionId);

            public Task UpdateAsync(
                string mrpackPath, string instanceDirectory,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ModpackImportSelectionTests
    {
        [Fact]
        public async Task Imported_modpack_is_selected_after_the_import_event()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-modpack-select-");
            string game = Path.Combine(root, ".minecraft");
            Directory.CreateDirectory(Path.Combine(game, "versions"));

            var catalog = new SwitchableCatalog();
            var installer = new ImportingInstaller(catalog);
            var resource = new ResourceViewModel(
                new TestSupport.NoopResourceCatalog(),
                new TestSupport.NoopResourceInstaller(),
                filePicker: new FixedPicker(Path.Combine(root, "pack.mrpack")),
                modpackInstaller: installer,
                javaLocator: new TestSupport.NoopJavaLocator());

            var vm = TestSupport.CreateMain(
                catalog,
                new AppPaths(Path.Combine(root, "roaming")),
                resource: resource);

            vm.CurrentGameDirectory = game;
            await vm.RefreshAsync();
            Assert.NotNull(vm.SelectedVersion);
            vm.ActivateResourceTab();

            bool raised = false;
            resource.ModpackImported += (_, _) => raised = true;
            await resource.ImportModpackAsync();
            Assert.True(raised, resource.StatusText);

            for (int i = 0; i < 100 && vm.SelectedVersion?.FolderName != "Imported-Pack"; i++)
                await Task.Delay(20);

            Assert.Equal("Imported-Pack", vm.SelectedVersion?.FolderName);
        }

        private sealed class SwitchableCatalog : IVersionCatalog
        {
            public bool IncludeImported { get; set; }

            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                var items = new List<MinecraftVersion>
                {
                    new MinecraftVersion("Old-Pack", "Old-Pack", VersionKind.Release, DateTimeOffset.Now)
                };
                if (IncludeImported)
                    items.Add(new MinecraftVersion(
                        "Imported-Pack", "Imported-Pack", VersionKind.Release, DateTimeOffset.Now));
                return Task.FromResult<IReadOnlyList<MinecraftVersion>>(items);
            }
        }

        private sealed class ImportingInstaller : IModpackInstaller
        {
            private readonly SwitchableCatalog _catalog;

            public ImportingInstaller(SwitchableCatalog catalog)
            {
                _catalog = catalog;
            }

            public Task<ModpackInfo> ReadAsync(string mrpackPath, CancellationToken cancellationToken)
                => Task.FromResult(new ModpackInfo(
                    "Imported Pack", "1.0.0", "1.21.1", ModpackLoader.Fabric, "0.16.10", 1));

            public Task<string> InstallAsync(
                string mrpackPath, string minecraftDirectory, string javaExecutable,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
            {
                _catalog.IncludeImported = true;
                return Task.FromResult("Imported-Pack");
            }

            public Task UpdateAsync(
                string mrpackPath, string instanceDirectory,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => Task.CompletedTask;
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
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ModpackRollbackTests
    {
        [Fact]
        public async Task Declining_confirmation_does_not_restore_the_instance()
        {
            RecordingPackages packages = new RecordingPackages();
            MainViewModel vm = TestSupport.CreateMain(
                new OneVersionCatalog(),
                new AppPaths(@"C:\Temp\roaming"),
                dialogs: new NoDialog(),
                instancePackages: packages);

            await vm.InitializeAsync();
            vm.SelectVersion(vm.Versions[0]);
            await Task.Delay(100);

            SetField(vm, "_lastModpackBackup", @"C:\Temp\backup.zip");
            SetField(vm, "_lastModpackBackupFolder", vm.SelectedVersion!.FolderName);

            await vm.RollbackModpackUpdateAsync();

            Assert.Equal(0, packages.Restores);
            Assert.Contains("已取消回滚", vm.ModpackUpdateMessage);
            Assert.False(vm.IsModpackUpdateBusy);
        }

        private static void SetField(MainViewModel vm, string name, object? value)
        {
            typeof(MainViewModel)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, value);
        }

        private sealed class OneVersionCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion("Test", "Test", VersionKind.Release, null, hasClientJar: true)
                });
        }

        private sealed class NoDialog : IDialogService
        {
            public bool Confirm(string title, string message) => false;

            public string? Prompt(string title, string message, string? initialValue = null) => null;
        }

        private sealed class RecordingPackages : IInstancePackageService
        {
            public int Restores { get; private set; }

            public Task<string> ExportAsync(
                string instancePath, string destinationZip, CancellationToken cancellationToken = default)
                => Task.FromResult(destinationZip);

            public Task<string> ImportAsync(
                string packageZip, string versionsDirectory, CancellationToken cancellationToken = default)
                => Task.FromResult(versionsDirectory);

            public Task RestoreAsync(
                string packageZip, string instanceDirectory, CancellationToken cancellationToken = default)
            {
                Restores++;
                return Task.CompletedTask;
            }
        }
    }
}

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
    /// <summary>整合包更新 ViewModel 流程：校验失败不能备份或替换实例。</summary>
    public class ModpackUpdateFlowTests
    {
        private sealed class SingleVersionCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion("Test", "Test", VersionKind.Release, null, hasClientJar: true)
                });
        }

        private sealed class FakeUpdateService : IModpackUpdateService
        {
            public Task<ModpackManifest?> ReadAsync(
                string instanceDirectory, CancellationToken cancellationToken)
                => Task.FromResult<ModpackManifest?>(null);

            public Task<ModpackUpdateInfo?> CheckAsync(
                string instanceDirectory, CancellationToken cancellationToken)
            {
                ModpackManifest manifest = new ModpackManifest
                {
                    Name = "Test Pack",
                    ProjectId = "project",
                    VersionId = "1.0.0",
                    MinecraftVersion = "1.21.1"
                };

                return Task.FromResult<ModpackUpdateInfo?>(new ModpackUpdateInfo(
                    manifest, "1.0.1", "1.0.1", "https://example.invalid/pack.mrpack",
                    "pack.mrpack", DateTimeOffset.UtcNow, new string('0', 40)));
            }

            public Task<string> BackupAsync(
                string instanceDirectory, CancellationToken cancellationToken)
                => throw new InvalidOperationException("校验失败时不应备份");
        }

        private sealed class FakeDownloader : IFileDownloader
        {
            public Task DownloadAsync(
                string url, string targetPath, CancellationToken cancellationToken)
                => DownloadAsync(url, targetPath, null, cancellationToken);

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress,
                CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "not the expected bytes");
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }

        private sealed class RecordingInstaller : IModpackInstaller
        {
            public int UpdateCalls { get; private set; }

            public Task<ModpackInfo> ReadAsync(
                string mrpackPath, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<string> InstallAsync(
                string mrpackPath, string minecraftDirectory, string javaExecutable,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task UpdateAsync(
                string mrpackPath, string instanceDirectory,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
            {
                UpdateCalls++;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Checksum_mismatch_stops_before_backup_and_update()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-modpack-flow-");
            try
            {
                RecordingInstaller installer = new RecordingInstaller();
                MainViewModel vm = TestSupport.CreateMain(
                    new SingleVersionCatalog(),
                    new AppPaths(root),
                    fileDownloader: new FakeDownloader(),
                    modpackUpdates: new FakeUpdateService(),
                    modpackInstaller: installer);

                await vm.RefreshAsync();
                vm.SelectedVersion = vm.Versions[0];
                await vm.CheckModpackUpdateAsync();
                Assert.True(vm.HasModpackUpdateAvailable);

                await vm.ApplyModpackUpdateAsync();

                Assert.Equal(0, installer.UpdateCalls);
                Assert.Contains("校验失败", vm.ModpackUpdateMessage);
                Assert.False(vm.IsModpackUpdateBusy);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

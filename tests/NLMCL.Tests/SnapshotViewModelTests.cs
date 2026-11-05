using System.Linq;
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
    /// <summary>快照面板的 ViewModel 行为（真正的文件操作在 SnapshotServiceTests 里覆盖）。</summary>
    public class SnapshotViewModelTests
    {
        private sealed class FakeSnapshots : ISnapshotService
        {
            public List<SnapshotInfo> Items { get; } = new List<SnapshotInfo>();

            public int CreateCalls { get; private set; }

            public Task<IReadOnlyList<SnapshotInfo>> ListAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SnapshotInfo>>(new List<SnapshotInfo>(Items));

            public Task<SnapshotInfo> CreateAsync(
                string minecraftDirectory, string versionFolder, string note, CancellationToken cancellationToken)
            {
                CreateCalls++;
                SnapshotInfo info = new SnapshotInfo(
                    "snap-" + CreateCalls,
                    versionFolder,
                    versionFolder,
                    note,
                    DateTimeOffset.Now,
                    1024,
                    3,
                    new[] { "saves" });
                Items.Insert(0, info);
                return Task.FromResult(info);
            }

            public Task RestoreAsync(SnapshotInfo snapshot, string minecraftDirectory, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task DeleteAsync(string snapshotId, CancellationToken cancellationToken)
            {
                Items.RemoveAll(item => item.Id == snapshotId);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeDialogs : IDialogService
        {
            public FakeDialogs(bool answer)
            {
                Answer = answer;
            }

            public bool Answer { get; }

        public string? Prompt(string title, string message, string initialValue) => null;

            public int Calls { get; private set; }

            public bool Confirm(string title, string message)
            {
                Calls++;
                return Answer;
            }
        }

        private sealed class OneVersionCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null)
                });
        }

        [Fact]
        public async Task Opening_without_a_selected_version_explains_what_to_do()
        {
            FakeSnapshots snapshots = new FakeSnapshots();
            MainViewModel vm = TestSupport.CreateMain(
                new OneVersionCatalog(), new AppPaths(@"C:\Temp\roaming"), snapshotService: snapshots);

            await vm.OpenSnapshotsAsync();

            Assert.False(vm.IsSnapshotPanelOpen);
            Assert.Contains("点选一个版本", vm.StatusText);
        }

        [Fact]
        public async Task Deleting_asks_for_confirmation_and_cancel_keeps_the_snapshot()
        {
            FakeSnapshots snapshots = new FakeSnapshots();
            snapshots.Items.Add(new SnapshotInfo(
                "s1", "1.20.1", "1.20.1", "别删我", DateTimeOffset.Now, 1024, 1, new[] { "saves" }));

            FakeDialogs dialogs = new FakeDialogs(false);
            MainViewModel vm = TestSupport.CreateMain(
                new OneVersionCatalog(), new AppPaths(@"C:\Temp\roaming"),
                snapshotService: snapshots, dialogs: dialogs);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.OpenSnapshotsAsync();

            await vm.DeleteSnapshotAsync(vm.Snapshots[0]);

            Assert.Equal(1, dialogs.Calls);
            Assert.Single(snapshots.Items);
            Assert.Contains("已取消", vm.StatusText);
        }

        [Fact]
        public async Task Creating_a_snapshot_refreshes_the_list()
        {
            FakeSnapshots snapshots = new FakeSnapshots();
            MainViewModel vm = TestSupport.CreateMain(
                new OneVersionCatalog(), new AppPaths(@"C:\Temp\roaming"), snapshotService: snapshots);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            await vm.OpenSnapshotsAsync();
            Assert.True(vm.IsSnapshotPanelOpen);

            vm.SnapshotNote = "装 mod 前";
            await vm.CreateSnapshotAsync();

            Assert.Equal(1, snapshots.CreateCalls);
            Assert.Single(vm.Snapshots);
            Assert.Equal("装 mod 前", vm.Snapshots[0].Note);
            Assert.Contains("已创建快照", vm.StatusText);

            await vm.DeleteSnapshotAsync(vm.Snapshots[0]);
            Assert.Empty(vm.Snapshots);
        }

        [Fact]
        public async Task Clicking_snapshot_nav_lands_on_a_bound_page_not_a_blank_one()
        {
            FakeSnapshots snapshots = new FakeSnapshots();
            MainViewModel vm = TestSupport.CreateMain(
                new OneVersionCatalog(), new AppPaths(@"C:\Temp\roaming"), snapshotService: snapshots);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            NavigationItem snapshotNav = vm.NavigationGroups
                .SelectMany(g => g.Items)
                .First(i => i.Section == ShellSection.Snapshot);

            vm.SelectSection(snapshotNav);

            // 现在「实例快照」是独立页面：切过去要有 IsSnapshotSection=true 的页面接住，
            // 否则就是之前那个空白页 bug。
            Assert.True(vm.IsSnapshotSection);
            Assert.False(vm.IsVersionsSection);
            Assert.Equal(ShellSection.Snapshot, vm.SelectedNavigation!.Section);
        }
    }
}

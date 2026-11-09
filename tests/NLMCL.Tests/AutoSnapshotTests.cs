using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class AutoSnapshotTests
    {
        [Fact]
        public async Task Creates_auto_snapshot_and_prunes_only_auto_ones()
        {
            FakeSnapshotService snapshots = new FakeSnapshotService();
            snapshots.Items.AddRange(new[]
            {
                Make("auto-1", "[自动] 启动前", 1),
                Make("auto-2", "[自动] 启动前", 2),
                Make("auto-3", "[自动] 启动前", 3),
                Make("manual", "手动备份", 4)
            });

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new AppPaths(@"C:\Temp\roaming"),
                snapshotService: snapshots);

            vm.SelectedVersion = new VersionItem(
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, DateTimeOffset.Now),
                false,
                @"D:\games\.minecraft");
            vm.AutoSnapshotBeforeLaunch = true;
            vm.SnapshotRetention = 2;

            await vm.CreateAutoSnapshotAsync();

            Assert.Equal(2, snapshots.Items.Count(item => item.Note.StartsWith("[自动]", StringComparison.Ordinal)));
            Assert.Contains(snapshots.Items, item => item.Note == "手动备份");
            Assert.Contains("自动快照", vm.StatusText);
        }

        [Fact]
        public async Task Snapshot_failure_does_not_throw()
        {
            FakeSnapshotService snapshots = new FakeSnapshotService { FailCreate = true };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new AppPaths(@"C:\Temp\roaming"),
                snapshotService: snapshots);

            vm.SelectedVersion = new VersionItem(
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, DateTimeOffset.Now),
                false,
                @"D:\games\.minecraft");
            vm.AutoSnapshotBeforeLaunch = true;

            await vm.CreateAutoSnapshotAsync();

            Assert.Contains("自动快照失败", vm.StatusText);
        }

        private static SnapshotInfo Make(string id, string note, int minutes)
        {
            return new SnapshotInfo(
                id, "1.20.1", "1.20.1", note,
                DateTimeOffset.Now.AddMinutes(-minutes), 1024, 1, Array.Empty<string>());
        }

        private sealed class FakeSnapshotService : ISnapshotService
        {
            public List<SnapshotInfo> Items { get; } = new List<SnapshotInfo>();

            public bool FailCreate { get; set; }

            public Task<IReadOnlyList<SnapshotInfo>> ListAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<SnapshotInfo>>(
                    Items.OrderByDescending(item => item.CreatedAt).ToList());
            }

            public Task<SnapshotInfo> CreateAsync(
                string minecraftDirectory, string versionFolder, string note,
                CancellationToken cancellationToken)
            {
                if (FailCreate)
                {
                    throw new InvalidOperationException("磁盘已满");
                }

                SnapshotInfo info = new SnapshotInfo(
                    "auto-new", versionFolder, versionFolder, note,
                    DateTimeOffset.Now, 1024, 1, Array.Empty<string>());
                Items.Add(info);
                return Task.FromResult(info);
            }

            public Task RestoreAsync(
                SnapshotInfo snapshot, string minecraftDirectory, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task DeleteAsync(string snapshotId, CancellationToken cancellationToken)
            {
                Items.RemoveAll(item => item.Id == snapshotId);
                return Task.CompletedTask;
            }
        }
    }
}

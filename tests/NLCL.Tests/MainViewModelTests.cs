using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.App.ViewModels;
using NLCL.Core;
using Xunit;

namespace NLCL.Tests
{
    public class MainViewModelTests
    {
        private sealed class FakeCatalog : IVersionCatalog
        {
            private readonly IReadOnlyList<MinecraftVersion> _items;

            public FakeCatalog(params MinecraftVersion[] items)
            {
                _items = items;
            }

            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(_items);
            }
        }

        private static MainViewModel Create(params MinecraftVersion[] items)
        {
            return TestSupport.CreateMain(new FakeCatalog(items), new AppPaths(@"C:\Temp\roaming"));
        }

        [Fact]
        public async Task Refresh_populates_list_and_reports_count()
        {
            MainViewModel vm = Create(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("24w44a", "24w44a", VersionKind.Snapshot, null));

            await vm.RefreshAsync();

            Assert.Equal(2, vm.Versions.Count);
            Assert.False(vm.IsEmpty);
            Assert.False(vm.IsBusy);
            Assert.Equal("共 2 个版本", vm.StatusText);
        }

        [Fact]
        public async Task Refresh_with_no_versions_shows_empty_state()
        {
            MainViewModel vm = Create();

            await vm.RefreshAsync();

            Assert.Empty(vm.Versions);
            Assert.True(vm.IsEmpty);
        }

        [Fact]
        public async Task Summary_splits_versions_by_kind()
        {
            MainViewModel vm = Create(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null),
                new MinecraftVersion("24w44a", "24w44a", VersionKind.Snapshot, null),
                new MinecraftVersion("b1.7.3", "b1.7.3", VersionKind.OldBeta, null));

            await vm.RefreshAsync();

            Assert.Equal("4 个版本", vm.VersionCountLabel);
            Assert.Equal("正式版 2", vm.ReleaseCountLabel);
            Assert.Equal("快照 1", vm.SnapshotCountLabel);
            Assert.Equal("其他 1", vm.OtherCountLabel);
            Assert.True(vm.HasRelease);
            Assert.True(vm.HasSnapshot);
            Assert.True(vm.HasOther);
        }

        [Fact]
        public async Task Missing_client_jar_exposes_warning_on_row()
        {
            MainViewModel vm = Create(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null, hasClientJar: false));

            await vm.RefreshAsync();

            Assert.False(vm.Versions[0].HasWarning);
            Assert.True(vm.Versions[1].HasWarning);
            Assert.Equal("未找到客户端 JAR，可能无法启动", vm.Versions[1].Warning);
        }

        [Fact]
        public void Default_game_directory_comes_from_paths_not_hardcoded()
        {
            MainViewModel vm = Create();

            Assert.Equal(@"C:\Temp\roaming\.minecraft", vm.CurrentGameDirectory);
        }

        [Fact]
        public void Navigation_is_grouped_and_first_item_is_active()
        {
            MainViewModel vm = Create();

            Assert.Equal(2, vm.NavigationGroups.Count);
            Assert.Equal("工作区", vm.NavigationGroups[0].Caption);
            Assert.Equal(6, vm.NavigationGroups[0].Items.Count);
            Assert.Equal("工具", vm.NavigationGroups[1].Caption);
            Assert.Equal(2, vm.NavigationGroups[1].Items.Count);

            NavigationItem first = vm.NavigationGroups[0].Items[0];
            Assert.True(first.IsActive);
            Assert.Same(first, vm.SelectedNavigation);
        }

        [Fact]
        public void SelectSection_moves_active_flag_and_never_leaves_two_active()
        {
            MainViewModel vm = Create();
            NavigationItem target = vm.NavigationGroups[1].Items[0];

            vm.SelectSection(target);

            Assert.Same(target, vm.SelectedNavigation);
            Assert.True(target.IsActive);

            int activeCount = vm.NavigationGroups
                .SelectMany(g => g.Items)
                .Count(i => i.IsActive);

            Assert.Equal(1, activeCount);
        }

        /// <summary>
        /// 回归：本地版本页扫 D 盘，控制台却去 %APPDATA%\.minecraft 找版本，
        /// 结果"启动失败：未找到版本目录"。游戏目录必须只有一份。
        /// </summary>
        [Fact]
        public void Game_directory_is_shared_with_console_and_download()
        {
            MainViewModel vm = Create();

            vm.CurrentGameDirectory = @"D:\MC\.minecraft";

            Assert.Equal(@"D:\MC\.minecraft", vm.Console.GameDirectory);
            Assert.Equal(@"D:\MC\.minecraft", vm.Download.TargetDirectory);
        }

        /// <summary>回归：选中版本后控制台自动填好版本目录名，不用手抄。</summary>
        [Fact]
        public async Task Selecting_a_version_prefills_console_version()
        {
            MainViewModel vm = Create(
                new MinecraftVersion("1.20.1", "1.20.1-Fabric 0.19.5", VersionKind.Release, null));

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            Assert.Equal("1.20.1-Fabric 0.19.5", vm.Console.VersionId);
        }
    }

    public class AppPathsTests
    {
        [Fact]
        public void Paths_are_derived_from_roaming_root()
        {
            AppPaths paths = new AppPaths(@"C:\Users\x\AppData\Roaming");

            Assert.Equal(@"C:\Users\x\AppData\Roaming\NLCL", paths.DataRoot);
            Assert.Equal(@"C:\Users\x\AppData\Roaming\.minecraft", paths.DefaultMinecraftDirectory);
            Assert.EndsWith("accounts.json", paths.AccountsFile);
        }
    }
}

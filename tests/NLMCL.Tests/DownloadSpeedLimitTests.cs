using System.Linq;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class DownloadSpeedLimitTests
    {
        [Theory]
        [InlineData(0, 42)]
        [InlineData(1, 9)]
        [InlineData(2, 15)]
        [InlineData(5, 21)]
        [InlineData(10, 31)]
        public void Maps_megabytes_to_pcl_setting(int megabytes, int expected)
        {
            Assert.Equal(expected, DownloadSpeedLimits.ToPclSetting(megabytes));
        }

        [Theory]
        [InlineData(42, 0)]
        [InlineData(9, 1)]
        [InlineData(15, 2)]
        [InlineData(21, 5)]
        [InlineData(31, 10)]
        public void Maps_pcl_setting_back_to_megabytes(int setting, int expected)
        {
            Assert.Equal(expected, DownloadSpeedLimits.ToMegabytesPerSecond(setting));
        }

        [Fact]
        public void Retry_policy_matches_current_download_stack()
        {
            Assert.Equal(3, DownloadRetryPolicy.Attempts);
            Assert.Equal(300, DownloadRetryPolicy.FileTimeoutSeconds);
            Assert.Contains("3 次", DownloadRetryPolicy.Describe());
        }

        [Fact]
        public void Settings_view_model_owns_speed_limit_state()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.SelectedSpeedLimit = "2 MB/s";

            Assert.Equal("2 MB/s", vm.SelectedSpeedLimit);
            Assert.Equal("2 MB/s", vm.Settings.SelectedSpeedLimit);
        }

        [Fact]
        public void Settings_view_model_owns_threads_and_mirror_state()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.SelectedThreads = "8";
            vm.Settings.UseMirror = false;

            Assert.Equal("8", vm.SelectedThreads);
            Assert.False(vm.UseMirror);
            Assert.Equal("8", vm.Settings.SelectedThreads);
            Assert.False(vm.Settings.UseMirror);
        }

        [Fact]
        public void Settings_view_model_owns_theme_and_scale_state()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.SelectedTheme = "深色";
            vm.Settings.UiScalePercent = 120;
            vm.Settings.UiScaleMaximizedPercent = 180;

            Assert.Equal("深色", vm.SelectedTheme);
            Assert.Equal(120, vm.UiScalePercent);
            Assert.Equal(180, vm.UiScaleMaximizedPercent);
            Assert.Equal("120%", vm.Settings.UiScaleText);
            Assert.Equal("180%", vm.Settings.UiScaleMaximizedText);
        }

        [Fact]
        public void Settings_view_model_owns_memory_and_launch_args()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.DefaultMemoryMb = 6144;
            vm.Settings.DefaultJvmArgs = "-XX:+UseG1GC";
            vm.Settings.DefaultGameArgs = "--demo";

            Assert.Equal(6144, vm.DefaultMemoryMb);
            Assert.Equal("-XX:+UseG1GC", vm.DefaultJvmArgs);
            Assert.Equal("--demo", vm.DefaultGameArgs);
        }

        [Fact]
        public void Settings_view_model_owns_general_page_snapshot_and_close_state()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.SelectedDefaultPage = "下载中心";
            vm.Settings.AutoSnapshotBeforeLaunch = true;
            vm.Settings.SnapshotRetention = 10;
            vm.Settings.SelectedCloseBehavior = "最小化到托盘";
            vm.Settings.ReduceMotion = true;

            Assert.Equal("下载中心", vm.SelectedDefaultPage);
            Assert.True(vm.AutoSnapshotBeforeLaunch);
            Assert.Equal(10, vm.SnapshotRetention);
            Assert.Equal("最小化到托盘", vm.SelectedCloseBehavior);
            Assert.True(vm.ReduceMotion);
        }

        [Fact]
        public void Settings_view_model_owns_default_java_path()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.DefaultJavaPath = @"C:\Java\bin\javaw.exe";

            Assert.Equal(@"C:\Java\bin\javaw.exe", vm.DefaultJavaPath);
            Assert.Equal(@"C:\Java\bin\javaw.exe", vm.Settings.DefaultJavaPath);
        }

        [Fact]
        public void Settings_view_model_owns_java_list_and_selection()
        {
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            NLMCL.App.ViewModels.JavaOption option = new NLMCL.App.ViewModels.JavaOption(
                @"C:\Java\bin\javaw.exe", "Java 17", "C:\\Java");

            vm.Settings.JavaOptions.Add(option);
            vm.Settings.SelectedJava = option;

            Assert.Same(option, vm.SelectedJava);
            Assert.Same(option, vm.JavaOptions[0]);
            Assert.Equal(@"C:\Java\bin\javaw.exe", vm.DefaultJavaPath);
            Assert.True(option.IsSelected);
        }

        [Fact]
        public void Settings_view_model_uses_injected_services()
        {
            NLMCL.Core.AppPaths paths = new NLMCL.Core.AppPaths(@"C:\Temp\roaming");
            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), paths);

            Assert.Equal(System.IO.Path.Combine(paths.DataRoot, "logs"), vm.Settings.LogDirectory);
            Assert.Same(vm.Cache, vm.Settings.Cache);
            Assert.Same(vm.Transfer, vm.Settings.Transfer);
            Assert.Equal("镜像（元数据）+ 官方 CDN", vm.Settings.DownloadSourceText);
        }

        [Fact]
        public void Settings_search_filters_by_group_and_can_clear()
        {
            NLMCL.App.SettingsSearchVisibilityConverter converter =
                new NLMCL.App.SettingsSearchVisibilityConverter();

            Assert.Equal(
                System.Windows.Visibility.Visible,
                converter.Convert(new object[] { "下载 线程 限速 镜像 网络", "下载" },
                    typeof(System.Windows.Visibility), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(
                System.Windows.Visibility.Collapsed,
                converter.Convert(new object[] { "下载 线程 限速 镜像 网络", "Java" },
                    typeof(System.Windows.Visibility), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(
                System.Windows.Visibility.Visible,
                converter.Convert(new object[] { "下载", "" },
                    typeof(System.Windows.Visibility), null, System.Globalization.CultureInfo.InvariantCulture));

            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            vm.Settings.FilterSettingsCommand.Execute("下载");
            Assert.Equal("下载", vm.Settings.SettingsSearchText);
            vm.Settings.FilterSettingsCommand.Execute("");
            Assert.Equal(string.Empty, vm.Settings.SettingsSearchText);

            vm.Settings.SelectSettingsGroupCommand.Execute("download");
            Assert.True(vm.Settings.IsDownloadGroupActive);
            Assert.False(vm.Settings.IsAllGroupActive);
            Assert.False(vm.Settings.HasNoSettingsSearchResults);
            Assert.Contains("下载", vm.Settings.SettingsSearchText);

            vm.Settings.ClearSettingsSearchCommand.Execute(null);
            Assert.True(vm.Settings.IsAllGroupActive);
            Assert.False(vm.Settings.HasSettingsSearchText);

            vm.Settings.SettingsSearchText = "不存在的设置";
            Assert.True(vm.Settings.HasNoSettingsSearchResults);

            NavigationItem settingsItem = vm.NavigationGroups.SelectMany(group => group.Items)
                .First(item => item.Section == ShellSection.Settings);
            vm.SelectSection(settingsItem);
            Assert.Equal(string.Empty, vm.Settings.SettingsSearchText);
            Assert.True(vm.Settings.IsAllGroupActive);
        }
    }
}

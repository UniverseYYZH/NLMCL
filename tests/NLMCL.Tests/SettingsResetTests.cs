using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class SettingsResetTests
    {
        private static (MainViewModel Vm, AppPaths Paths) Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-reset-" + Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);

            // 传 null 对话框：ConfirmReset 视为"确认"
            MainViewModel vm = TestSupport.CreateMain(new TestSupport.EmptyCatalog(), paths);

            return (vm, paths);
        }

        [Fact]
        public async Task Resetting_appearance_restores_theme_and_scale()
        {
            (MainViewModel vm, AppPaths paths) = Create();

            vm.SelectedTheme = "深色";
            vm.UiScalePercent = 130;
            vm.ReduceMotion = true;
            await vm.ResetAppearanceDefaultsAsync();

            Assert.Equal("浅色", vm.SelectedTheme);
            Assert.Equal(100, vm.UiScalePercent);
            Assert.False(vm.ReduceMotion);
        }

        [Fact]
        public async Task Resetting_download_restores_threads_and_mirror()
        {
            (MainViewModel vm, AppPaths _) = Create();

            vm.SelectedThreads = "16";
            vm.UseMirror = false;
            await vm.ResetDownloadDefaultsAsync();

            Assert.Equal("6", vm.SelectedThreads);
            Assert.True(vm.UseMirror);
        }

        [Fact]
        public async Task Resetting_launch_restores_memory_java_and_args()
        {
            (MainViewModel vm, AppPaths _) = Create();

            vm.DefaultMemoryMb = 8192;
            vm.DefaultJavaPath = @"C:\Java\21\bin\javaw.exe";
            vm.DefaultJvmArgs = "-XX:+UseG1GC";
            vm.DefaultGameArgs = "--demo";

            await vm.ResetLaunchDefaultsAsync();

            Assert.Equal(2048, vm.DefaultMemoryMb);
            Assert.True(string.IsNullOrEmpty(vm.DefaultJavaPath));
            Assert.True(string.IsNullOrEmpty(vm.DefaultJvmArgs));
            Assert.True(string.IsNullOrEmpty(vm.DefaultGameArgs));
        }

        [Fact]
        public async Task Resetting_all_persists_defaults_to_settings_file()
        {
            (MainViewModel vm, AppPaths paths) = Create();

            vm.SelectedTheme = "深色";
            vm.UseMirror = false;
            vm.DefaultMemoryMb = 16384;
            vm.DefaultGameArgs = "--demo";

            await vm.ResetAllDefaultsAsync();

            AppSettings saved = await new JsonAppSettingsStore(paths).LoadAsync(CancellationToken.None);

            Assert.Equal("light", saved.Theme);
            Assert.True(saved.UseMirror);
            Assert.Equal(2048, saved.DefaultMemoryMb);
            Assert.True(string.IsNullOrEmpty(saved.DefaultGameArgs));
        }
    }
}
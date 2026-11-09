using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>设置页：外观（主题/缩放）在启动时应用并随修改保存。</summary>
    public class AppearanceSettingsTests
    {
        private sealed class FakeAppearance : IAppearance
        {
            public string? Theme { get; private set; }

            public double Scale { get; private set; } = 1.0;

            public bool IsMaximized { get; set; }

            public event EventHandler? WindowStateChanged;

            public void ApplyTheme(string theme) => Theme = theme;

            public void ApplyScale(double scale) => Scale = scale;

            public void RaiseWindowStateChanged() => WindowStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class EmptyCatalog : IVersionCatalog
        {
            public Task<System.Collections.Generic.IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<MinecraftVersion>>(
                    Array.Empty<MinecraftVersion>());
        }

        [Fact]
        public async Task Applies_saved_theme_and_scale_on_initialize()
        {
            TestSupport.FakeAppSettings store = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings
                {
                    Theme = "dark",
                    UiScale = 1.1,
                    DownloadThreads = 4,
                    DefaultMemoryMb = 4096
                }
            };

            FakeAppearance appearance = new FakeAppearance();
            MainViewModel vm = TestSupport.CreateMain(
                new EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appearance: appearance, appSettings: store);

            await vm.InitializeAsync();

            Assert.Equal("dark", appearance.Theme);
            Assert.Equal(1.1, appearance.Scale, 3);

            Assert.Equal("深色", vm.SelectedTheme);
            Assert.Equal(110, vm.UiScalePercent, 3);
        }

        [Fact]
        public async Task Changing_default_game_and_jvm_args_persists()
        {
            TestSupport.FakeAppSettings store = new TestSupport.FakeAppSettings();
            MainViewModel vm = TestSupport.CreateMain(
                new EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: store);

            await vm.InitializeAsync();

            vm.DefaultJvmArgs = "-XX:+UseG1GC";
            vm.DefaultGameArgs = "--demo";

            await Task.Delay(300);

            Assert.Equal("-XX:+UseG1GC", store.Current.DefaultJvmArgs);
            Assert.Equal("--demo", store.Current.DefaultGameArgs);
        }

        [Fact]
        public async Task Changing_theme_to_high_contrast_applies_it_and_persists()
        {
            TestSupport.FakeAppSettings store = new TestSupport.FakeAppSettings();
            FakeAppearance appearance = new FakeAppearance();

            MainViewModel vm = TestSupport.CreateMain(
                new EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appearance: appearance, appSettings: store);

            await vm.InitializeAsync();
            Assert.Contains("高对比度", vm.ThemeOptions);

            vm.SelectedTheme = "高对比度";
            await Task.Delay(500);

            Assert.Equal("highcontrast", appearance.Theme);
            Assert.Equal("highcontrast", store.Current.Theme);
        }

        [Fact]
        public async Task Changing_theme_applies_it_and_persists()
        {
            TestSupport.FakeAppSettings store = new TestSupport.FakeAppSettings();
            FakeAppearance appearance = new FakeAppearance();

            MainViewModel vm = TestSupport.CreateMain(
                new EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appearance: appearance, appSettings: store);

            await vm.InitializeAsync();

            vm.SelectedTheme = "深色";
            await Task.Delay(500);

            Assert.Equal("dark", appearance.Theme);
            Assert.Equal("dark", store.Current.Theme);

            vm.UiScalePercent = 120;
            await Task.Delay(500); // 缩放有 250ms 防抖

            Assert.Equal(1.2, appearance.Scale, 3);
            Assert.Equal(1.2, store.Current.UiScale, 3);
        }
    }
}

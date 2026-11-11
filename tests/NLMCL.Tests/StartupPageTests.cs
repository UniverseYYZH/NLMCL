using System;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class StartupPageTests
    {
        [Fact]
        public async Task Starts_on_configured_page()
        {
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings { DefaultPage = "download" }
            };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: settings);

            await vm.InitializeAsync();

            Assert.True(vm.IsDownloadSection);
            Assert.False(vm.IsVersionsSection);
        }

        [Fact]
        public async Task Last_page_is_restored_when_configured()
        {
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings
                {
                    DefaultPage = "last",
                    LastPage = "multiplayer"
                }
            };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: settings);

            await vm.InitializeAsync();

            Assert.True(vm.IsMultiplayerSection);
            Assert.False(vm.IsVersionsSection);
        }

        [Theory]
        [InlineData("world")]
        [InlineData("ningli")]
        [InlineData("snapshot")]
        [InlineData("skin")]
        [InlineData("console")]
        [InlineData("settings")]
        public async Task Starts_on_each_top_level_page(string page)
        {
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings { DefaultPage = page }
            };

            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: settings);

            await vm.InitializeAsync();

            Assert.True(page switch
            {
                "world" => vm.IsWorldSection,
                "ningli" => vm.IsNingliSection,
                "snapshot" => vm.IsSnapshotSection,
                "skin" => vm.IsSkinSection,
                "console" => vm.IsLaunchSection,
                "settings" => vm.IsSettingsSection,
                _ => false
            });
            Assert.False(vm.IsVersionsSection);
        }

        [Theory]
        [InlineData(ShellSection.Versions, "versions")]
        [InlineData(ShellSection.World, "world")]
        [InlineData(ShellSection.Download, "download")]
        [InlineData(ShellSection.Multiplayer, "multiplayer")]
        [InlineData(ShellSection.Ningli, "ningli")]
        [InlineData(ShellSection.Snapshot, "snapshot")]
        [InlineData(ShellSection.Skin, "skin")]
        [InlineData(ShellSection.Launch, "console")]
        [InlineData(ShellSection.Settings, "settings")]
        public async Task Last_page_is_saved_for_every_top_level_page(ShellSection section, string key)
        {
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"),
                appSettings: settings);
            await vm.InitializeAsync();

            vm.SelectSection(section);

            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (!string.Equals(settings.Current.LastPage, key, StringComparison.Ordinal) &&
                   DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(key, settings.Current.LastPage);
        }

        [Fact]
        public void Default_page_options_cover_every_top_level_page()
        {
            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(@"C:\Temp\roaming"));

            Assert.Equal(
                new[] { "上次页面", "世界库", "世界", "下载中心", "联机", "方块宁理", "快照", "皮肤", "日志", "设置" },
                vm.DefaultPageOptions);
        }
    }
}

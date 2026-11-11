using System;
using System.IO;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>设置页「当前生效」摘要使用的只读诊断值。</summary>
    public sealed class SettingsDiagnosticsTests
    {
        [Fact]
        public void Selecting_a_settings_group_keeps_its_cards_visible()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-set-");
            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(root));

            vm.Settings.SelectSettingsGroupCommand.Execute("game");

            Assert.Equal("游戏", vm.Settings.SettingsSearchText);
            Assert.True(vm.Settings.IsGameGroupActive);
            Assert.False(vm.Settings.HasNoSettingsSearchResults);
        }

        [Fact]
        public void Game_directory_summary_reports_existence_and_disk_space()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-disk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                MainViewModel vm = TestSupport.CreateMain(
                    new TestSupport.EmptyCatalog(), new AppPaths(root));

                vm.CurrentGameDirectory = root;

                Assert.Equal("目录存在", vm.GameDirectoryStatusText);
                Assert.Contains("可用", vm.GameDirectoryDiskText);
                Assert.Contains("GB", vm.GameDirectoryDiskText);
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

        [Fact]
        public void Java_summary_explains_auto_and_fixed_paths()
        {
            MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(), new AppPaths(Path.GetTempPath()));

            vm.SelectedJava = new JavaOption(
                null, "自动（按版本要求）", "由启动器挑选");

            Assert.Contains("自动", vm.EffectiveJavaDetailText);

            vm.SelectedJava = new JavaOption(
                @"C:\Java\bin\javaw.exe", "Java 17", "C:\\Java");

            Assert.Contains(@"C:\Java\bin\javaw.exe", vm.EffectiveJavaDetailText);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>多目录：扫描全部目录，选中版本时切换活动目录。</summary>
    public sealed class MultiDirectoryTests
    {
        [Fact]
        public async Task Scans_all_directories_and_switches_active_directory()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-multi-" + Guid.NewGuid().ToString("N"));
            string first = System.IO.Path.Combine(root, "one");
            string second = System.IO.Path.Combine(root, "two");

            try
            {
                System.IO.Directory.CreateDirectory(first);
                System.IO.Directory.CreateDirectory(second);

                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
                settings.Current.GameDirectory = first;
                settings.Current.GameDirectories = new List<string> { first, second };

                MainViewModel vm = TestSupport.CreateMain(
                    new MultiCatalog(first, second),
                    new NLMCL.Core.AppPaths(root),
                    appSettings: settings);

                await vm.InitializeAsync();

                Assert.Equal(2, vm.Versions.Count);
                Assert.Equal(2, vm.GameDirectories.Count);
                Assert.Contains(vm.Versions, item => item.GameDirectory == first);
                Assert.Contains(vm.Versions, item => item.GameDirectory == second);

                VersionItem target = vm.Versions.First(item => item.GameDirectory == second);
                vm.SelectVersion(target);

                Assert.Equal(target.GameDirectory, vm.CurrentGameDirectory);
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Selecting_a_known_directory_persists_the_active_directory()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-select-" + Guid.NewGuid().ToString("N"));
            string first = System.IO.Path.Combine(root, "one");
            string second = System.IO.Path.Combine(root, "two");

            try
            {
                System.IO.Directory.CreateDirectory(first);
                System.IO.Directory.CreateDirectory(second);

                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
                settings.Current.GameDirectory = first;
                settings.Current.GameDirectories = new List<string> { first, second };

                MainViewModel vm = TestSupport.CreateMain(
                    new MultiCatalog(first, second),
                    new NLMCL.Core.AppPaths(root),
                    appSettings: settings);
                await vm.InitializeAsync();

                vm.SelectedGameDirectory = second;

                for (int i = 0; i < 50
                    && !string.Equals(settings.Current.GameDirectory, second, StringComparison.OrdinalIgnoreCase); i++)
                {
                    await Task.Delay(20);
                }

                Assert.Equal(second, vm.CurrentGameDirectory);
                Assert.Equal(second, settings.Current.GameDirectory);
                Assert.Contains(second, settings.Current.GameDirectories);
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Removing_the_active_directory_falls_back_to_the_first_remaining()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-remove-" + Guid.NewGuid().ToString("N"));
            string first = System.IO.Path.Combine(root, "one");
            string second = System.IO.Path.Combine(root, "two");

            try
            {
                System.IO.Directory.CreateDirectory(first);
                System.IO.Directory.CreateDirectory(second);

                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
                settings.Current.GameDirectory = first;
                settings.Current.GameDirectories = new List<string> { first, second };

                MainViewModel vm = TestSupport.CreateMain(
                    new MultiCatalog(first, second),
                    new NLMCL.Core.AppPaths(root),
                    appSettings: settings);
                await vm.InitializeAsync();

                vm.SelectedGameDirectory = second;
                await vm.RemoveGameDirectoryAsync();

                Assert.Equal(new[] { first }, vm.GameDirectories);
                Assert.Equal(first, vm.CurrentGameDirectory);
                Assert.Equal(first, vm.SelectedGameDirectory);
                Assert.Equal(first, settings.Current.GameDirectory);
                Assert.Equal(new[] { first }, settings.Current.GameDirectories);
                Assert.DoesNotContain(vm.Versions, item =>
                    string.Equals(item.GameDirectory, second, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Removing_a_directory_stored_with_a_trailing_separator_still_works()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-remove-normalized-" + Guid.NewGuid().ToString("N"));
            string first = System.IO.Path.Combine(root, "one");
            string second = System.IO.Path.Combine(root, "two");

            try
            {
                System.IO.Directory.CreateDirectory(first);
                System.IO.Directory.CreateDirectory(second);

                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
                settings.Current.GameDirectory = first;
                settings.Current.GameDirectories = new List<string> { first, second };

                MainViewModel vm = TestSupport.CreateMain(
                    new MultiCatalog(first, second),
                    new NLMCL.Core.AppPaths(root),
                    appSettings: settings);
                await vm.InitializeAsync();

                // 旧设置或手工编辑可能把目录写成带尾部分隔符的形式。
                vm.GameDirectories.Clear();
                vm.GameDirectories.Add(first + System.IO.Path.DirectorySeparatorChar);
                vm.GameDirectories.Add(second + System.IO.Path.DirectorySeparatorChar);
                vm.CurrentGameDirectory = second;

                await vm.RemoveGameDirectoryAsync();

                Assert.Equal(new[] { first }, vm.GameDirectories);
                Assert.Equal(first, vm.CurrentGameDirectory);
                Assert.Equal(first, vm.SelectedGameDirectory);
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Add_folder_button_uses_folder_picker()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-pick-" + Guid.NewGuid().ToString("N"));
            string picked = System.IO.Path.Combine(root, ".minecraft");

            try
            {
                System.IO.Directory.CreateDirectory(picked);

                TestSupport.FakeFolderPicker picker = new TestSupport.FakeFolderPicker
                {
                    Next = picked
                };

                MainViewModel vm = TestSupport.CreateMain(
                    new TestSupport.EmptyCatalog(),
                    new NLMCL.Core.AppPaths(root),
                    folderPicker: picker);

                await vm.InitializeAsync();
                await vm.AddGameDirectoryAsync();

                Assert.Contains(vm.GameDirectories, item =>
                    string.Equals(item, picked, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(picked, vm.CurrentGameDirectory);
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Activating_resource_tab_rebinds_the_current_directory()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-resource-" + Guid.NewGuid().ToString("N"));
            string first = System.IO.Path.Combine(root, "one");
            string second = System.IO.Path.Combine(root, "two");

            try
            {
                System.IO.Directory.CreateDirectory(first);
                System.IO.Directory.CreateDirectory(second);

                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
                settings.Current.GameDirectory = first;
                settings.Current.GameDirectories = new List<string> { first, second };

                ResourceViewModel resource = new ResourceViewModel(
                    new TestSupport.NoopResourceCatalog(),
                    new TestSupport.NoopResourceInstaller());
                MainViewModel vm = TestSupport.CreateMain(
                    new MultiCatalog(first, second),
                    new NLMCL.Core.AppPaths(root),
                    appSettings: settings,
                    resource: resource);
                await vm.InitializeAsync();

                vm.CurrentGameDirectory = second;
                vm.ActivateResourceTab();

                Assert.Equal(second, resource.MinecraftDirectory);
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        private sealed class MultiCatalog : IVersionCatalog
        {
            private readonly string _first;
            private readonly string _second;

            public MultiCatalog(string first, string second)
            {
                _first = first;
                _second = second;
            }

            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                string id = string.Equals(minecraftDirectory, _first, StringComparison.OrdinalIgnoreCase)
                    ? "1.20.1"
                    : "1.21.1";

                return Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion(id, id, VersionKind.Release, DateTimeOffset.Now)
                });
            }
        }
    }
}

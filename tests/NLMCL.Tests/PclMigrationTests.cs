using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class PclMigrationTests
    {
        [Fact]
        public async Task Probes_and_activates_a_valid_pcl_directory()
        {
            string root = CreateRoot();
            try
            {
                string pcl = CreatePcl(root, "pcl");
                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
                {
                    Current = new NLMCL.Abstractions.AppSettings
                    {
                        GameDirectory = Path.Combine(root, "current"),
                        GameDirectories = new List<string>()
                    }
                };
                MainViewModel vm = CreateMain(root, settings, canWrite: true);

                await vm.InitializeAsync();
                vm.ProbePclDirectory(pcl);

                Assert.True(vm.PclMigrationCanActivate);
                Assert.False(vm.PclMigrationAlreadyAdded);
                Assert.Contains("1 个版本", vm.PclMigrationResult);
                Assert.Contains("mods：有", vm.PclMigrationResult);
                Assert.Contains("saves：有", vm.PclMigrationResult);
                Assert.Contains("可写", vm.PclMigrationResult);
                Assert.Empty(vm.PclMigrationError);

                await vm.ActivatePclDirectoryAsync();

                Assert.Equal(pcl, vm.CurrentGameDirectory);
                Assert.Contains(pcl, vm.GameDirectories);
                Assert.Contains("已加入", vm.PclMigrationStatus);
                Assert.Equal(pcl, settings.Current.GameDirectory);
            }
            finally
            {
                Delete(root);
            }
        }

        [Fact]
        public async Task Does_not_add_the_same_directory_twice()
        {
            string root = CreateRoot();
            try
            {
                string pcl = CreatePcl(root, "pcl");
                TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
                {
                    Current = new NLMCL.Abstractions.AppSettings
                    {
                        GameDirectory = pcl,
                        GameDirectories = new List<string> { pcl }
                    }
                };
                MainViewModel vm = CreateMain(root, settings, canWrite: true);
                await vm.InitializeAsync();

                vm.ProbePclDirectory(pcl);

                Assert.True(vm.PclMigrationCanActivate);
                Assert.True(vm.PclMigrationAlreadyAdded);
                Assert.Contains("已在已添加列表", vm.PclMigrationResult);

                await vm.ActivatePclDirectoryAsync();

                Assert.Single(vm.GameDirectories);
                Assert.Contains("已在列表中", vm.PclMigrationStatus);
            }
            finally
            {
                Delete(root);
            }
        }

        [Fact]
        public void Rejects_a_directory_without_versions()
        {
            string root = CreateRoot();
            try
            {
                string invalid = Path.Combine(root, "not-pcl");
                Directory.CreateDirectory(invalid);
                MainViewModel vm = CreateMain(root, new TestSupport.FakeAppSettings(), canWrite: true);

                vm.ProbePclDirectory(invalid);

                Assert.False(vm.PclMigrationCanActivate);
                Assert.Contains("versions", vm.PclMigrationError);
                Assert.Empty(vm.PclMigrationResult);
            }
            finally
            {
                Delete(root);
            }
        }

        [Fact]
        public async Task Unwritable_directory_activates_but_keeps_the_environment_warning()
        {
            string root = CreateRoot();
            try
            {
                string pcl = CreatePcl(root, "pcl");
                MainViewModel vm = CreateMain(root, new TestSupport.FakeAppSettings(), canWrite: false);
                await vm.InitializeAsync();

                vm.ProbePclDirectory(pcl);
                Assert.True(vm.PclMigrationCanActivate);
                Assert.Contains("不可写", vm.PclMigrationResult);

                await vm.ActivatePclDirectoryAsync();

                Assert.Equal(pcl, vm.CurrentGameDirectory);
                Assert.Contains("无法写入游戏目录", vm.EnvironmentWarning);
            }
            finally
            {
                Delete(root);
            }
        }

        private static MainViewModel CreateMain(string root, TestSupport.FakeAppSettings settings, bool canWrite)
        {
            PclMigrationProbe probe = new PclMigrationProbe(_ => canWrite);
            return TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new AppPaths(root),
                appSettings: settings,
                pclMigration: probe);
        }

        private static string CreatePcl(string root, string name)
        {
            string pcl = Path.Combine(root, name);
            string version = Path.Combine(pcl, "versions", "1.21.1");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{}");
            Directory.CreateDirectory(Path.Combine(pcl, "mods"));
            File.WriteAllText(Path.Combine(pcl, "mods", "test.jar"), "x");
            Directory.CreateDirectory(Path.Combine(pcl, "saves", "world"));
            File.WriteAllText(Path.Combine(pcl, "saves", "world", "level.dat"), "x");
            return pcl;
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "nlmcl-pcl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void Delete(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

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
    /// <summary>应用设置：游戏目录要记住，否则每次启动都得重选。</summary>
    public class AppSettingsTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-app-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Round_trips_game_directory()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                Assert.Null((await store.LoadAsync(CancellationToken.None)).GameDirectory);

                await store.SaveAsync(
                    new AppSettings
                    {
                        GameDirectory = @"D:\games\.minecraft",
                        MicrosoftClientId = "11111111-2222-3333-4444-555555555555",
                        DefaultGameArgs = "--demo --global-flag",
                        DownloadSpeedLimitMb = 5,
                        DefaultPage = "download",
                        LastPage = "multiplayer",
                        AutoSnapshotBeforeLaunch = true,
                        SnapshotRetention = 10,
                        CloseBehavior = "tray"
                    },
                    CancellationToken.None);

                AppSettings loaded = await store.LoadAsync(CancellationToken.None);
                Assert.Equal(@"D:\games\.minecraft", loaded.GameDirectory);
                Assert.Equal("11111111-2222-3333-4444-555555555555", loaded.MicrosoftClientId);
                Assert.Equal("--demo --global-flag", loaded.DefaultGameArgs);
                Assert.Equal(5, loaded.DownloadSpeedLimitMb);
                Assert.Equal("download", loaded.DefaultPage);
                Assert.Equal("multiplayer", loaded.LastPage);
                Assert.True(loaded.AutoSnapshotBeforeLaunch);
                Assert.Equal(10, loaded.SnapshotRetention);
                Assert.Equal("tray", loaded.CloseBehavior);

                Assert.True(File.Exists(paths.SettingsFile));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Broken_settings_file_falls_back_to_defaults()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.SettingsFile, "{ 坏掉的 json");

                // 坏配置不该拦住启动
                AppSettings loaded = await store.LoadAsync(CancellationToken.None);
                Assert.Null(loaded.GameDirectory);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Schema_six_settings_gain_reduce_motion_default_false()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.SettingsFile,
                    "{\"schemaVersion\":6,\"theme\":\"dark\"}");

                AppSettings loaded = await store.LoadAsync(CancellationToken.None);

                Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
                Assert.Equal("dark", loaded.Theme);
                Assert.False(loaded.ReduceMotion);

                loaded.ReduceMotion = false;
                await store.SaveAsync(loaded, CancellationToken.None);

                string saved = File.ReadAllText(paths.SettingsFile);
                Assert.Contains("\"schemaVersion\": " + AppSettings.CurrentSchemaVersion, saved);
                Assert.Contains("\"reduceMotion\": false", saved);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Portable_settings_round_trip_under_schema_seven()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root, portable: true);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                AppSettings settings = await store.LoadAsync(CancellationToken.None);
                settings.ReduceMotion = true;
                settings.Theme = "dark";
                await store.SaveAsync(settings, CancellationToken.None);

                Assert.StartsWith(Path.Combine(root, "data"), paths.SettingsFile);
                Assert.True(File.Exists(paths.SettingsFile));

                AppSettings reloaded = await store.LoadAsync(CancellationToken.None);
                Assert.Equal(AppSettings.CurrentSchemaVersion, reloaded.SchemaVersion);
                Assert.True(reloaded.ReduceMotion);
                Assert.Equal("dark", reloaded.Theme);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Reduce_motion_round_trips()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                AppSettings settings = await store.LoadAsync(CancellationToken.None);
                settings.ReduceMotion = true;
                await store.SaveAsync(settings, CancellationToken.None);

                AppSettings reloaded = await store.LoadAsync(CancellationToken.None);

                Assert.True(reloaded.ReduceMotion);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Old_settings_gain_current_schema_version()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.SettingsFile,
                    "{\"theme\":\"dark\",\"uiScale\":1.2,\"downloadThreads\":4}");

                AppSettings loaded = await store.LoadAsync(CancellationToken.None);

                Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
                Assert.False(loaded.IsReadOnly);
                Assert.Equal("dark", loaded.Theme);
                Assert.Equal(4, loaded.DownloadThreads);

                loaded.Theme = "light";
                await store.SaveAsync(loaded, CancellationToken.None);

                string saved = File.ReadAllText(paths.SettingsFile);
                Assert.Contains("\"schemaVersion\": " + AppSettings.CurrentSchemaVersion, saved);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Newer_settings_are_not_overwritten_and_are_backed_up()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAppSettingsStore store = new JsonAppSettingsStore(paths);

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.SettingsFile,
                    "{\"schemaVersion\":999,\"theme\":\"dark\",\"downloadThreads\":8}");

                AppSettings loaded = await store.LoadAsync(CancellationToken.None);

                Assert.True(loaded.IsReadOnly);
                Assert.Contains("999", loaded.ReadOnlyReason);
                Assert.Equal("dark", loaded.Theme);
                Assert.True(File.Exists(paths.SettingsFile + ".newer"));

                loaded.Theme = "light";
                await store.SaveAsync(loaded, CancellationToken.None);

                string saved = File.ReadAllText(paths.SettingsFile);
                Assert.Contains("\"schemaVersion\":999", saved);
                Assert.Contains("\"theme\":\"dark\"", saved);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private sealed class OneVersionCatalog : IVersionCatalog
        {
            public string? LastDirectory { get; private set; }

            public Task<System.Collections.Generic.IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                LastDirectory = minecraftDirectory;
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<MinecraftVersion>>(
                    new[] { new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null) });
            }
        }

        [Fact]
        public async Task Restores_saved_directory_on_initialize()
        {
            string saved = Path.Combine(Path.GetTempPath(), "nlmcl-saved-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saved);

            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings { GameDirectory = saved }
            };

            OneVersionCatalog catalog = new OneVersionCatalog();

            MainViewModel vm = new MainViewModel(
                catalog, paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                new TestSupport.NoopLauncher(),
                new TestSupport.NoopJavaLocator(),
                new TestSupport.MemoryAccountStore(),
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(),
                settings);

            try
            {
                await vm.InitializeAsync();

                Assert.Equal(saved, vm.CurrentGameDirectory);
                // 扫描必须用恢复后的目录
                Assert.Equal(saved, catalog.LastDirectory);
            }
            finally
            {
                TryDelete(saved);
            }
        }

        [Fact]
        public async Task Saves_directory_after_successful_scan()
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();

            MainViewModel vm = new MainViewModel(
                new OneVersionCatalog(), paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                new TestSupport.NoopLauncher(),
                new TestSupport.NoopJavaLocator(),
                new TestSupport.MemoryAccountStore(),
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(),
                settings);

            vm.CurrentGameDirectory = @"D:\custom\.minecraft";
            await vm.RefreshAsync();

            Assert.Equal(@"D:\custom\.minecraft", settings.Current.GameDirectory);
        }

        [Fact]
        public async Task Missing_saved_directory_is_ignored()
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings
            {
                Current = new AppSettings { GameDirectory = @"D:\已被删除的目录" }
            };

            MainViewModel vm = new MainViewModel(
                new OneVersionCatalog(), paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                new TestSupport.NoopLauncher(),
                new TestSupport.NoopJavaLocator(),
                new TestSupport.MemoryAccountStore(),
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(),
                settings);

            await vm.InitializeAsync();

            // 目录不存在就回落到默认，而不是扫描一个不存在的路径
            Assert.Equal(paths.DefaultMinecraftDirectory, vm.CurrentGameDirectory);
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

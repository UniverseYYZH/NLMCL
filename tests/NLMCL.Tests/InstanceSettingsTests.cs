using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    public class JsonInstanceSettingsStoreTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-inst-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Round_trips_per_version()
        {
            string root = NewRoot();
            JsonInstanceSettingsStore store = new JsonInstanceSettingsStore();

            try
            {
                await store.SaveAsync(root, "1.21.1",
                    new InstanceSettings
                    {
                        MemoryMb = 6144,
                        JavaPath = @"C:\jdk21\bin\javaw.exe",
                        UseProxy = true,
                        ProxyHost = "127.0.0.1",
                        ProxyPort = 7890,
                        SoftwareOpenGl = true
                    },
                    CancellationToken.None);

                await store.SaveAsync(root, "1.20.1",
                    new InstanceSettings { MemoryMb = 2048 },
                    CancellationToken.None);

                // 两个版本的设置互不干扰 —— 这是"按实例存"的核心保证
                InstanceSettings first = await store.LoadAsync(root, "1.21.1", CancellationToken.None);
                Assert.Equal(6144, first.MemoryMb);
                Assert.Equal(@"C:\jdk21\bin\javaw.exe", first.JavaPath);
                Assert.True(first.UseProxy);
                Assert.Equal("127.0.0.1", first.ProxyHost);
                Assert.Equal(7890, first.ProxyPort);
                Assert.True(first.SoftwareOpenGl);

                InstanceSettings second = await store.LoadAsync(root, "1.20.1", CancellationToken.None);
                Assert.Equal(2048, second.MemoryMb);
                Assert.Null(second.JavaPath);

                // 设置文件就在版本目录里，复制版本文件夹时会跟着走
                string file = Path.Combine(root, "versions", "1.21.1", JsonInstanceSettingsStore.FileName);
                Assert.True(File.Exists(file));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Missing_or_broken_file_falls_back_to_defaults()
        {
            string root = NewRoot();
            JsonInstanceSettingsStore store = new JsonInstanceSettingsStore();

            try
            {
                InstanceSettings missing = await store.LoadAsync(root, "1.21.1", CancellationToken.None);
                Assert.Equal(InstanceSettings.DefaultMemoryMb, missing.MemoryMb);

                string folder = Path.Combine(root, "versions", "1.21.1");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, JsonInstanceSettingsStore.FileName), "{ broken");

                // 坏配置不该拦住启动
                InstanceSettings broken = await store.LoadAsync(root, "1.21.1", CancellationToken.None);
                Assert.Equal(InstanceSettings.DefaultMemoryMb, broken.MemoryMb);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Extra_arguments_are_split_on_whitespace()
        {
            InstanceSettings settings = new InstanceSettings { ExtraArguments = "  --width 1280   --height 720 " };

            var parts = settings.ParsedExtraArguments();

            Assert.Equal(4, parts.Count);
            Assert.Equal("--width", parts[0]);
            Assert.Equal("1280", parts[1]);

            Assert.Empty(new InstanceSettings().ParsedExtraArguments());
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

    public class InstanceSettingsFlowTests
    {
        private sealed class OneVersionCatalog : IVersionCatalog
        {
            public Task<System.Collections.Generic.IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<MinecraftVersion>>(
                    new[] { new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null) });
            }
        }

        private sealed class TwoVersionCatalog : IVersionCatalog
        {
            public Task<System.Collections.Generic.IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<MinecraftVersion>>(
                    new[]
                    {
                        new MinecraftVersion("slow", "slow", VersionKind.Release, null),
                        new MinecraftVersion("fast", "fast", VersionKind.Release, null),
                    });
            }
        }

        private sealed class DelayedSettingsStore : IInstanceSettingsStore
        {
            public TaskCompletionSource<InstanceSettings> Slow { get; } =
                new TaskCompletionSource<InstanceSettings>();

            public Task<InstanceSettings> LoadAsync(string minecraftDirectory, string versionId,
                CancellationToken cancellationToken)
            {
                return versionId == "slow"
                    ? Slow.Task
                    : Task.FromResult(new InstanceSettings { MemoryMb = 2048 });
            }

            public Task SaveAsync(string minecraftDirectory, string versionId, InstanceSettings settings,
                CancellationToken cancellationToken) => Task.CompletedTask;

            public bool Exists(string minecraftDirectory, string versionId) => true;
        }

        private sealed class RecordingLauncher : IGameLauncher
        {
            public LaunchRequest? LastRequest { get; private set; }

            public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new LaunchResult(1, true, "ok"));
            }
        }

        private sealed class AnyJava : IJavaLocator
        {
            public JavaRuntime Java { get; } = TestSupport.CreateJava(21);

            public Task<System.Collections.Generic.IReadOnlyList<JavaRuntime>> FindInstalledAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<JavaRuntime>>(
                    new[] { Java });
            }
        }

        private static MainViewModel Create(
            RecordingLauncher launcher, IInstanceSettingsStore store,
            IVersionCatalog? catalog = null)
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            return new MainViewModel(
                catalog ?? new OneVersionCatalog(), paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                launcher, new AnyJava(),
                new TestSupport.MemoryAccountStore(),
                new TestSupport.NoopAuthenticator(),
                store,
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());
        }

        [Fact]
        public async Task Settings_are_loaded_on_selection_and_used_at_launch()
        {
            TestSupport.MemoryInstanceSettingsStore store = new TestSupport.MemoryInstanceSettingsStore();
            store.Items["1.21.1"] = new InstanceSettings
            {
                MemoryMb = 6144,
                JavaPath = @"C:\jdk17\bin\javaw.exe",
                ExtraArguments = "--width 1280"
            };

            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(launcher, store);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await Task.Delay(30); // 设置是异步加载的

            Assert.True(vm.HasSelection);
            Assert.Equal(6144, vm.MemoryMb);
            Assert.Equal(@"C:\jdk17\bin\javaw.exe", vm.JavaPath);
            Assert.Equal("--width 1280", vm.ExtraArguments);

            await vm.LaunchSelectedAsync();

            // 指定了 Java 就不该用自动挑的那个
            Assert.Equal(@"C:\jdk17\bin\javaw.exe", launcher.LastRequest!.JavaExecutable);
            Assert.Equal(6144, launcher.LastRequest.MaximumMemoryMb);
        }

        [Fact]
        public async Task Without_java_path_launcher_auto_picks()
        {
            TestSupport.MemoryInstanceSettingsStore store = new TestSupport.MemoryInstanceSettingsStore();
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(launcher, store);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await Task.Delay(30);

            await vm.LaunchSelectedAsync();

            Assert.EndsWith(Path.Combine("bin", "javaw.exe"), launcher.LastRequest!.JavaExecutable,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Saving_requires_a_selected_version()
        {
            TestSupport.MemoryInstanceSettingsStore store = new TestSupport.MemoryInstanceSettingsStore();
            MainViewModel vm = Create(new RecordingLauncher(), store);

            await vm.SaveSettingsAsync();

            Assert.Empty(store.Items);
            Assert.Contains("请先选择", vm.StatusText);
        }

        [Fact]
        public async Task Slow_settings_load_does_not_overwrite_the_new_selection()
        {
            DelayedSettingsStore store = new DelayedSettingsStore();
            MainViewModel vm = Create(new RecordingLauncher(), store, new TwoVersionCatalog());

            await vm.RefreshAsync();
            VersionItem slow = vm.Versions.First(v => v.FolderName == "slow");
            VersionItem fast = vm.Versions.First(v => v.FolderName == "fast");

            vm.SelectVersion(slow);
            vm.SelectVersion(fast);
            await Task.Delay(30);

            store.Slow.SetResult(new InstanceSettings { MemoryMb = 6144 });
            await Task.Delay(30);

            Assert.Same(fast, vm.SelectedVersion);
            Assert.Equal(2048, vm.MemoryMb);
        }
    }
}

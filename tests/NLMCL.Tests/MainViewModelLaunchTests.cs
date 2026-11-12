using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>启动链路与"装完自动刷新"的测试。</summary>
    public class MainViewModelLaunchTests
    {
        private sealed class CountingCatalog : IVersionCatalog
        {
            private readonly IReadOnlyList<MinecraftVersion> _items;

            public CountingCatalog(params MinecraftVersion[] items)
            {
                _items = items;
            }

            public int Calls { get; private set; }

            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_items);
            }
        }

        private sealed class RecordingLauncher : IGameLauncher
        {
            public LaunchRequest? LastRequest { get; private set; }

            public LaunchResult Result { get; set; } = new LaunchResult(4242, true, "ok");

            public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(Result);
            }
        }

        private sealed class FixedJavaLocator : IJavaLocator
        {
            private readonly IReadOnlyList<JavaRuntime> _javas;

            public FixedJavaLocator(params JavaRuntime[] javas)
            {
                _javas = javas;
            }

            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_javas);
            }
        }

        private static MainViewModel Create(
            IVersionCatalog catalog, IGameLauncher launcher, IJavaLocator javas,
            IInstanceSettingsStore? instanceSettings = null)
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            return new MainViewModel(
                catalog, paths, new DownloadViewModel(new TestSupport.EmptyProvider(), new TestSupport.NoopInstaller(), paths),
                TestSupport.CreateConsole(paths), launcher, javas, new TestSupport.MemoryAccountStore(), new TestSupport.NoopAuthenticator(), instanceSettings ?? new TestSupport.MemoryInstanceSettingsStore(), new TestSupport.FakeNetwork(), new TestSupport.FakeClipboard(), new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());
        }

        [Fact]
        public async Task Selecting_a_version_marks_it_and_sets_launch_target()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null));

            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator());
            await vm.RefreshAsync();

            vm.SelectVersion(vm.Versions[1]);

            Assert.Same(vm.Versions[1], vm.SelectedVersion);
            Assert.True(vm.Versions[1].IsSelected);
            Assert.False(vm.Versions[0].IsSelected);

            // 再点另一个，旧的高亮必须消掉
            vm.SelectVersion(vm.Versions[0]);
            Assert.False(vm.Versions[1].IsSelected);
            Assert.True(vm.Versions[0].IsSelected);

        }

        [Fact]
        public async Task Launch_without_selection_asks_user_to_pick()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator());

            await vm.LaunchSelectedAsync();

            Assert.Null(launcher.LastRequest);
            Assert.Contains("请先", vm.StatusText);
        }

        [Fact]
        public async Task Restart_is_ignored_while_a_launch_is_in_progress()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator());
            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.IsLaunching = true;

            await vm.RestartGameAsync();

            Assert.Null(launcher.LastRequest);
        }

        [Fact]
        public async Task Launch_without_java_reports_instead_of_starting()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator());
            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            await vm.LaunchSelectedAsync();

            Assert.Null(launcher.LastRequest);
            Assert.Contains("Java", vm.StatusText);
            Assert.False(vm.IsLaunching);
        }

        [Fact]
        public async Task Launch_passes_selected_java_and_folder_name()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            JavaRuntime java17 = TestSupport.CreateJava(17);
            JavaRuntime java21 = TestSupport.CreateJava(21);

            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(java17, java21));
            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            await vm.LaunchSelectedAsync();

            Assert.NotNull(launcher.LastRequest);
            Assert.Equal("1.21.1", launcher.LastRequest!.VersionId);

            // 两个 Java 中应该挑高版本（这里断言的是"挑中了哪一个实例"）
            Assert.Equal(java21.ExecutablePath, launcher.LastRequest.JavaExecutable);
            Assert.Contains("已启动", vm.StatusText);
        }

        [Fact]
        public async Task Launch_keeps_global_jvm_arguments()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(TestSupport.CreateJava(21)));

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.DefaultJvmArgs = "-XX:+UseG1GC -XX:MaxGCPauseMillis=50";

            await vm.LaunchSelectedAsync();

            Assert.NotNull(launcher.LastRequest);
            Assert.Contains("UseG1GC", launcher.LastRequest!.ExtraJvmArguments);
        }

        [Fact]
        public async Task Launch_passes_instance_proxy_to_jvm_arguments()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            TestSupport.MemoryInstanceSettingsStore settings = new TestSupport.MemoryInstanceSettingsStore();
            settings.Items["1.21.1"] = new InstanceSettings
            {
                UseProxy = true,
                ProxyHost = "127.0.0.1",
                ProxyPort = 7890
            };
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(TestSupport.CreateJava(21)), settings);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LoadInstanceSettingsAsync(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.NotNull(launcher.LastRequest);
            string jvm = launcher.LastRequest!.ExtraJvmArguments;
            Assert.Contains("-Dhttp.proxyHost=127.0.0.1", jvm);
            Assert.Contains("-Dhttp.proxyPort=7890", jvm);
            Assert.Contains("-Dhttps.proxyHost=127.0.0.1", jvm);
        }

        [Fact]
        public async Task Launch_passes_software_opengl_flag_to_jvm_arguments()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            TestSupport.MemoryInstanceSettingsStore settings = new TestSupport.MemoryInstanceSettingsStore();
            settings.Items["1.21.1"] = new InstanceSettings { SoftwareOpenGl = true };
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(TestSupport.CreateJava(21)), settings);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LoadInstanceSettingsAsync(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.NotNull(launcher.LastRequest);
            Assert.Contains(
                "-Dorg.lwjgl.opengl.Display.allowSoftwareOpenGL=true",
                launcher.LastRequest!.ExtraJvmArguments);
        }

        [Fact]
        public async Task Launch_with_incomplete_proxy_reports_instead_of_starting()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            TestSupport.MemoryInstanceSettingsStore settings = new TestSupport.MemoryInstanceSettingsStore();
            settings.Items["1.21.1"] = new InstanceSettings { UseProxy = true };
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(TestSupport.CreateJava(21)), settings);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LoadInstanceSettingsAsync(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.Null(launcher.LastRequest);
            Assert.Contains("代理设置不完整", vm.StatusText);
        }

        [Fact]
        public async Task Successful_launch_records_last_launch_time()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            TestSupport.FakeAppSettings settings = new TestSupport.FakeAppSettings();
            MainViewModel vm = TestSupport.CreateMain(
                catalog,
                new AppPaths(@"C:\Temp\roaming"),
                javaLocator: new FixedJavaLocator(TestSupport.CreateJava(21)),
                appSettings: settings);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            string instancePath = vm.Versions[0].InstancePath;

            await vm.LaunchSelectedAsync();

            Assert.True(settings.Current.LastLaunched.ContainsKey(instancePath));
            Assert.StartsWith("最后启动 ", vm.Versions[0].LastLaunchLabel);
            Assert.Contains("已启动", vm.StatusText);
        }

        private sealed class SingleVersionProvider : IVersionListProvider
        {
            public Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<VanillaVersion>>(new[]
                {
                    new VanillaVersion("1.21.1", "release", new DateTimeOffset(2024, 8, 8, 0, 0, 0, TimeSpan.Zero))
                });
            }
        }

        [Fact]
        public async Task Install_completion_triggers_version_rescan()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");

            // 走真实路径：下载页真的装一次，主窗口应当自动重扫
            DownloadViewModel download = new DownloadViewModel(
                new SingleVersionProvider(), new TestSupport.NoopInstaller(), paths);

            MainViewModel vm = new MainViewModel(
                catalog, paths, download, TestSupport.CreateConsole(paths), new RecordingLauncher(), new FixedJavaLocator(), new TestSupport.MemoryAccountStore(), new TestSupport.NoopAuthenticator(), new TestSupport.MemoryInstanceSettingsStore(), new TestSupport.FakeNetwork(), new TestSupport.FakeClipboard(), new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());

            await vm.RefreshAsync();
            int before = catalog.Calls;

            await download.LoadVersionsAsync();
            download.SelectedVersion = download.Versions[0];
            await download.InstallAsync();
            await Task.Delay(100);

            Assert.True(catalog.Calls > before, "安装完成后应重新扫描本地版本");
        }

        private sealed class StubSession : IGameSession
        {
            public event EventHandler<GameOutputLine>? Output;

            public event EventHandler<int>? Exited;

            public int ProcessId => 777;

            public bool IsRunning { get; private set; } = true;

            public int? ExitCode { get; private set; }

            public void Stop() => IsRunning = false;

            public void Dispose()
            {
            }

            public void Emit(string text) => Output?.Invoke(this, new GameOutputLine(text, false));
        }

        private sealed class StubSessionLauncher : IGameSessionLauncher
        {
            public StubSession Session { get; } = new StubSession();

            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult<IGameSession>(Session);
            }
        }

        /// <summary>
        /// 回归：从「本地版本」点「启动游戏」也要把会话交给控制台。
        /// 以前两条启动路径互不相干——主界面走 IGameLauncher，不捕获输出，
        /// 所以本地版本启动的游戏在「启动与日志」里一行日志都看不到。
        /// </summary>
        [Fact]
        public async Task Main_launch_hands_the_session_to_the_console()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            StubSessionLauncher launcher = new StubSessionLauncher();

            MainViewModel vm = TestSupport.CreateMain(
                catalog,
                new AppPaths(@"C:\Temp\roaming"),
                new FixedJavaLocator(TestSupport.CreateJava(21)),
                launcher);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.True(vm.Console.IsRunning, "主界面启动后，控制台应当进入运行状态");
            Assert.Contains("777", vm.Console.StatusText);

            launcher.Session.Emit("[main/INFO] hello from main page");
            Assert.Contains(vm.Console.Lines, line => line.Text.Contains("hello from main page"));
        }
    }
}

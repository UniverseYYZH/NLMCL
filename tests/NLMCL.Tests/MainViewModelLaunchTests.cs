using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.SafeMode;
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

        private sealed class MutableCatalog : IVersionCatalog
        {
            public MutableCatalog(params MinecraftVersion[] items)
            {
                Items = items;
            }

            public IReadOnlyList<MinecraftVersion> Items { get; set; }

            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult(Items);
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

        private sealed class BlockingLauncher : IGameLauncher
        {
            private readonly TaskCompletionSource<LaunchResult> _result =
                new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Entered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Calls { get; private set; }

            public async Task<LaunchResult> LaunchAsync(
                LaunchRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                Entered.TrySetResult(true);
                return await _result.Task.ConfigureAwait(false);
            }

            public void Release() => _result.TrySetResult(new LaunchResult(777, true, "ok"));
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
            IInstanceSettingsStore? instanceSettings = null,
            ModsViewModel? mods = null, IDialogService? dialogs = null,
            IGameSessionLauncher? sessionLauncher = null,
            AppPaths? paths = null,
            ISafeModeService? safeMode = null)
        {
            paths = paths ?? new AppPaths(@"C:\Temp\roaming");
            return new MainViewModel(
                catalog, paths, new DownloadViewModel(new TestSupport.EmptyProvider(), new TestSupport.NoopInstaller(), paths),
                TestSupport.CreateConsole(paths), launcher, javas, new TestSupport.MemoryAccountStore(), new TestSupport.NoopAuthenticator(), instanceSettings ?? new TestSupport.MemoryInstanceSettingsStore(), new TestSupport.FakeNetwork(), new TestSupport.FakeClipboard(), new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings(),
                mods: mods, dialogs: dialogs, sessionLauncher: sessionLauncher, safeMode: safeMode);
        }

        private sealed class ConflictModManager : IModManager
        {
            public Task<IReadOnlyList<ModEntry>> ListAsync(string modsDirectory, CancellationToken cancellationToken)
            {
                IReadOnlyList<ModEntry> mods = new[]
                {
                    new ModEntry("sodium.jar", "sodium.jar", 1) { Name = "Sodium", Enabled = true },
                    new ModEntry("optifine.jar", "optifine.jar", 1) { Name = "OptiFine", Enabled = true }
                };
                return Task.FromResult(mods);
            }

            public Task EnableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DisableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DeleteAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class MissingDependencyModManager : IModManager
        {
            public Task<IReadOnlyList<ModEntry>> ListAsync(string modsDirectory, CancellationToken cancellationToken)
            {
                ModEntry mod = new ModEntry("create.jar", "create.jar", 1) { Name = "Create", Enabled = true };
                mod.MissingDependencies.Add("flywheel");
                IReadOnlyList<ModEntry> mods = new[] { mod };
                return Task.FromResult(mods);
            }

            public Task EnableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DisableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DeleteAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class BlockingInstanceSettingsStore : IInstanceSettingsStore
        {
            private readonly TaskCompletionSource<InstanceSettings> _pending =
                new TaskCompletionSource<InstanceSettings>(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool Block { get; set; }

            public Task<InstanceSettings> LoadAsync(
                string minecraftDirectory, string versionId, CancellationToken cancellationToken)
            {
                return Block
                    ? _pending.Task
                    : Task.FromResult(new InstanceSettings());
            }

            public Task SaveAsync(
                string minecraftDirectory, string versionId, InstanceSettings settings,
                CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public bool Exists(string minecraftDirectory, string versionId) => false;
        }

        private sealed class FailingInstanceSettingsStore : IInstanceSettingsStore
        {
            public Task<InstanceSettings> LoadAsync(
                string minecraftDirectory, string versionId, CancellationToken cancellationToken)
            {
                throw new InvalidDataException("settings.json 损坏");
            }

            public Task SaveAsync(
                string minecraftDirectory, string versionId, InstanceSettings settings,
                CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public bool Exists(string minecraftDirectory, string versionId) => true;
        }

        private sealed class DelayedFailingSettingsStore : IInstanceSettingsStore
        {
            private readonly TaskCompletionSource<InstanceSettings> _first =
                new TaskCompletionSource<InstanceSettings>(TaskCreationOptions.RunContinuationsAsynchronously);

            public string FirstVersionId { get; set; } = "1.21.1";

            public InstanceSettings Second { get; set; } = new InstanceSettings();

            public Task<InstanceSettings> LoadAsync(
                string minecraftDirectory, string versionId, CancellationToken cancellationToken)
            {
                return string.Equals(versionId, FirstVersionId, StringComparison.Ordinal)
                    ? _first.Task
                    : Task.FromResult(Second);
            }

            public Task SaveAsync(
                string minecraftDirectory, string versionId, InstanceSettings settings,
                CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public bool Exists(string minecraftDirectory, string versionId) => true;

            public void FailFirst(Exception error) => _first.TrySetException(error);
        }

        private sealed class CancelDialog : IDialogService
        {
            public bool Asked { get; private set; }

            public string LastTitle { get; private set; } = string.Empty;

            public string LastMessage { get; private set; } = string.Empty;

            public bool Confirm(string title, string message)
            {
                Asked = true;
                LastTitle = title;
                LastMessage = message;
                return false;
            }

            public string? Prompt(string title, string message, string initialValue) => null;
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
        public async Task Restart_is_ignored_while_a_join_is_preparing()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator());
            await vm.RefreshAsync();
            vm.IsJoiningProfile = true;

            await vm.RestartGameAsync();

            Assert.Null(launcher.LastRequest);
            Assert.Contains("正在准备进入服务器", vm.StatusText);
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
        public async Task Launch_with_missing_client_jar_reports_and_resets_state()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null, hasClientJar: false));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(TestSupport.CreateJava(21)));
            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            await vm.LaunchSelectedAsync();

            Assert.Null(launcher.LastRequest);
            Assert.Contains("缺少客户端 JAR", vm.StatusText);
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
            Assert.Equal("legacy", launcher.LastRequest.UserType);
            Assert.Contains("已启动", vm.StatusText);
        }

        [Fact]
        public async Task Launch_warns_about_renderer_conflicts_and_can_cancel()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            CancelDialog dialogs = new CancelDialog();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(),
                mods: new ModsViewModel(new ConflictModManager()), dialogs: dialogs);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.True(dialogs.Asked);
            Assert.Null(launcher.LastRequest);
            Assert.Equal("启动前检查", dialogs.LastTitle);
            Assert.Contains("OptiFine", dialogs.LastMessage);
            Assert.Contains("不兼容", dialogs.LastMessage);
            Assert.Contains("启动前检查", vm.StatusText);
            Assert.False(vm.IsLaunching);
        }

        [Fact]
        public async Task Launch_warns_about_missing_mod_dependencies_and_can_cancel()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            CancelDialog dialogs = new CancelDialog();
            MainViewModel vm = Create(catalog, launcher, new FixedJavaLocator(),
                mods: new ModsViewModel(new MissingDependencyModManager()), dialogs: dialogs);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.True(dialogs.Asked);
            Assert.Null(launcher.LastRequest);
            Assert.Contains("缺少前置", dialogs.LastMessage);
            Assert.Contains("flywheel", dialogs.LastMessage);
            Assert.False(vm.IsLaunching);
        }

        [Fact]
        public async Task Select_version_populates_launch_preflight_warnings()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator(),
                mods: new ModsViewModel(new MissingDependencyModManager()));

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.RefreshLaunchPreflightAsync();

            Assert.True(vm.HasLaunchPreflightWarnings);
            Assert.Contains("flywheel", string.Join("\n", vm.LaunchPreflightWarnings));
            Assert.Contains("发现", vm.LaunchPreflightSummary);
        }

        [Fact]
        public async Task Saving_proxy_settings_refreshes_launch_preflight()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.RefreshLaunchPreflightAsync();
            Assert.False(vm.HasLaunchPreflightWarnings);

            vm.UseProxy = true;
            vm.ProxyHost = "bad host";
            vm.ProxyPort = 0;
            await vm.SaveSettingsAsync();

            Assert.True(vm.HasLaunchPreflightWarnings);
            Assert.Contains("代理设置不完整", string.Join("\n", vm.LaunchPreflightWarnings));
        }

        [Fact]
        public async Task Editing_proxy_updates_launch_preflight_without_saving()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.RefreshLaunchPreflightAsync();
            Assert.False(vm.HasLaunchPreflightWarnings);

            vm.UseProxy = true;
            vm.ProxyHost = "bad host";
            vm.ProxyPort = 0;
            await Task.Delay(700);
            Assert.Contains("代理设置不完整", string.Join("\n", vm.LaunchPreflightWarnings));

            vm.ProxyHost = "127.0.0.1";
            vm.ProxyPort = 7890;
            await Task.Delay(700);
            Assert.False(vm.HasLaunchPreflightWarnings);
            Assert.Contains("未发现", vm.LaunchPreflightSummary);
        }

        [Fact]
        public async Task Settings_load_failure_resets_previous_instance_proxy()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null));
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator(),
                instanceSettings: new FailingInstanceSettingsStore());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.UseProxy = true;
            vm.ProxyHost = "bad host";
            vm.ProxyPort = 0;
            await vm.RefreshLaunchPreflightAsync();
            Assert.True(vm.HasLaunchPreflightWarnings);

            vm.SelectVersion(vm.Versions[1]);
            await vm.LoadInstanceSettingsAsync(vm.Versions[1]);
            await vm.RefreshLaunchPreflightAsync();

            Assert.False(vm.UseProxy);
            Assert.Equal(string.Empty, vm.ProxyHost);
            Assert.Equal(0, vm.ProxyPort);
            Assert.False(vm.HasLaunchPreflightWarnings);
            Assert.Contains("读取实例设置失败", vm.StatusText);
        }

        [Fact]
        public async Task Preparing_join_selection_loads_instance_settings_before_launch()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null));
            TestSupport.MemoryInstanceSettingsStore settings = new TestSupport.MemoryInstanceSettingsStore();
            settings.Items["1.20.1"] = new InstanceSettings
            {
                MemoryMb = 6144,
                UseProxy = true,
                ProxyHost = "127.0.0.1",
                ProxyPort = 7890
            };
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator(),
                instanceSettings: settings);

            await vm.RefreshAsync();
            await vm.SelectVersionForLaunchAsync(vm.Versions[1]);

            Assert.Same(vm.Versions[1], vm.SelectedVersion);
            Assert.True(vm.HasSelection);
            Assert.Equal(6144, vm.MemoryMb);
            Assert.True(vm.UseProxy);
            Assert.Equal(7890, vm.ProxyPort);
            Assert.False(vm.IsLaunchPreflightBusy);
            Assert.Contains("未发现", vm.LaunchPreflightSummary);
        }

        [Fact]
        public async Task Late_settings_failure_of_previous_instance_does_not_reset_new_one()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null));
            DelayedFailingSettingsStore settings = new DelayedFailingSettingsStore
            {
                Second = new InstanceSettings
                {
                    MemoryMb = 4096,
                    UseProxy = true,
                    ProxyHost = "127.0.0.1",
                    ProxyPort = 7890
                }
            };
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator(),
                instanceSettings: settings);

            await vm.RefreshAsync();
            VersionItem first = vm.Versions[0];
            VersionItem second = vm.Versions[1];

            vm.SelectedVersion = first;
            Task firstLoad = vm.LoadInstanceSettingsAsync(first);

            vm.SelectedVersion = second;
            await vm.LoadInstanceSettingsAsync(second);
            Assert.Equal(4096, vm.MemoryMb);
            Assert.True(vm.UseProxy);

            settings.FailFirst(new InvalidDataException("old settings broken"));
            await firstLoad;

            Assert.Equal(4096, vm.MemoryMb);
            Assert.True(vm.UseProxy);
            Assert.Equal("127.0.0.1", vm.ProxyHost);
            Assert.Equal(7890, vm.ProxyPort);
            Assert.DoesNotContain("读取实例设置失败", vm.StatusText);
        }

        [Fact]
        public async Task Selecting_another_instance_clears_previous_preflight_immediately()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null),
                new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, null));
            BlockingInstanceSettingsStore settings = new BlockingInstanceSettingsStore();
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator(),
                instanceSettings: settings);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.UseProxy = true;
            vm.ProxyHost = "bad host";
            vm.ProxyPort = 0;
            await vm.RefreshLaunchPreflightAsync();
            Assert.Contains("代理设置不完整", string.Join("\n", vm.LaunchPreflightWarnings));

            settings.Block = true;
            vm.SelectVersion(vm.Versions[1]);

            Assert.Empty(vm.LaunchPreflightWarnings);
            Assert.Equal("正在检查启动前问题…", vm.LaunchPreflightSummary);
            Assert.True(vm.IsLaunchPreflightBusy);
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

        [Fact]
        public async Task Refresh_with_no_instances_clears_selection_and_preflight()
        {
            MutableCatalog catalog = new MutableCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            MainViewModel vm = Create(catalog, new RecordingLauncher(), new FixedJavaLocator());

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            vm.UseProxy = true;
            vm.ProxyHost = "bad host";
            vm.ProxyPort = 0;
            await vm.RefreshLaunchPreflightAsync();
            Assert.True(vm.HasLaunchPreflightWarnings);

            catalog.Items = Array.Empty<MinecraftVersion>();
            await vm.RefreshAsync();

            Assert.Null(vm.SelectedVersion);
            Assert.False(vm.HasSelection);
            Assert.Empty(vm.LaunchPreflightWarnings);
            Assert.False(vm.IsLaunchPreflightBusy);
            Assert.Contains("先选择一个实例", vm.LaunchPreflightSummary);
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

        [Fact]
        public async Task Safe_mode_disables_mods_and_restores_after_session_exit()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-safe-mode-vm-");
            AppPaths paths = new AppPaths(root);
            string modsDirectory = Path.Combine(
                paths.DefaultMinecraftDirectory, "versions", "1.21.1", "mods");
            Directory.CreateDirectory(modsDirectory);
            string jar = Path.Combine(modsDirectory, "sodium.jar");
            File.WriteAllText(jar, "sodium");

            SafeModeService service = new SafeModeService(Path.Combine(root, "data"));
            StubSessionLauncher sessions = new StubSessionLauncher();
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            RecordingLauncher launcher = new RecordingLauncher();
            MainViewModel vm = Create(
                catalog,
                launcher,
                new FixedJavaLocator(TestSupport.CreateJava(21)),
                sessionLauncher: sessions,
                paths: paths,
                safeMode: service);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LoadInstanceSettingsAsync(vm.Versions[0]);

            await vm.LaunchSafeModeAsync();

            Assert.Null(launcher.LastRequest);
            Assert.False(File.Exists(jar));
            Assert.True(File.Exists(jar + ".disabled"));
            Assert.Contains("已暂时禁用", vm.SafeModeMessage);
            Assert.True(vm.HasSafeModeMessage);
            Assert.Contains("已启动", vm.StatusText);

            sessions.Session.RaiseExit(0);

            for (int i = 0; i < 100; i++)
            {
                if (File.Exists(jar) && vm.SafeModeMessage.Contains("已恢复"))
                {
                    break;
                }

                await Task.Delay(20);
            }

            Assert.True(File.Exists(jar));
            Assert.False(File.Exists(jar + ".disabled"));
            Assert.Contains("已恢复", vm.SafeModeMessage);
            Assert.True(vm.HasSafeModeMessage);
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

            public void RaiseExit(int exitCode)
            {
                IsRunning = false;
                ExitCode = exitCode;
                Exited?.Invoke(this, exitCode);
            }
        }

        private sealed class StubSessionLauncher : IGameSessionLauncher
        {
            public StubSession Session { get; } = new StubSession();

            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult<IGameSession>(Session);
            }
        }

        private sealed class QueueSessionLauncher : IGameSessionLauncher
        {
            private readonly Queue<StubSession> _sessions;

            public QueueSessionLauncher(params StubSession[] sessions)
            {
                _sessions = new Queue<StubSession>(sessions);
            }

            public int Starts { get; private set; }

            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                Starts++;
                return Task.FromResult<IGameSession>(_sessions.Dequeue());
            }
        }

        /// <summary>
        /// 回归：从「本地版本」点「启动游戏」也要把会话交给控制台。
        /// 以前两条启动路径互不相干——主界面走 IGameLauncher，不捕获输出，
        /// 所以本地版本启动的游戏在「启动与日志」里一行日志都看不到。
        /// </summary>
        [Fact]
        public async Task Launch_ignores_a_second_click_while_first_is_starting()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            BlockingLauncher launcher = new BlockingLauncher();
            MainViewModel vm = Create(
                catalog,
                launcher,
                new FixedJavaLocator(TestSupport.CreateJava(21)));

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);

            Task first = vm.LaunchSelectedAsync();
            await launcher.Entered.Task;

            // 第一次启动还卡在 IGameLauncher 里；第二次点击必须直接返回，
            // 不能再次走预检/启动请求。
            Task second = vm.LaunchSelectedAsync();
            await second;

            Assert.Equal(1, launcher.Calls);
            Assert.True(vm.IsLaunching);

            launcher.Release();
            await first;

            Assert.False(vm.IsLaunching);
        }

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

        [Fact]
        public async Task Second_launch_of_same_instance_warns_and_can_cancel()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            StubSession first = new StubSession();
            StubSession second = new StubSession();
            QueueSessionLauncher launcher = new QueueSessionLauncher(first, second);
            CancelDialog dialogs = new CancelDialog();

            MainViewModel vm = TestSupport.CreateMain(
                catalog,
                new AppPaths(@"C:\Temp\roaming"),
                new FixedJavaLocator(TestSupport.CreateJava(21)),
                launcher,
                dialogs: dialogs);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.True(vm.HasRunningSelectedInstance);
            Assert.False(vm.CanEditInstanceSettings);
            Assert.Contains("多开", vm.MultiInstanceWarning);

            await vm.LaunchSelectedAsync();

            Assert.True(dialogs.Asked);
            Assert.Equal("多开提醒", dialogs.LastTitle);
            Assert.Equal(1, launcher.Starts);
            Assert.Contains("已取消多开启动", vm.StatusText);

            first.RaiseExit(0);

            Assert.False(vm.HasRunningSelectedInstance);
            Assert.True(vm.CanEditInstanceSettings);
            Assert.Equal(string.Empty, vm.MultiInstanceWarning);
        }

        [Fact]
        public async Task Session_exit_replaces_the_main_started_status()
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

            Assert.Contains("已启动", vm.StatusText);

            launcher.Session.RaiseExit(1);

            Assert.Contains("退出码 1", vm.StatusText);
            Assert.DoesNotContain("已启动", vm.StatusText);
        }

        [Fact]
        public async Task Older_session_exit_does_not_replace_latest_started_status()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            StubSession first = new StubSession();
            StubSession second = new StubSession();
            QueueSessionLauncher launcher = new QueueSessionLauncher(first, second);

            MainViewModel vm = TestSupport.CreateMain(
                catalog,
                new AppPaths(@"C:\Temp\roaming"),
                new FixedJavaLocator(TestSupport.CreateJava(21)),
                launcher);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();
            await vm.LaunchSelectedAsync();

            Assert.Contains("已启动", vm.StatusText);

            first.RaiseExit(0);

            Assert.Contains("已启动", vm.StatusText);
            Assert.DoesNotContain("游戏已退出", vm.StatusText);

            second.RaiseExit(1);

            Assert.Contains("退出码 1", vm.StatusText);
        }

        [Fact]
        public async Task Latest_session_exit_keeps_older_running_session_status()
        {
            CountingCatalog catalog = new CountingCatalog(
                new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null));
            StubSession first = new StubSession();
            StubSession second = new StubSession();
            QueueSessionLauncher launcher = new QueueSessionLauncher(first, second);

            MainViewModel vm = TestSupport.CreateMain(
                catalog,
                new AppPaths(@"C:\Temp\roaming"),
                new FixedJavaLocator(TestSupport.CreateJava(21)),
                launcher);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();
            await vm.LaunchSelectedAsync();

            second.RaiseExit(0);

            Assert.Contains("还有 1 个游戏在运行", vm.StatusText);
            Assert.DoesNotContain("游戏已退出", vm.StatusText);

            first.RaiseExit(2);

            Assert.Contains("退出码 2", vm.StatusText);
        }
    }
}

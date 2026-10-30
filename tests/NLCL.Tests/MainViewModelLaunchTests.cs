using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.App.ViewModels;
using NLCL.Core;
using Xunit;

namespace NLCL.Tests
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
            IVersionCatalog catalog, IGameLauncher launcher, IJavaLocator javas)
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            return new MainViewModel(
                catalog, paths, new DownloadViewModel(new TestSupport.EmptyProvider(), new TestSupport.NoopInstaller(), paths),
                TestSupport.CreateConsole(paths), launcher, javas, new TestSupport.MemoryAccountStore(), new TestSupport.NoopAuthenticator(), new TestSupport.MemoryInstanceSettingsStore(), new TestSupport.FakeNetwork(), new TestSupport.FakeClipboard(), new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());
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

    }
}

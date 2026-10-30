using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.App.ViewModels;
using NLCL.Core;
using NLCL.Core.Pcl;
using NLCL.Core.Pcl.Vb;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// 端到端启动测试：走**和「启动游戏」按钮完全相同**的路径
    /// （MainViewModel.LaunchSelectedAsync → IGameLauncher → javaw），真的把游戏进程拉起来。
    ///
    /// 由 <c>NLCL_TEST_MC_DIR</c> / <c>NLCL_TEST_VERSION</c> 启用；未设置时跳过。
    /// 跑完会把进程杀掉，不留后台。
    /// </summary>
    public class RealLaunchEndToEndTests
    {
        private static (string Directory, string Version)? Target
        {
            get
            {
                string? dir = Environment.GetEnvironmentVariable("NLCL_TEST_MC_DIR");
                string? version = Environment.GetEnvironmentVariable("NLCL_TEST_VERSION");

                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(version))
                {
                    return null;
                }

                return System.IO.Directory.Exists(Path.Combine(dir!, "versions", version!))
                    ? (dir!, version!)
                    : ((string, string)?)null;
            }
        }

        [Fact]
        public async Task Launching_from_the_view_model_starts_a_real_process()
        {
            (string Directory, string Version)? target = Target;
            if (target == null)
            {
                return;
            }

            (string minecraft, string version) = target.Value;
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");

            RecordingLauncher recorder = new RecordingLauncher(new PclGameLauncher());

            MainViewModel vm = new MainViewModel(
                new PclVersionCatalog(), paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                recorder,
                new PclJavaLocator(),
                new TestSupport.MemoryAccountStore { Current = NLCL.Core.Accounts.OfflineAccountFactory.Create("ZACK") },
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(),
                new TestSupport.FakeAppSettings());

            vm.CurrentGameDirectory = minecraft;
            await vm.RefreshAsync();

            VersionItem? item = null;
            foreach (VersionItem candidate in vm.Versions)
            {
                if (candidate.FolderName == version)
                {
                    item = candidate;
                    break;
                }
            }

            Assert.NotNull(item);
            vm.SelectVersion(item!);

            await vm.LaunchSelectedAsync();

            // 状态栏必须报"已启动"，而不是失败
            Assert.True(vm.StatusText.Contains("已启动"),
                "状态：" + vm.StatusText +
                " | java=" + (recorder.LastRequest?.JavaExecutable ?? "(null)") +
                " | 存在=" + (recorder.LastRequest?.JavaExecutable != null && File.Exists(recorder.LastRequest.JavaExecutable)) +
                " | 版本=" + (recorder.LastRequest?.VersionId ?? "(null)"));

            int processId = recorder.LastResult?.ProcessId ?? 0;
            Assert.True(processId > 0, "没有拿到进程 ID：" + vm.StatusText);

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    // 给它几秒钟：真崩的话会立刻退出
                    await Task.Delay(TimeSpan.FromSeconds(25));
                    process.Refresh();
                    Assert.False(process.HasExited,
                        "进程启动后立刻退出了（退出码 " + (process.HasExited ? process.ExitCode : 0) + "）");
                }
            }
            finally
            {
                try
                {
                    Process.GetProcessById(processId).Kill();
                }
                catch (ArgumentException)
                {
                    // 已经退了
                }
            }
        }

        private sealed class RecordingLauncher : IGameLauncher
        {
            private readonly IGameLauncher _inner;

            public RecordingLauncher(IGameLauncher inner)
            {
                _inner = inner;
            }

            public LaunchResult? LastResult { get; private set; }

            public LaunchRequest? LastRequest { get; private set; }

            public async Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastResult = await _inner.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
                return LastResult;
            }
        }
    }
}

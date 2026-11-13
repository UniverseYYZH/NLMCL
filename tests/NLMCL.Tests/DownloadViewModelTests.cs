using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public class DownloadViewModelTests
    {
        private const string Manifest = @"{
            ""latest"": { ""release"": ""1.21.1"" },
            ""versions"": [
                { ""id"": ""1.20.1"", ""type"": ""release"", ""releaseTime"": ""2023-06-12T12:00:00+00:00"" },
                { ""id"": ""24w44a"", ""type"": ""snapshot"", ""releaseTime"": ""2024-10-30T10:00:00+00:00"" },
                { ""id"": ""1.21.1"", ""type"": ""release"", ""releaseTime"": ""2024-08-08T14:00:00+00:00"" }
            ]
        }";

        private sealed class FakeDownload : DownloadServiceStub
        {
            private readonly string _manifest;

            public FakeDownload(string manifest)
            {
                _manifest = manifest;
            }

            public override Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("本测试不应下载文件");
            }

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                return Task.FromResult(_manifest);
            }
        }

        private sealed class FakeInstaller : IVersionInstaller
        {
            private readonly Exception? _failure;

            public FakeInstaller(Exception? failure = null)
            {
                _failure = failure;
            }

            public int Calls { get; private set; }

            public InstallRequest? LastRequest { get; private set; }

            public Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress,
                CancellationToken cancellationToken)
            {
                Calls++;
                LastRequest = request;

                if (_failure != null)
                {
                    throw _failure;
                }

                progress?.Report(new InstallProgress("下载库与客户端 1/3", 20, 0));
                progress?.Report(new InstallProgress("下载游戏资源 2/3", 60, 0));
                progress?.Report(new InstallProgress("安装完成", 100, 0));
                return Task.CompletedTask;
            }
        }

        private sealed class BlockingInstaller : IVersionInstaller
        {
            private int _calls;

            public TaskCompletionSource<bool> Started { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Calls => Volatile.Read(ref _calls);

            public async Task InstallAsync(InstallRequest request, IProgress<InstallProgress> progress,
                CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref _calls);

                if (call == 1)
                {
                    Started.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new InstallProgress("安装完成", 100, 0));
            }
        }

        private sealed class MemoryJobStore : IDownloadJobStore
        {
            public DownloadJob? Job { get; set; }
            public int Saves { get; private set; }
            public int Clears { get; private set; }

            public Task<DownloadJob?> LoadAsync(CancellationToken cancellationToken)
                => Task.FromResult(Job);

            public Task SaveAsync(DownloadJob job, CancellationToken cancellationToken)
            {
                Saves++;
                Job = job;
                return Task.CompletedTask;
            }

            public Task ClearAsync(CancellationToken cancellationToken)
            {
                Clears++;
                Job = null;
                return Task.CompletedTask;
            }
        }

        private static DownloadViewModel Create(
            IVersionInstaller installer, IDownloadJobStore? jobs = null, IAppPaths? paths = null)
        {
            return new DownloadViewModel(
                new MojangVersionListProvider(new FakeDownload(Manifest)),
                installer,
                paths ?? new AppPaths(@"C:\Temp\roaming"),
                jobs: jobs);
        }

        [Fact]
        public void Pick_target_directory_updates_and_raises_event()
        {
            TestSupport.FakeFolderPicker picker = new TestSupport.FakeFolderPicker
            {
                Next = @"D:\Games\minecraft"
            };

            DownloadViewModel vm = new DownloadViewModel(
                new MojangVersionListProvider(new FakeDownload(Manifest)),
                new FakeInstaller(),
                new AppPaths(@"C:\Temp\roaming"),
                folderPicker: picker);

            string? picked = null;
            vm.TargetDirectoryPicked += (_, path) => picked = path;

            vm.PickTargetDirectory();

            Assert.Equal(@"D:\Games\minecraft", vm.TargetDirectory);
            Assert.Equal(@"D:\Games\minecraft", picked);
            Assert.Contains("版本将保存到", vm.StatusText);
        }

        [Fact]
        public async Task Load_orders_versions_by_release_time_and_selects_newest()
        {
            DownloadViewModel vm = Create(new FakeInstaller());

            await vm.LoadVersionsAsync();

            Assert.Equal(3, vm.Versions.Count);
            Assert.Equal("24w44a", vm.Versions[0].Id);   // 最新的快照
            Assert.Equal("1.21.1", vm.Versions[1].Id);
            Assert.Equal("1.20.1", vm.Versions[2].Id);

            // 默认选第一个正式版（快照只是列表里的第一项，不该被默认安装）
            Assert.NotNull(vm.SelectedVersion);
            Assert.Equal("1.21.1", vm.SelectedVersion!.Id);
            Assert.False(vm.IsLoading);
            Assert.Contains("3", vm.StatusText);
        }

        [Fact]
        public async Task Install_without_selection_reports_and_does_nothing()
        {
            FakeInstaller installer = new FakeInstaller();
            DownloadViewModel vm = Create(installer);

            await vm.InstallAsync();

            Assert.Equal(0, installer.Calls);
            Assert.Equal("请先选择一个版本", vm.StatusText);
        }

        [Fact]
        public async Task Install_reports_progress_and_finishes_at_100()
        {
            FakeInstaller installer = new FakeInstaller();
            DownloadViewModel vm = Create(installer);
            await vm.LoadVersionsAsync();
            vm.SelectedVersion = vm.Versions.Single(v => v.Id == "1.21.1");

            await vm.InstallAsync();

            Assert.Equal(1, installer.Calls);
            Assert.Equal("1.21.1", installer.LastRequest!.VersionId);
            Assert.Equal(@"C:\Temp\roaming\.minecraft", installer.LastRequest.MinecraftDirectory);

            Assert.True(vm.HasProgress);
            Assert.Equal(100, vm.Progress);
            Assert.False(vm.IsBusy);
            Assert.Contains("已安装", vm.StatusText);
        }

        [Fact]
        public async Task Install_failure_is_reported_and_busy_flag_clears()
        {
            FakeInstaller installer = new FakeInstaller(new InvalidDataException("文件校验失败"));
            DownloadViewModel vm = Create(installer);
            await vm.LoadVersionsAsync();

            await vm.InstallAsync();

            Assert.False(vm.IsBusy);
            Assert.StartsWith("安装失败：", vm.StatusText);
            Assert.Contains("文件校验失败", vm.StatusText);
        }

        [Fact]
        public async Task Fabric_selection_passes_loader_into_request()
        {
            FakeInstaller installer = new FakeInstaller();
            DownloadViewModel vm = Create(installer);
            await vm.LoadVersionsAsync();
            vm.SelectedVersion = vm.Versions.Single(v => v.Id == "1.21.1");

            Assert.False(vm.IsFabricSelected);
            vm.SelectedLoader = "Fabric";
            Assert.True(vm.IsFabricSelected);
            vm.LoaderVersion = "0.16.9";

            await vm.InstallAsync();

            Assert.Equal("fabric", installer.LastRequest!.Loader);
            Assert.Equal("0.16.9", installer.LastRequest.LoaderVersion);

            // 切回原版后不应再带加载器
            vm.SelectedLoader = "原版";
            await vm.InstallAsync();
            Assert.Null(installer.LastRequest!.Loader);
        }

        [Fact]
        public async Task Failed_install_keeps_a_resumable_job_and_resume_runs_it_again()
        {
            MemoryJobStore jobs = new MemoryJobStore();
            FakeInstaller failing = new FakeInstaller(new InvalidDataException("half"));
            DownloadViewModel vm = Create(failing, jobs);
            await vm.LoadVersionsAsync();
            vm.SelectedVersion = vm.Versions.Single(v => v.Id == "1.21.1");

            await vm.InstallAsync();

            Assert.True(vm.HasPendingJob);
            Assert.NotNull(jobs.Job);
            Assert.Equal("1.21.1", jobs.Job!.VersionId);

            FakeInstaller ok = new FakeInstaller();
            DownloadViewModel resumed = Create(ok, jobs);
            await resumed.LoadPendingJobAsync();
            Assert.True(resumed.HasPendingJob);

            await resumed.ResumePendingJobCommand.ExecuteAsync(null);

            Assert.Equal(1, ok.Calls);
            Assert.Equal("1.21.1", ok.LastRequest!.VersionId);
            Assert.False(resumed.HasPendingJob);
            Assert.Null(jobs.Job);
        }

        [Fact]
        public async Task Install_success_clears_the_pending_job()
        {
            MemoryJobStore jobs = new MemoryJobStore();
            DownloadViewModel vm = Create(new FakeInstaller(), jobs);
            await vm.LoadVersionsAsync();
            vm.SelectedVersion = vm.Versions.Single(v => v.Id == "1.21.1");

            await vm.InstallAsync();

            Assert.False(vm.HasPendingJob);
            Assert.Null(jobs.Job);
            Assert.True(jobs.Clears > 0);
        }

        [Fact]
        public async Task Pausing_keeps_the_resumable_job_and_resume_finishes()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-pause-");
            AppPaths paths = new AppPaths(root);
            string parts = Path.Combine(paths.DefaultMinecraftDirectory, "client.jar.parts");
            Directory.CreateDirectory(parts);
            File.WriteAllText(Path.Combine(parts, "0"), "partial");

            MemoryJobStore jobs = new MemoryJobStore();
            BlockingInstaller installer = new BlockingInstaller();
            DownloadViewModel vm = Create(installer, jobs, paths);
            await vm.LoadVersionsAsync();
            vm.SelectedVersion = vm.Versions.Single(v => v.Id == "1.21.1");

            Task install = vm.InstallAsync();
            Task finished = await Task.WhenAny(installer.Started.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(installer.Started.Task, finished);
            Assert.True(vm.CanPause);

            vm.PauseInstallCommand.Execute(null);
            await install;

            Assert.True(vm.IsPaused);
            Assert.False(vm.IsBusy);
            Assert.True(vm.CanResume);
            Assert.True(Directory.Exists(parts));
            Assert.True(vm.HasPendingJob);
            Assert.NotNull(jobs.Job);
            Assert.True(jobs.Job!.Paused);
            Assert.Contains("已暂停", vm.StatusText);

            await vm.ResumeInstallCommand.ExecuteAsync(null);

            Assert.Equal(2, installer.Calls);
            Assert.False(vm.IsPaused);
            Assert.Equal(100, vm.Progress);
            Assert.False(vm.HasPendingJob);
            Assert.Contains("已安装", vm.StatusText);
        }

        [Fact]
        public async Task Loading_a_paused_job_reports_it_and_resumes()
        {
            MemoryJobStore jobs = new MemoryJobStore
            {
                Job = new DownloadJob
                {
                    VersionId = "1.21.1",
                    Loader = "原版",
                    TargetDirectory = @"C:\Temp\roaming\.minecraft",
                    Paused = true
                }
            };

            FakeInstaller installer = new FakeInstaller();
            DownloadViewModel vm = Create(installer, jobs);
            await vm.LoadPendingJobAsync();

            Assert.True(vm.IsPaused);
            Assert.True(vm.HasPendingJob);
            Assert.Contains("已暂停", vm.PendingJobText);
            Assert.False(vm.CanStartInstall);

            await vm.ResumePendingJobCommand.ExecuteAsync(null);

            Assert.Equal(1, installer.Calls);
            Assert.False(vm.IsPaused);
            Assert.False(vm.HasPendingJob);
            Assert.Contains("已安装", vm.StatusText);
        }

        [Fact]
        public async Task Cancelling_a_paused_install_removes_partial_files_and_job()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-cancel-");
            AppPaths paths = new AppPaths(root);
            string parts = Path.Combine(paths.DefaultMinecraftDirectory, "client.jar.parts");
            Directory.CreateDirectory(parts);
            File.WriteAllText(Path.Combine(parts, "0"), "partial");
            string temporary = Path.Combine(paths.DefaultMinecraftDirectory, "client.jar.nlmcl-tmp");
            File.WriteAllText(temporary, "temporary");

            MemoryJobStore jobs = new MemoryJobStore();
            BlockingInstaller installer = new BlockingInstaller();
            DownloadViewModel vm = Create(installer, jobs, paths);
            await vm.LoadVersionsAsync();
            vm.SelectedVersion = vm.Versions.Single(v => v.Id == "1.21.1");

            Task install = vm.InstallAsync();
            Task finished = await Task.WhenAny(installer.Started.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(installer.Started.Task, finished);

            vm.PauseInstallCommand.Execute(null);
            await install;
            Assert.True(vm.IsPaused);

            vm.CancelInstallCommand.Execute(null);
            await Task.Delay(300);

            Assert.False(vm.IsPaused);
            Assert.False(vm.HasPendingJob);
            Assert.False(Directory.Exists(parts));
            Assert.False(File.Exists(temporary));
            Assert.Null(jobs.Job);
            Assert.Contains("已取消安装", vm.StatusText);
        }

        [Fact]
        public void Version_type_labels_match_the_manifest_vocabulary()
        {
            Assert.Equal("正式版", new VanillaVersion("1.21.1", "release", null).TypeLabel);
            Assert.Equal("快照", new VanillaVersion("24w44a", "snapshot", null).TypeLabel);
            Assert.Equal("Beta", new VanillaVersion("b1.7.3", "old_beta", null).TypeLabel);
        }
    }
}

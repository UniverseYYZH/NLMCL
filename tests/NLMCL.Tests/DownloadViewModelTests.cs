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

        private static DownloadViewModel Create(IVersionInstaller installer)
        {
            return new DownloadViewModel(
                new MojangVersionListProvider(new FakeDownload(Manifest)),
                installer,
                new AppPaths(@"C:\Temp\roaming"));
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
        public void Version_type_labels_match_the_manifest_vocabulary()
        {
            Assert.Equal("正式版", new VanillaVersion("1.21.1", "release", null).TypeLabel);
            Assert.Equal("快照", new VanillaVersion("24w44a", "snapshot", null).TypeLabel);
            Assert.Equal("Beta", new VanillaVersion("b1.7.3", "old_beta", null).TypeLabel);
        }
    }
}

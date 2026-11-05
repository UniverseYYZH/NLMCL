using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class CancelDownloadTests
    {
        [Fact]
        public void Cancel_command_exists_so_users_can_back_out()
        {
            DownloadViewModel vm = Create();

            Assert.NotNull(vm.CancelInstallCommand);
            Assert.False(vm.CanCancel);
        }

        [Fact]
        public void Cancelling_before_any_download_is_harmless()
        {
            DownloadViewModel vm = Create();

            vm.CancelInstallCommand.Execute(null);

            Assert.False(vm.IsBusy);
            Assert.False(vm.CanCancel);
        }

        private static DownloadViewModel Create()
        {
            AppPaths paths = new AppPaths(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nlmcl-cancel-" + Guid.NewGuid().ToString("N")));

            return new DownloadViewModel(
                new TestSupport.EmptyProvider(),
                new NoopInstaller(),
                paths);
        }

        private sealed class NoopInstaller : IVersionInstaller
        {
            public Task InstallAsync(
                InstallRequest request, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }
    }
}
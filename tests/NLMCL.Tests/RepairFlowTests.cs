using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>版本修复流程：避免检查与修复各算一遍 SHA1。</summary>
    public class RepairFlowTests
    {
        private sealed class SingleVersionCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion("Test", "Test", VersionKind.Release, null, hasClientJar: true)
                });
        }

        private sealed class CountingRepairer : IVersionRepairer
        {
            public int InspectCalls { get; private set; }

            public int RepairCalls { get; private set; }

            public Task<RepairReport> InspectAsync(
                string minecraftDirectory, string instanceFolder, CancellationToken cancellationToken)
            {
                InspectCalls++;
                return Task.FromResult(new RepairReport(0, 0, 0, Array.Empty<string>()));
            }

            public Task<RepairReport> RepairAsync(
                string minecraftDirectory, string instanceFolder,
                IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
            {
                RepairCalls++;
                return Task.FromResult(new RepairReport(0, 0, 0, Array.Empty<string>()));
            }
        }

        [Fact]
        public async Task Repairing_a_healthy_instance_uses_one_integrity_pass()
        {
            CountingRepairer repairer = new CountingRepairer();
            MainViewModel vm = TestSupport.CreateMain(
                new SingleVersionCatalog(),
                new AppPaths(@"C:\Temp\roaming"),
                repairer: repairer);

            await vm.RefreshAsync();
            vm.SelectedVersion = vm.Versions[0];

            await vm.RepairSelectedAsync();

            Assert.Equal(0, repairer.InspectCalls);
            Assert.Equal(1, repairer.RepairCalls);
            Assert.Contains("无需修复", vm.StatusText);
            Assert.False(vm.IsBusy);
        }
    }
}

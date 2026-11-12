using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class RendererModDiagnosticsTests
    {
        [Fact]
        public void Detects_renderer_mods_and_warns_about_optifine_conflict()
        {
            ModsViewModel vm = new ModsViewModel(new EmptyModManager());
            vm.Mods.Add(new ModEntry("sodium.jar", "sodium.jar", 1)
            {
                Name = "Sodium",
                Version = "0.6.0",
                Enabled = true
            });
            vm.Mods.Add(new ModEntry("OptiFine_1.21.1.jar", "OptiFine_1.21.1.jar", 1)
            {
                Name = "OptiFine",
                Version = "HD U I6",
                Enabled = true
            });

            vm.RefreshRendererDiagnostics();

            Assert.True(vm.HasRendererMods);
            Assert.True(vm.HasRendererWarning);
            Assert.Contains("Sodium", vm.RendererSummary);
            Assert.Contains("OptiFine", vm.RendererSummary);
            Assert.Contains("不兼容", vm.RendererWarning);
        }

        [Fact]
        public void Disabled_renderer_does_not_trigger_a_conflict()
        {
            ModsViewModel vm = new ModsViewModel(new EmptyModManager());
            vm.Mods.Add(new ModEntry("sodium.jar", "sodium.jar", 1)
            {
                Name = "Sodium",
                Enabled = true
            });
            vm.Mods.Add(new ModEntry("optifine.jar", "optifine.jar", 1)
            {
                Name = "OptiFine",
                Enabled = false
            });

            vm.RefreshRendererDiagnostics();

            Assert.True(vm.HasRendererMods);
            Assert.False(vm.HasRendererWarning);
            Assert.Contains("已禁用", vm.RendererSummary);
        }

        [Fact]
        public void Iris_without_sodium_reports_the_missing_prerequisite()
        {
            ModsViewModel vm = new ModsViewModel(new EmptyModManager());
            vm.Mods.Add(new ModEntry("iris.jar", "iris.jar", 1)
            {
                Name = "Iris",
                Enabled = true
            });

            vm.RefreshRendererDiagnostics();

            Assert.Contains("Iris", vm.RendererSummary);
            Assert.Contains("Sodium", vm.RendererWarning);
        }

        private sealed class EmptyModManager : IModManager
        {
            public Task<IReadOnlyList<ModEntry>> ListAsync(string modsDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModEntry>>(Array.Empty<ModEntry>());

            public Task EnableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DisableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DeleteAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class JavaManagementTests
    {
        private static JavaRuntime MakeJava(string home, int major)
            => new JavaRuntime(home, new Version(major, 0));

        [Fact]
        public void Default_java_wins_over_instance_and_auto()
        {
            JavaRuntime[] javas = { MakeJava(@"C:\Java\21\", 21), MakeJava(@"C:\Java\17\", 17) };

            // 默认 Java 存在 → 用它（即使实例也有一个）
            string existing = Path.GetTempFileName();
            string? picked = MainViewModel.PickConfiguredJava(existing, null, javas, 17);

            Assert.Equal(existing, picked);
        }

        [Fact]
        public void Missing_default_java_falls_back_to_auto_pick()
        {
            string home = Path.Combine(Path.GetTempPath(), "nlmcl-fakejava-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(home, "bin"));
            File.WriteAllText(Path.Combine(home, "bin", "javaw.exe"), string.Empty);
            JavaRuntime[] javas = { new JavaRuntime(home, new Version(21, 0)) };

            string? picked = MainViewModel.PickConfiguredJava(
                @"C:\not-exists\javaw.exe", null, javas, 21);

            Assert.Equal(javas[0].ExecutablePath, picked);
        }
        [Fact]
        public async Task Default_java_path_persists_in_settings()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-java-" + Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var store = new JsonAppSettingsStore(paths);

            AppSettings settings = await store.LoadAsync(CancellationToken.None);
            settings.DefaultJavaPath = @"C:\Java\21\bin\javaw.exe";
            await store.SaveAsync(settings, CancellationToken.None);

            AppSettings reloaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(@"C:\Java\21\bin\javaw.exe", reloaded.DefaultJavaPath);
        }

        [Fact]
        public async Task Refreshing_java_list_puts_auto_first()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-java-" + Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var catalog = new TestSupport.EmptyCatalog();
            var locator = new FixedJavaLocator(
                MakeJava(@"C:\Java\17\", 17), MakeJava(@"C:\Java\21\", 21));

            MainViewModel vm = TestSupport.CreateMain(catalog, paths, locator);

            await vm.RefreshJavaAsync();

            Assert.True(vm.JavaOptions.Count >= 3);
            Assert.True(vm.JavaOptions[0].IsAuto);
            Assert.Equal("自动（按版本要求）", vm.JavaOptions[0].Title);
            Assert.Equal(21, vm.JavaOptions[1].Title.Contains("21") ? 21 : 0);
            Assert.Contains("探测到 2 个 Java", vm.JavaStatus);
        }

        private sealed class FixedJavaLocator : IJavaLocator
        {
            private readonly IReadOnlyList<JavaRuntime> _javas;

            public FixedJavaLocator(params JavaRuntime[] javas)
            {
                _javas = javas;
            }

            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
                => Task.FromResult(_javas);
        }

    }
}

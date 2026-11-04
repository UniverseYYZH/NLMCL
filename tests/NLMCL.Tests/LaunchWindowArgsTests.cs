using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>实例设置里的窗口参数：存储往返 + 生成游戏参数。</summary>
    public class LaunchWindowArgsTests
    {
        private sealed class EmptyCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new List<MinecraftVersion>());
        }

        [Fact]
        public void Builds_width_height_fullscreen_and_extra_args()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-win-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var vm = TestSupport.CreateMain(new EmptyCatalog(), new AppPaths(root));

                vm.WindowWidth = 1280;
                vm.WindowHeight = 720;
                vm.Fullscreen = true;
                vm.ExtraArguments = "-XX:+UseG1GC  --demo";

                IReadOnlyList<string> args = vm.BuildExtraGameArguments();

                // 窗口尺寸/全屏不再走命令行（会与 PCL 自带的 --width 重复而互相抵消）
                Assert.DoesNotContain("--width", args);
                Assert.DoesNotContain("--fullscreen", args);
                Assert.Contains("-XX:+UseG1GC", args);
                Assert.Contains("--demo", args);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Zero_size_means_game_default()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-win-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var vm = TestSupport.CreateMain(new EmptyCatalog(), new AppPaths(root));

                vm.WindowWidth = 0;
                vm.WindowHeight = 0;
                vm.Fullscreen = false;
                vm.ExtraArguments = string.Empty;

                Assert.Empty(vm.BuildExtraGameArguments());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Settings_store_round_trips_window_fields()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-win-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                // JsonInstanceSettingsStore 无参构造（设置写在版本目录里）
                JsonInstanceSettingsStore store = new JsonInstanceSettingsStore();

                await store.SaveAsync(
                    root,
                    "1.20.1",
                    new InstanceSettings
                    {
                        MemoryMb = 4096,
                        WindowWidth = 1600,
                        WindowHeight = 900,
                        Fullscreen = true
                    },
                    CancellationToken.None);

                InstanceSettings loaded = await store.LoadAsync(root, "1.20.1", CancellationToken.None);

                Assert.Equal(1600, loaded.WindowWidth);
                Assert.Equal(900, loaded.WindowHeight);
                Assert.True(loaded.Fullscreen);
                Assert.Equal(4096, loaded.MemoryMb);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

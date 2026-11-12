using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>进模组服时，AutoModpack 不可用必须中止，不能“装不上也照常启动”。</summary>
    public class JoinProfileTests
    {
        private sealed class FixedCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion(
                        "fabric-loader-0.15.0-1.21.1",
                        "fabric-loader-0.15.0-1.21.1",
                        VersionKind.Release,
                        null)
                });
        }

        private sealed class FixedResolver : IServerAddressResolver
        {
            public Task<ServerEndpoint> ResolveAsync(
                string host, int port, CancellationToken cancellationToken)
                => Task.FromResult(new ServerEndpoint(host, port, false));
        }

        private sealed class BlockingResolver : IServerAddressResolver
        {
            private readonly TaskCompletionSource<ServerEndpoint> _endpoint =
                new TaskCompletionSource<ServerEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Entered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Calls { get; private set; }

            public async Task<ServerEndpoint> ResolveAsync(
                string host, int port, CancellationToken cancellationToken)
            {
                Calls++;
                Entered.TrySetResult(true);
                return await _endpoint.Task.ConfigureAwait(false);
            }

            public void Release(string host, int port)
                => _endpoint.TrySetResult(new ServerEndpoint(host, port, false));
        }

        private sealed class CountingResolver : IServerAddressResolver
        {
            public int Calls { get; private set; }

            public Task<ServerEndpoint> ResolveAsync(
                string host, int port, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new ServerEndpoint(host, port, false));
            }
        }

        private sealed class OnlinePinger : IServerPinger
        {
            public Task<LanRoom> PingAsync(
                string host, int port, CancellationToken cancellationToken)
                => Task.FromResult(new LanRoom(host, port)
                {
                    Pinged = true,
                    Online = 1,
                    Max = 20,
                    VersionName = "1.21.1",
                    LatencyMs = 5
                });
        }

        private sealed class EmptyResourceCatalog : IResourceCatalog
        {
            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceSummary>>(Array.Empty<ResourceSummary>());

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(Array.Empty<ResourceFile>());
        }

        private sealed class MismatchedAutoModpackCatalog : IResourceCatalog
        {
            public Task<IReadOnlyList<ResourceSummary>> SearchAsync(
                ResourceQuery query, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceSummary>>(Array.Empty<ResourceSummary>());

            public Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
                string projectId, ResourceKind kind, string? gameVersion, string? loader,
                CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ResourceFile>>(new[]
                {
                    new ResourceFile(
                        "automodpack-5.0.0.jar",
                        "https://example.com/automodpack-5.0.0.jar",
                        1,
                        "5.0.0",
                        new[] { "1.21.1" },
                        "fabric")
                });
        }

        private sealed class UnusedDownloader : IFileDownloader
        {
            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
                => throw new InvalidOperationException("不应下载");

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
                => throw new InvalidOperationException("不应下载");

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => throw new InvalidOperationException("不应请求");
        }

        private sealed class CountingLauncher : IGameLauncher
        {
            public int Calls { get; private set; }

            public Task<LaunchResult> LaunchAsync(
                LaunchRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new LaunchResult(1, true, "ok"));
            }
        }

        [Fact]
        public void Stale_automodpack_jars_are_removed_when_target_already_exists()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-stale-");
            try
            {
                string mods = Path.Combine(root, "mods");
                Directory.CreateDirectory(mods);
                string target = Path.Combine(mods, "automodpack-4.0.7.jar");
                string stale = Path.Combine(mods, "automodpack-5.0.0.jar");
                string unrelated = Path.Combine(mods, "sodium.jar");
                File.WriteAllText(target, "target");
                File.WriteAllText(stale, "stale");
                File.WriteAllText(unrelated, "keep");

                MainViewModel.DeleteStaleAutoModpackJars(mods, Path.GetFileName(target));

                Assert.True(File.Exists(target));
                Assert.False(File.Exists(stale));
                Assert.True(File.Exists(unrelated));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Lobby_join_stops_if_a_game_starts_while_picking()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-lobby-launch-race-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                BlockingResolver resolver = new BlockingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker);

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;
                await vm.RefreshAsync();
                vm.SelectedVersion = vm.Versions[0];

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                Task lobby = vm.JoinProfileLobbyAsync(profile);
                await resolver.Entered.Task;

                vm.IsLaunching = true;
                resolver.Release("example.com", 25565);
                await lobby;

                Assert.Contains("已停止进入大厅", vm.StatusText);
                Assert.Equal(0, launcher.Calls);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_stops_if_a_game_starts_while_picking()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-modded-launch-race-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                BlockingResolver resolver = new BlockingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new EmptyResourceCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                Task join = vm.JoinProfileModdedAsync(profile);
                await resolver.Entered.Task;

                vm.IsLaunching = true;
                resolver.Release("example.com", 25565);
                await join;

                Assert.Contains("已停止进服准备", vm.StatusText);
                Assert.Equal(0, launcher.Calls);
                Assert.False(vm.IsJoiningProfile);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Launch_is_ignored_while_a_join_is_preparing()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-launch-join-guard-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                BlockingResolver resolver = new BlockingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new EmptyResourceCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                Task join = vm.JoinProfileModdedAsync(profile);
                await resolver.Entered.Task;
                Assert.True(vm.IsJoiningProfile);

                await vm.LaunchSelectedAsync();

                Assert.Contains("正在准备进入服务器", vm.StatusText);
                Assert.Equal(0, launcher.Calls);

                resolver.Release("example.com", 25565);
                await join;
                Assert.False(vm.IsJoiningProfile);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Joins_are_ignored_while_a_game_is_launching()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-launching-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                CountingResolver resolver = new CountingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new EmptyResourceCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;
                vm.IsLaunching = true;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                await vm.JoinProfileModdedAsync(profile);
                Assert.Contains("游戏正在启动", vm.StatusText);

                await vm.JoinProfileLobbyAsync(profile);
                Assert.Contains("游戏正在启动", vm.StatusText);
                Assert.Equal(0, resolver.Calls);
                Assert.Equal(0, launcher.Calls);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Lobby_join_ignores_a_second_click_while_preparing()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-lobby-guard-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                BlockingResolver resolver = new BlockingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    profilePicker: picker);

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;
                await vm.RefreshAsync();
                vm.SelectedVersion = vm.Versions[0];

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                Task first = vm.JoinProfileLobbyAsync(profile);
                await resolver.Entered.Task;

                Task second = vm.JoinProfileLobbyAsync(profile);
                await second;

                Assert.Contains("正在进入大厅", vm.StatusText);
                Assert.Equal(1, resolver.Calls);

                resolver.Release("example.com", 25565);
                await first;
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Lobby_join_ignores_click_while_modded_join_is_preparing()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-lobby-guard-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                BlockingResolver resolver = new BlockingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    profilePicker: picker,
                    resourceCatalog: new EmptyResourceCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                Task modded = vm.JoinProfileModdedAsync(profile);
                await resolver.Entered.Task;

                await vm.JoinProfileLobbyAsync(profile);

                Assert.Contains("正在进服", vm.StatusText);
                Assert.Equal(1, resolver.Calls);

                resolver.Release("example.com", 25565);
                await modded;
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_aborts_when_automodpack_services_are_missing()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-no-services-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker);

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                await vm.JoinProfileModdedAsync(profile);

                Assert.Contains("AutoModpack 下载服务未就绪", vm.StatusText);
                Assert.Equal(0, launcher.Calls);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_aborts_when_no_automodpack_is_available()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new EmptyResourceCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                await vm.JoinProfileModdedAsync(profile);

                Assert.Contains("没找到适配", vm.StatusText);
                Assert.Contains("已停止启动", vm.StatusText);
                Assert.Equal(0, launcher.Calls);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_reports_the_pinned_automodpack_version_when_missing()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-version-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MismatchedAutoModpackCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                await vm.JoinProfileModdedAsync(profile);

                Assert.Contains("4.0.6", vm.StatusText);
                Assert.Contains("5.0.0", vm.StatusText);
                Assert.Contains("已停止启动", vm.StatusText);
                Assert.Equal(0, launcher.Calls);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_ignores_a_second_click_while_preparing()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-guard-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                BlockingResolver resolver = new BlockingResolver();
                ServerProfilePicker picker = new ServerProfilePicker(
                    resolver, new OnlinePinger(), TimeSpan.FromSeconds(1));
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    profilePicker: picker,
                    resourceCatalog: new EmptyResourceCatalog(),
                    fileDownloader: new UnusedDownloader());

                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Mode = ServerProfileMode.Modded,
                    Loader = "fabric",
                    LoaderVersion = "0.15.0",
                    AutoModpackVersion = "4.0.6"
                };
                profile.Addresses.Add("example.com");

                Task first = vm.JoinProfileModdedAsync(profile);
                await resolver.Entered.Task;

                Assert.True(vm.IsJoiningProfile);

                Task second = vm.JoinProfileModdedAsync(profile);
                await second;

                Assert.Contains("正在进服", vm.StatusText);
                Assert.Equal(1, resolver.Calls);

                resolver.Release("example.com", 25565);
                await first;

                Assert.False(vm.IsJoiningProfile);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

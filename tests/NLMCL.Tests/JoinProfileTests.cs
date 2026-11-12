using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

        private sealed class MatchingAutoModpackCatalog : IResourceCatalog
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
                        "automodpack-4.0.6.jar",
                        "https://example.com/automodpack-4.0.6.jar",
                        "automodpack".Length,
                        "4.0.6",
                        new[] { "1.21.1" },
                        "fabric")
                });
        }

        private sealed class ProgressRecordingDownloader : IFileDownloader
        {
            public double? LastProgress { get; private set; }

            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
                => throw new InvalidOperationException("应使用带进度的重载");

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                LastProgress = 0.42;
                progress?.Report(0.42);
                throw new InvalidOperationException("测试中止下载");
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => throw new InvalidOperationException("不应请求");
        }

        private sealed class CountingLauncher : IGameLauncher
        {
            public int Calls { get; private set; }

            public LaunchRequest? LastRequest { get; private set; }

            public Task<LaunchResult> LaunchAsync(
                LaunchRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                LastRequest = request;
                return Task.FromResult(new LaunchResult(1, true, "ok"));
            }
        }

        private sealed class FixedGameDirectoryResolver : IInstancePathResolver
        {
            private readonly string _gameDirectory;

            public FixedGameDirectoryResolver(string gameDirectory)
            {
                _gameDirectory = gameDirectory;
            }

            public string GetGameDirectory(string minecraftDirectory, string versionId)
                => _gameDirectory;
        }

        private sealed class FixedJavaLocator : IJavaLocator
        {
            private readonly string _path;

            public FixedJavaLocator(string path)
            {
                _path = path;
            }

            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<JavaRuntime>>(new[]
                {
                    new JavaRuntime(_path, new Version(21, 0))
                });
        }

        private sealed class WritingAutoModpackDownloader : IFileDownloader
        {
            public int Calls { get; private set; }

            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
                => DownloadAsync(url, targetPath, null, cancellationToken);

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                Calls++;
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "automodpack");
                progress?.Report(1);
                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }

        private sealed class FailingAutoModpackDownloader : IFileDownloader
        {
            public Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
                => DownloadAsync(url, targetPath, null, cancellationToken);

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "partial");
                throw new IOException("网络中断");
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);
        }

        [Fact]
        public void Join_check_reports_a_mismatched_automodpack_version()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-version-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.20.1-1.21.1");
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                File.WriteAllText(Path.Combine(mods, "automodpack-5.0.0.jar"), "wrong-version");

                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = null;
                foreach (ServerJoinCheck item in vm.JoinChecks)
                {
                    if (item.Label == "模组")
                    {
                        check = item;
                        break;
                    }
                }

                Assert.NotNull(check);
                Assert.False(check!.IsOk);
                Assert.Contains("版本不匹配", check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Join_check_reports_a_missing_client_jar()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-client-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.20.1-1.21.1");
                Directory.CreateDirectory(instance);

                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    LoaderVersion = "0.20.1",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(check);
                Assert.False(check!.IsOk);
                Assert.Contains("客户端 JAR 缺失", check.Detail);

                string jar = Path.Combine(instance, "fabric-loader-0.20.1-1.21.1.jar");
                File.WriteAllBytes(jar, new byte[1024 * 1024 + 1]);

                vm.RefreshJoinChecks();

                check = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(check);
                Assert.True(check!.IsOk);
                Assert.Contains("已就绪", check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Join_check_accepts_a_loader_instance_with_inherited_client_jar()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-inherited-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string folder = "fabric-loader-0.20.1-1.21.1";
                Directory.CreateDirectory(Path.Combine(paths.DefaultMinecraftDirectory, "versions", folder));

                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;
                vm.Versions.Add(new VersionItem(
                    new MinecraftVersion(folder, folder, VersionKind.Release, null, hasClientJar: true),
                    clientJarMissing: false,
                    gameDirectory: paths.DefaultMinecraftDirectory));

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    LoaderVersion = "0.20.1",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(check);
                Assert.True(check!.IsOk, check.Detail);
                Assert.Contains("已就绪", check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Join_check_uses_neoforge_json_to_choose_the_matching_minecraft_version()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-neoforge-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string versions = Path.Combine(paths.DefaultMinecraftDirectory, "versions");
                string wrong = "neoforge-20.4.0";
                string right = "neoforge-20.4.1";
                Directory.CreateDirectory(Path.Combine(versions, wrong));
                Directory.CreateDirectory(Path.Combine(versions, right));
                File.WriteAllText(
                    Path.Combine(versions, wrong, wrong + ".json"),
                    "{\"id\":\"" + wrong + "\",\"inheritsFrom\":\"1.20.1\"}");
                File.WriteAllText(
                    Path.Combine(versions, right, right + ".json"),
                    "{\"id\":\"" + right + "\",\"inheritsFrom\":\"1.21.1\"}");
                Directory.CreateDirectory(Path.Combine(versions, "1.21.1"));
                File.WriteAllBytes(
                    Path.Combine(versions, "1.21.1", "1.21.1.jar"),
                    new byte[1024 * 1024 + 1]);

                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "neoforge",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(check);
                Assert.True(check!.IsOk, check.Detail);
                Assert.Contains(right, check.Detail);
                Assert.DoesNotContain(wrong, check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Join_check_follows_inherits_from_when_version_list_is_empty()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-disk-inherited-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string versions = Path.Combine(paths.DefaultMinecraftDirectory, "versions");
                string loader = "fabric-loader-0.20.1-1.21.1";
                string parent = "1.21.1";
                Directory.CreateDirectory(Path.Combine(versions, loader));
                Directory.CreateDirectory(Path.Combine(versions, parent));
                File.WriteAllText(
                    Path.Combine(versions, loader, loader + ".json"),
                    "{\"id\":\"" + loader + "\",\"inheritsFrom\":\"" + parent + "\"}");
                File.WriteAllBytes(
                    Path.Combine(versions, parent, parent + ".jar"),
                    new byte[1024 * 1024 + 1]);

                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    LoaderVersion = "0.20.1",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(check);
                Assert.True(check!.IsOk, check.Detail);
                Assert.Contains("已就绪", check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Changing_game_directory_refreshes_join_checks()
        {
            string first = TestSupport.CreateTemporaryDirectory("nlmcl-join-dir-a-");
            string second = TestSupport.CreateTemporaryDirectory("nlmcl-join-dir-b-");
            try
            {
                AppPaths paths = new AppPaths(first);
                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    LoaderVersion = "0.20.1",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.CurrentGameDirectory = first;
                ServerJoinCheck? before = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(before);
                Assert.False(before!.IsOk);

                string version = Path.Combine(second, "versions", "fabric-loader-0.20.1-1.21.1");
                Directory.CreateDirectory(version);
                File.WriteAllText(
                    Path.Combine(version, "fabric-loader-0.20.1-1.21.1.json"),
                    "{\"id\":\"fabric-loader-0.20.1-1.21.1\"}");
                File.WriteAllBytes(
                    Path.Combine(version, "fabric-loader-0.20.1-1.21.1.jar"),
                    new byte[1024 * 1024 + 1]);

                vm.CurrentGameDirectory = second;

                ServerJoinCheck? after = vm.JoinChecks.FirstOrDefault(item => item.Label == "客户端");
                Assert.NotNull(after);
                Assert.True(after!.IsOk, after.Detail);
            }
            finally
            {
                try { Directory.Delete(first, true); } catch (IOException) { }
                try { Directory.Delete(second, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Switching_account_refreshes_join_checks()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-account-");
            try
            {
                AppPaths paths = new AppPaths(root);
                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    LoaderVersion = "0.20.1",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });
                vm.RefreshJoinChecks();

                ServerJoinCheck? before = vm.JoinChecks.FirstOrDefault(item => item.Label == "账号");
                Assert.NotNull(before);
                Assert.False(before!.IsOk);

                vm.Account = new Account("UniverseYYZH", "uuid-universe", AccountType.Offline);

                ServerJoinCheck? after = vm.JoinChecks.FirstOrDefault(item => item.Label == "账号");
                Assert.NotNull(after);
                Assert.True(after!.IsOk, after.Detail);
                Assert.Contains("UniverseYYZH", after.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Join_check_reports_a_corrupted_automodpack_jar()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-corrupt-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);
                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.20.1-1.21.1");
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                string jar = Path.Combine(mods, "automodpack-4.0.6.jar");
                File.WriteAllText(jar, "not a zip");

                MainViewModel vm = TestSupport.CreateMain(new FixedCatalog(), paths);
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = vm.JoinChecks.FirstOrDefault(item => item.Label == "模组");
                Assert.NotNull(check);
                Assert.False(check!.IsOk);
                Assert.Contains("文件损坏", check.Detail);

                File.Delete(jar);
                using (ZipArchive archive = ZipFile.Open(jar, ZipArchiveMode.Create))
                {
                    archive.CreateEntry("fabric.mod.json");
                }

                vm.RefreshJoinChecks();
                check = vm.JoinChecks.FirstOrDefault(item => item.Label == "模组");
                Assert.NotNull(check);
                Assert.True(check!.IsOk);
                Assert.Contains("已就位", check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task Modded_join_installs_automodpack_in_the_resolved_instance_directory()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-isolated-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string rawInstance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.15.0-1.21.1");
                Directory.CreateDirectory(rawInstance);
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "java.exe"), string.Empty);

                string isolated = Path.Combine(root, "isolated-game");
                Directory.CreateDirectory(isolated);

                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    javaLocator: new FixedJavaLocator(root),
                    launcher: launcher,
                    profilePicker: new ServerProfilePicker(
                        new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1)),
                    resourceCatalog: new MatchingAutoModpackCatalog(),
                    fileDownloader: new WritingAutoModpackDownloader(),
                    instancePaths: new FixedGameDirectoryResolver(isolated));

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

                Assert.Equal(1, launcher.Calls);
                Assert.True(File.Exists(Path.Combine(isolated, "mods", "automodpack-4.0.6.jar")));
                Assert.False(File.Exists(Path.Combine(rawInstance, "mods", "automodpack-4.0.6.jar")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void Join_check_reads_mods_from_the_resolved_instance_directory()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-check-isolated-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string rawInstance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.20.1-1.21.1");
                Directory.CreateDirectory(rawInstance);

                string isolated = Path.Combine(root, "isolated-game");
                string mods = Path.Combine(isolated, "mods");
                Directory.CreateDirectory(mods);
                using (ZipArchive archive = ZipFile.Open(
                    Path.Combine(mods, "automodpack-4.0.6.jar"), ZipArchiveMode.Create))
                {
                    archive.CreateEntry("fabric.mod.json");
                }

                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    instancePaths: new FixedGameDirectoryResolver(isolated));
                vm.CurrentGameDirectory = paths.DefaultMinecraftDirectory;

                ServerProfile profile = new ServerProfile("pokemon", "宝可梦", "1.21.1")
                {
                    Loader = "fabric",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded
                };
                profile.Addresses.Add("play.example.org:25565");
                vm.Ningli.SetProfiles(new[] { profile });

                vm.RefreshJoinChecks();

                ServerJoinCheck? check = vm.JoinChecks.FirstOrDefault(item => item.Label == "模组");
                Assert.NotNull(check);
                Assert.True(check!.IsOk);
                Assert.Contains("已就位", check.Detail);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task Modded_join_downloads_automodpack_with_progress()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-progress-");
            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DefaultMinecraftDirectory);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                ProgressRecordingDownloader downloader = new ProgressRecordingDownloader();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MatchingAutoModpackCatalog(),
                    fileDownloader: downloader);

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

                Assert.Equal(0.42, downloader.LastProgress);
                Assert.Equal(0, launcher.Calls);
                Assert.Contains("进模组服失败", vm.StatusText);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_downloads_automodpack_and_launches_with_server_address()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-success-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.15.0-1.21.1");
                Directory.CreateDirectory(instance);
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "java.exe"), string.Empty);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    javaLocator: new FixedJavaLocator(root),
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MatchingAutoModpackCatalog(),
                    fileDownloader: new WritingAutoModpackDownloader());

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

                Assert.True(launcher.Calls == 1, vm.StatusText);
                Assert.Equal("example.com:25565", launcher.LastRequest?.ServerAddress);
                Assert.True(File.Exists(Path.Combine(instance, "mods", "automodpack-4.0.6.jar")));
                Assert.Contains("已启动", vm.StatusText);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Modded_join_installs_the_pinned_loader_instead_of_reusing_another_loader()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-loader-pin-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string wrongInstance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.20.1-1.21.1");
                Directory.CreateDirectory(wrongInstance);
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "java.exe"), string.Empty);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    javaLocator: new FixedJavaLocator(root),
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MatchingAutoModpackCatalog(),
                    fileDownloader: new WritingAutoModpackDownloader());

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

                string wanted = Path.Combine(
                    paths.DefaultMinecraftDirectory,
                    "versions",
                    "fabric-loader-0.15.0-1.21.1");
                Assert.True(launcher.Calls == 1, vm.StatusText);
                Assert.Equal("fabric-loader-0.15.0-1.21.1", launcher.LastRequest?.VersionId);
                Assert.True(File.Exists(Path.Combine(wanted, "mods", "automodpack-4.0.6.jar")));
                Assert.False(Directory.Exists(Path.Combine(wrongInstance, "mods")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Installed_automodpack_with_matching_size_is_not_downloaded()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-size-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.15.0-1.21.1");
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "java.exe"), string.Empty);
                File.WriteAllText(Path.Combine(mods, "automodpack-4.0.6.jar"), "automodpack");

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    javaLocator: new FixedJavaLocator(root),
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MatchingAutoModpackCatalog(),
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

                Assert.True(launcher.Calls == 1, vm.StatusText);
                Assert.Equal("automodpack", File.ReadAllText(
                    Path.Combine(mods, "automodpack-4.0.6.jar")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Truncated_automodpack_is_replaced()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-truncated-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.15.0-1.21.1");
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "java.exe"), string.Empty);
                File.WriteAllText(Path.Combine(mods, "automodpack-4.0.6.jar"), "old");

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                WritingAutoModpackDownloader downloader = new WritingAutoModpackDownloader();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    javaLocator: new FixedJavaLocator(root),
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MatchingAutoModpackCatalog(),
                    fileDownloader: downloader);

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

                Assert.True(launcher.Calls == 1, vm.StatusText);
                Assert.Equal(1, downloader.Calls);
                Assert.Equal("automodpack", File.ReadAllText(
                    Path.Combine(mods, "automodpack-4.0.6.jar")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Failed_automodpack_download_leaves_no_partial_target()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-join-partial-");
            try
            {
                AppPaths paths = new AppPaths(root);
                string instance = Path.Combine(
                    paths.DefaultMinecraftDirectory, "versions", "fabric-loader-0.15.0-1.21.1");
                string mods = Path.Combine(instance, "mods");
                Directory.CreateDirectory(mods);
                Directory.CreateDirectory(Path.Combine(root, "bin"));
                File.WriteAllText(Path.Combine(root, "bin", "java.exe"), string.Empty);

                ServerProfilePicker picker = new ServerProfilePicker(
                    new FixedResolver(), new OnlinePinger(), TimeSpan.FromSeconds(1));
                CountingLauncher launcher = new CountingLauncher();
                MainViewModel vm = TestSupport.CreateMain(
                    new FixedCatalog(),
                    paths,
                    javaLocator: new FixedJavaLocator(root),
                    launcher: launcher,
                    profilePicker: picker,
                    resourceCatalog: new MatchingAutoModpackCatalog(),
                    fileDownloader: new FailingAutoModpackDownloader());

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

                Assert.Contains("进模组服失败", vm.StatusText);
                Assert.Equal(0, launcher.Calls);
                Assert.False(File.Exists(Path.Combine(mods, "automodpack-4.0.6.jar")));
                Assert.False(File.Exists(Path.Combine(mods, "automodpack-4.0.6.jar.part")));
            }
            finally
            {
                Directory.Delete(root, true);
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

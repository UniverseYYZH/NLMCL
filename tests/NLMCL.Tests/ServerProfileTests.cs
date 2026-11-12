using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>服务器档案存储与入口挑选。</summary>
    public class ServerProfileTests
    {
        private sealed class FakeResolver : IServerAddressResolver
        {
            public Dictionary<string, int> Ports { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Task<ServerEndpoint> ResolveAsync(string host, int port, CancellationToken cancellationToken)
            {
                if (Ports.TryGetValue(host, out int mapped))
                {
                    return Task.FromResult(new ServerEndpoint(host, mapped, true));
                }

                return Task.FromResult(new ServerEndpoint(host, port, false));
            }
        }

        private sealed class FakePinger : IServerPinger
        {
            private int _active;

            public Dictionary<string, (bool ok, int online, int max, int latency)> Rooms { get; }
                = new(StringComparer.OrdinalIgnoreCase);

            public TimeSpan Delay { get; set; }

            public bool IgnoreCancellation { get; set; }

            public HashSet<string> HangingHosts { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public int MaxConcurrent { get; private set; }

            public async Task<LanRoom> PingAsync(string host, int port, CancellationToken cancellationToken)
            {
                int active = Interlocked.Increment(ref _active);
                MaxConcurrent = Math.Max(MaxConcurrent, active);

                try
                {
                    if (HangingHosts.Contains(host))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                    }
                    else if (Delay > TimeSpan.Zero)
                    {
                        await Task.Delay(Delay, IgnoreCancellation ? CancellationToken.None : cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (!Rooms.TryGetValue(host, out var data) || !data.ok)
                    {
                        throw new TimeoutException("连接超时：" + host + ":" + port);
                    }

                    return new LanRoom(host, port)
                    {
                        Pinged = true,
                        Online = data.online,
                        Max = data.max,
                        LatencyMs = data.latency,
                        VersionName = "Velocity 1.7.2-1.21.11"
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
            }
        }

        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [Fact]
        public async Task Builtin_profile_seeds_and_persists()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                ServerProfileStore store = new ServerProfileStore(paths);

                IReadOnlyList<ServerProfile> profiles = await store.LoadAsync(CancellationToken.None);

                Assert.Equal(2, profiles.Count);   // 宝可梦（模组）+ 还原服（纯净）
                ServerProfile pokemon = profiles[0];

                Assert.Equal("方块宁理 · 宝可梦服", pokemon.Name);
                Assert.Equal("1.21.1", pokemon.Minecraft);
                Assert.Equal("fabric", pokemon.Loader);
                Assert.Equal("4.0.6", pokemon.AutoModpackVersion);
                Assert.Equal(ServerProfileMode.Modded, pokemon.Mode);
                Assert.Equal(new[] { "mc2.nxist.top", "mc4.nxist.top" }, pokemon.Addresses);

                Assert.True(File.Exists(Path.Combine(paths.DataRoot, "server-profiles.json")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Save_and_load_round_trip()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                ServerProfileStore store = new ServerProfileStore(paths);

                await store.LoadAsync(CancellationToken.None); // 触发写入内置

                ServerProfile custom = new ServerProfile("local-test", "测试服", "1.20.1")
                {
                    Loader = "forge",
                    LoaderVersion = "47.4.28",
                    Mode = ServerProfileMode.Vanilla
                };

                custom.Addresses.Add("10.21.1.2:25565");

                await store.SaveAsync(new[] { custom }, CancellationToken.None);

                IReadOnlyList<ServerProfile> loaded = await store.LoadAsync(CancellationToken.None);

                Assert.Contains(loaded, p => p.Id == "local-test");
                Assert.Equal("测试服", loaded[0].Name);
                Assert.Equal("forge", loaded[0].Loader);
                Assert.Equal("47.4.28", loaded[0].LoaderVersion);
                Assert.Equal(ServerProfileMode.Vanilla, loaded[0].Mode);
                Assert.Equal("10.21.1.2:25565", loaded[0].Addresses[0]);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Picker_prefers_lower_latency()
        {
            FakeResolver resolver = new FakeResolver();
            resolver.Ports["mc2.nxist.top"] = 46718;
            resolver.Ports["mc4.nxist.top"] = 18491;

            FakePinger pinger = new FakePinger();
            pinger.Rooms["mc2.nxist.top"] = (true, 30, 40, 20);
            pinger.Rooms["mc4.nxist.top"] = (true, 3, 40, 45);

            ServerProfile profile = ServerProfileStore.BuiltIn[0];
            ServerProfilePicker picker = new ServerProfilePicker(resolver, pinger);

            ProfileEndpoint chosen = await picker.PickAsync(profile, CancellationToken.None);

            Assert.True(chosen.Online);
            Assert.Equal("mc2.nxist.top", chosen.Address);
            Assert.Equal(46718, chosen.Endpoint.Port);
            Assert.Equal(30, chosen.Players);
        }

        [Fact]
        public async Task Picker_probes_all_endpoints_in_parallel()
        {
            FakeResolver resolver = new FakeResolver();
            resolver.Ports["mc2.nxist.top"] = 46718;
            resolver.Ports["mc4.nxist.top"] = 18491;

            FakePinger pinger = new FakePinger { Delay = TimeSpan.FromMilliseconds(150) };
            pinger.Rooms["mc2.nxist.top"] = (true, 30, 40, 20);
            pinger.Rooms["mc4.nxist.top"] = (true, 3, 40, 45);

            ServerProfilePicker picker = new ServerProfilePicker(
                resolver, pinger, TimeSpan.FromSeconds(2));

            await picker.PickAsync(ServerProfileStore.BuiltIn[0], CancellationToken.None);

            Assert.Equal(2, pinger.MaxConcurrent);
        }

        [Fact]
        public async Task Picker_respects_the_total_deadline()
        {
            FakeResolver resolver = new FakeResolver();
            resolver.Ports["mc2.nxist.top"] = 46718;
            resolver.Ports["mc4.nxist.top"] = 18491;

            FakePinger pinger = new FakePinger();
            pinger.Rooms["mc2.nxist.top"] = (true, 30, 40, 20);
            pinger.Rooms["mc4.nxist.top"] = (true, 3, 40, 45);
            pinger.HangingHosts.Add("mc4.nxist.top");

            // mc2 立刻返回，mc4 永远不返回；总 deadline 应让 mc2 胜出。
            ServerProfilePicker picker = new ServerProfilePicker(
                resolver, pinger, TimeSpan.FromMilliseconds(150));

            var started = DateTime.UtcNow;
            ProfileEndpoint chosen = await picker.PickAsync(
                ServerProfileStore.BuiltIn[0], CancellationToken.None);
            TimeSpan elapsed = DateTime.UtcNow - started;

            Assert.True(elapsed < TimeSpan.FromSeconds(2));
            Assert.Equal("mc2.nxist.top", chosen.Address);
        }

        [Fact]
        public async Task Picker_falls_back_to_first_when_all_offline()
        {
            FakePinger pinger = new FakePinger(); // 所有地址都抛超时

            ServerProfile profile = ServerProfileStore.BuiltIn[0];
            ServerProfilePicker picker = new ServerProfilePicker(new FakeResolver(), pinger);

            ProfileEndpoint chosen = await picker.PickAsync(profile, CancellationToken.None);

            Assert.False(chosen.Online);
            Assert.Equal("mc2.nxist.top", chosen.Address);
            Assert.NotNull(chosen.Error);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>常用服务器：地址解析、默认端口、持久化与内置预设。</summary>
    public class ServerBookmarkTests
    {
        [Theory]
        [InlineData("mc2.nxist.top", "mc2.nxist.top:25565")]
        [InlineData("mc2.nxist.top:25566", "mc2.nxist.top:25566")]
        public void Endpoint_fills_default_port(string address, string expected)
        {
            Assert.Equal(expected, new ServerBookmark("x", address).Endpoint);
        }

        [Theory]
        [InlineData("mc2.nxist.top", "mc2.nxist.top", 25565)]
        [InlineData("1.2.3.4:12345", "1.2.3.4", 12345)]
        public void Parses_host_and_port(string text, string host, int port)
        {
            Assert.True(ServerBookmark.TryParse(text, out string actualHost, out int actualPort, out _));
            Assert.Equal(host, actualHost);
            Assert.Equal(port, actualPort);
        }

        [Theory]
        [InlineData("")]
        [InlineData("host:99999")]
        [InlineData("host:abc")]
        public void Rejects_bad_addresses(string text)
        {
            Assert.False(ServerBookmark.TryParse(text, out _, out _, out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public async Task First_load_seeds_built_in_servers()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-servers-" + Guid.NewGuid().ToString("N"));

            try
            {
                ServerBookmarkStore store = new ServerBookmarkStore(new AppPaths(root));

                IReadOnlyList<ServerBookmark> servers = await store.LoadAsync(CancellationToken.None);

                Assert.Equal(3, servers.Count);
                Assert.Contains(servers, s => s.Address == "mc2.nxist.top");
                Assert.Contains(servers, s => s.Address == "mc4.nxist.top");
                Assert.Contains(servers, s => s.Address == "mua.nxist.top");

                // 首次加载应落盘
                Assert.True(File.Exists(Path.Combine(root, "NLMCL", "servers.json")));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class RecordingStore : IServerBookmarkStore
        {
            public IReadOnlyList<ServerBookmark>? Saved { get; private set; }

            public Task<IReadOnlyList<ServerBookmark>> LoadAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ServerBookmark>>(new List<ServerBookmark>());

            public Task SaveAsync(IReadOnlyList<ServerBookmark> servers, CancellationToken cancellationToken)
            {
                Saved = servers;
                return Task.CompletedTask;
            }
        }

        private sealed class EmptyCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<MinecraftVersion>>(new List<MinecraftVersion>());
        }

        [Fact]
        public async Task MainViewModel_adds_and_removes_custom_servers()
        {
            RecordingStore store = new RecordingStore();
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-servers-" + Guid.NewGuid().ToString("N"));

            try
            {
                NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                    new EmptyCatalog(), new AppPaths(root), bookmarks: store);

                vm.NewServerName = "我的服";
                vm.NewServerAddress = "play.example.com:25570";

                await vm.AddServerAsync();

                Assert.Single(vm.Servers);
                Assert.Equal("play.example.com:25570", vm.Servers[0].Endpoint);
                Assert.NotNull(store.Saved);

                await vm.RemoveServerAsync(vm.Servers[0]);

                Assert.Empty(vm.Servers);
                Assert.Empty(store.Saved!);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public async Task Saves_and_reloads_custom_servers()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-servers-" + Guid.NewGuid().ToString("N"));

            try
            {
                ServerBookmarkStore store = new ServerBookmarkStore(new AppPaths(root));

                await store.SaveAsync(
                    new[] { new ServerBookmark("自建服", "192.168.1.9:25570") },
                    CancellationToken.None);

                IReadOnlyList<ServerBookmark> servers = await store.LoadAsync(CancellationToken.None);

                Assert.Single(servers);
                Assert.Equal("自建服", servers[0].Name);
                Assert.Equal("192.168.1.9:25570", servers[0].Endpoint);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

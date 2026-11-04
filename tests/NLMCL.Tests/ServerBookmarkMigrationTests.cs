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
    /// <summary>常用服务器的内置预设与老命名迁移。</summary>
    public class ServerBookmarkMigrationTests
    {
        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-bm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [Fact]
        public void Builtin_entries_describe_real_purpose()
        {
            IReadOnlyList<ServerBookmark> builtIn = ServerBookmarkStore.BuiltIn;

            Assert.Equal(2, builtIn.Count);
            Assert.Equal("方块宁理 · 大厅（线路一）", builtIn[0].Name);
            Assert.Equal("mc2.nxist.top", builtIn[0].Address);
            Assert.Equal("方块宁理 · 大厅（线路二）", builtIn[1].Name);
            Assert.Equal("mc4.nxist.top", builtIn[1].Address);
            // 认证服（mua.nxist.top）已从游戏列表移除：它不是游戏服务器
        }

        [Fact]
        public async Task Old_seed_names_are_migrated_and_persisted()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DataRoot);

                File.WriteAllText(
                    Path.Combine(paths.DataRoot, "servers.json"),
                    @"{
                        ""servers"": [
                            { ""name"": ""方块宁理 · 线路一"", ""address"": ""mc2.nxist.top"" },
                            { ""name"": ""方块宁理 · 线路二"", ""address"": ""mc4.nxist.top"" }
                        ]
                    }");

                ServerBookmarkStore store = new ServerBookmarkStore(paths);

                IReadOnlyList<ServerBookmark> loaded = await store.LoadAsync(CancellationToken.None);

                Assert.Equal("方块宁理 · 大厅（线路一）", loaded[0].Name);
                Assert.Equal("mc2.nxist.top", loaded[0].Address);
                Assert.Equal("方块宁理 · 大厅（线路二）", loaded[1].Name);

                // 迁移结果要落盘，否则下次启动又变回去
                string saved = File.ReadAllText(Path.Combine(paths.DataRoot, "servers.json"));
                Assert.Contains("大厅（线路一）", saved);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Custom_names_are_never_overwritten()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                Directory.CreateDirectory(paths.DataRoot);

                File.WriteAllText(
                    Path.Combine(paths.DataRoot, "servers.json"),
                    @"{
                        ""servers"": [
                            { ""name"": ""我自己的服"", ""address"": ""mc2.nxist.top"" }
                        ]
                    }");

                ServerBookmarkStore store = new ServerBookmarkStore(paths);

                IReadOnlyList<ServerBookmark> loaded = await store.LoadAsync(CancellationToken.None);

                Assert.Equal("我自己的服", loaded[0].Name);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

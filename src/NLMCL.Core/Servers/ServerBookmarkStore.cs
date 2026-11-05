using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Servers
{
    /// <summary>
    /// 常用服务器列表（<c>%APPDATA%\NLMCL\servers.json</c>）。
    ///
    /// 首次运行时写入内置预设「方块宁理」三条线路；之后以文件为准，
    /// 用户可以直接编辑这个 JSON 增删服务器。
    /// </summary>
    public sealed class ServerBookmarkStore : IServerBookmarkStore
    {
        public static readonly IReadOnlyList<ServerBookmark> BuiltIn = new[]
        {
            new ServerBookmark("方块宁理 · 大厅（线路一）", "mc2.nxist.top"),
            new ServerBookmark("方块宁理 · 大厅（线路二）", "mc4.nxist.top"),
            new ServerBookmark("方块宁理 · 学生专线", "mua.nxist.top")
        };

        private readonly IAppPaths _paths;

        public ServerBookmarkStore(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public async Task<IReadOnlyList<ServerBookmark>> LoadAsync(CancellationToken cancellationToken)
        {
            string file = FilePath;

            if (!File.Exists(file))
            {
                await SaveAsync(BuiltIn, cancellationToken).ConfigureAwait(false);
                return BuiltIn;
            }

            try
            {
                string json = File.ReadAllText(file, Encoding.UTF8);
                JObject root = JObject.Parse(json);

                List<ServerBookmark> servers = new List<ServerBookmark>();

                if (root["servers"] is JArray items)
                {
                    foreach (JToken item in items)
                    {
                        string? name = item.Value<string>("name");
                        string? address = item.Value<string>("address");

                        if (!string.IsNullOrWhiteSpace(address))
                        {
                            servers.Add(new ServerBookmark(
                                string.IsNullOrWhiteSpace(name) ? address! : name!, address!));
                        }
                    }
                }

                // 老版本写入的是「线路一 / 线路二 / 学生专线」，现在按实际用途改名。
                // 只改与内置预设完全同名的那几条，用户自己改过名字的一律不动。
                if (MigrateSeededNames(servers))
                {
                    await SaveAsync(servers, cancellationToken).ConfigureAwait(false);
                }

                return servers;
            }
            catch (Exception)
            {
                // 文件坏了就回退到预设，不阻塞界面
                return BuiltIn;
            }
        }

        /// <summary>把老版本的内置命名迁移成现在的用途命名；只匹配"名字与内置预设完全一致"的条目。</summary>
        internal static bool MigrateSeededNames(IList<ServerBookmark> servers)
        {
            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["方块宁理 · 线路一"] = "方块宁理 · 大厅（线路一）",
                ["方块宁理 · 线路二"] = "方块宁理 · 大厅（线路二）",
                ["方块宁理 · 宝可梦服"] = "方块宁理 · 大厅（线路一）",
                ["方块宁理 · 宝可梦生存"] = "方块宁理 · 大厅（线路二）"
            };

            bool changed = false;

            for (int i = 0; i < servers.Count; i++)
            {
                if (renames.TryGetValue(servers[i].Name, out string? newName))
                {
                    servers[i] = new ServerBookmark(newName, servers[i].Address);
                    changed = true;
                }
            }

            // 修正历史误判：mua.nxist.top 是「学生专线」游戏入口（同一套 Velocity 导航），
            // 不是账号认证 API。之前把它标成"非游戏服"并删除是错的，这里改回"学生专线"。
            for (int i = 0; i < servers.Count; i++)
            {
                if (servers[i].Address.StartsWith("mua.nxist.top", StringComparison.OrdinalIgnoreCase)
                    && (servers[i].Name.Contains("认证") || servers[i].Name.Contains("高校联盟")))
                {
                    servers[i] = new ServerBookmark("方块宁理 · 学生专线", servers[i].Address);
                    changed = true;
                }
            }

            return changed;
        }
        public async Task SaveAsync(IReadOnlyList<ServerBookmark> servers, CancellationToken cancellationToken)
        {
            string file = FilePath;
            string? directory = Path.GetDirectoryName(file);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            JArray items = new JArray();

            foreach (ServerBookmark server in servers)
            {
                items.Add(new JObject
                {
                    ["name"] = server.Name,
                    ["address"] = server.Address
                });
            }

            string content = new JObject { ["servers"] = items }.ToString(Formatting.Indented);
            string temporary = file + ".tmp";

            await Task.Run(
                () =>
                {
                    File.WriteAllText(temporary, content, new UTF8Encoding(false));
                    File.Copy(temporary, file, overwrite: true);
                    File.Delete(temporary);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private string FilePath => Path.Combine(_paths.DataRoot, "servers.json");
    }
}

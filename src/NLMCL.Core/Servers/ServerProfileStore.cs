using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Servers
{
    /// <summary>
    /// 服务器档案存储（<c>%APPDATA%\NLMCL\server-profiles.json</c>）。
    ///
    /// 首次运行写入内置档案（方块宁理 · 宝可梦服）；之后以文件为准，
    /// 服主换了地址/版本，用户改文件或改界面即可，不用等新版本。
    /// </summary>
    public sealed class ServerProfileStore : IServerProfileStore
    {
        /// <summary>内置档案：两个地址是服主用来分流的两条入口，指向同一台服务器。</summary>
        public static IReadOnlyList<ServerProfile> BuiltIn
        {
            get
            {
                ServerProfile pokemon = new ServerProfile("nxist-pokemon", "方块宁理 · 宝可梦服", "1.21.1")
                {
                    Loader = "fabric",
                    AutoModpackVersion = "4.0.6",
                    Mode = ServerProfileMode.Modded,
                    MemoryMb = 6144,
                    Notes = "纯净客户端也能进大厅；进宝可梦子服需要在游戏内用菜单传送"
                };

                pokemon.Addresses.Add("mc2.nxist.top");
                pokemon.Addresses.Add("mc4.nxist.top");

                return new[] { pokemon };
            }
        }

        private readonly IAppPaths _paths;

        public ServerProfileStore(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        private string FilePath => Path.Combine(_paths.DataRoot, "server-profiles.json");

        public async Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(FilePath))
            {
                IReadOnlyList<ServerProfile> builtIn = BuiltIn;
                await SaveAsync(builtIn, cancellationToken).ConfigureAwait(false);
                return builtIn;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                List<ServerProfile> profiles = new List<ServerProfile>();

                if (root["profiles"] is JArray items)
                {
                    foreach (JObject item in items.OfType<JObject>())
                    {
                        string? id = item.Value<string>("id");

                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        ServerProfile profile = new ServerProfile(
                            id!,
                            item.Value<string>("name") ?? id!,
                            item.Value<string>("minecraft") ?? string.Empty)
                        {
                            Loader = item.Value<string>("loader") ?? string.Empty,
                            LoaderVersion = item.Value<string>("loaderVersion") ?? string.Empty,
                            AutoModpackVersion = item.Value<string>("automodpack") ?? string.Empty,
                            Notes = item.Value<string>("notes") ?? string.Empty,
                            MemoryMb = item.Value<int?>("memory") ?? 0,
                            Mode = string.Equals(item.Value<string>("mode"), "modded", StringComparison.OrdinalIgnoreCase)
                                ? ServerProfileMode.Modded
                                : ServerProfileMode.Vanilla
                        };

                        if (item["addresses"] is JArray addresses)
                        {
                            foreach (JToken address in addresses)
                            {
                                string? value = address.Type == JTokenType.String ? address.ToString() : null;

                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    profile.Addresses.Add(value!.Trim());
                                }
                            }
                        }

                        if (profile.Addresses.Count > 0)
                        {
                            profiles.Add(profile);
                        }
                    }
                }

                return profiles;
            }
            catch (JsonException)
            {
                // 文件坏了不阻塞：回退到内置档案（不覆盖用户文件，避免丢自定义）
                return BuiltIn;
            }
            catch (IOException)
            {
                return BuiltIn;
            }
        }

        public Task SaveAsync(IReadOnlyList<ServerProfile> profiles, CancellationToken cancellationToken)
        {
            if (profiles == null)
            {
                throw new ArgumentNullException(nameof(profiles));
            }

            Directory.CreateDirectory(_paths.DataRoot);

            JArray items = new JArray();

            foreach (ServerProfile profile in profiles)
            {
                JArray addresses = new JArray();

                foreach (string address in profile.Addresses)
                {
                    addresses.Add(address);
                }

                items.Add(new JObject
                {
                    ["id"] = profile.Id,
                    ["name"] = profile.Name,
                    ["minecraft"] = profile.Minecraft,
                    ["loader"] = profile.Loader,
                    ["loaderVersion"] = profile.LoaderVersion,
                    ["automodpack"] = profile.AutoModpackVersion,
                    ["mode"] = profile.Mode == ServerProfileMode.Modded ? "modded" : "vanilla",
                    ["notes"] = profile.Notes,
                    ["memory"] = profile.MemoryMb,
                    ["addresses"] = addresses
                });
            }

            JObject root = new JObject { ["profiles"] = items };
            string temporary = FilePath + ".tmp";

            File.WriteAllText(temporary, root.ToString(Formatting.Indented), Encoding.UTF8);
            File.Copy(temporary, FilePath, overwrite: true);
            File.Delete(temporary);

            return Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// NeoForge 构建列表。
    ///
    /// 端点：<c>https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge</c>
    /// 返回 <c>{ name, files: [{ name: "21.1.80" }, ...] }</c>。
    ///
    /// 版本号前两段编码了 Minecraft 版本：21.1.x → 1.21.1，20.4.x → 1.20.4。
    /// （1.20.1 没有 NeoForge，从 1.20.2 开始。）
    /// </summary>
    public sealed class NeoForgeCatalog : INeoForgeCatalog
    {
        public const string DetailsUrl =
            "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge";

        private readonly IDownloadService _download;

        public NeoForgeCatalog(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<IReadOnlyList<LoaderBuild>> GetBuildsAsync(
            string minecraftVersion, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion))
            {
                throw new ArgumentException("游戏版本为空。", nameof(minecraftVersion));
            }

            string json = await _download.GetStringAsync(DetailsUrl, cancellationToken).ConfigureAwait(false);

            return Parse(json, minecraftVersion.Trim());
        }

        internal static IReadOnlyList<LoaderBuild> Parse(string json, string minecraftVersion)
        {
            JObject root;

            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("NeoForge 版本列表格式异常。");
            }

            List<LoaderBuild> builds = new List<LoaderBuild>();

            if (root["files"] is not JArray files)
            {
                return builds;
            }

            foreach (JToken file in files)
            {
                string? name = file.Value<string>("name");

                if (string.IsNullOrWhiteSpace(name) || !name!.Contains("."))
                {
                    continue;
                }

                // 只保留纯版本号（排除 maven-metadata.xml 之类）
                if (name.IndexOf('.') < 0 || !char.IsDigit(name[0]))
                {
                    continue;
                }

                if (!string.Equals(MinecraftOf(name!), minecraftVersion, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                builds.Add(new LoaderBuild(name!, minecraftVersion, null));
            }

            return builds
                .OrderByDescending(build => ForgeCatalog.VersionKey(build.Version))
                .ToList();
        }

        /// <summary>21.1.80 → 1.21.1；20.4.253 → 1.20.4。</summary>
        internal static string? MinecraftOf(string neoforgeVersion)
        {
            string[] parts = neoforgeVersion.Split('.');

            if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
            {
                return null;
            }

            // 1.21.1 / 1.20.4：前缀就是 "1.<major>.<minor>"
            if (major >= 20)
            {
                return "1." + major.ToString(CultureInfo.InvariantCulture)
                    + "." + minor.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }
    }
}

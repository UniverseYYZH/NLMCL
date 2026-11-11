using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Forge 构建列表（走 BMCLAPI 镜像）。
    ///
    /// 端点：<c>https://bmclapi2.bangbang93.com/forge/minecraft/{mc}</c>
    /// 返回数组，每项形如 { "version": "47.4.27", "mcversion": "1.20.1", "modified": "..." }。
    /// 官方 promotions 接口在镜像上是 404，所以直接用构建列表，取最新。
    /// </summary>
    public sealed class ForgeCatalog : IForgeCatalog
    {
        public const string BuildListUrl = "https://bmclapi2.bangbang93.com/forge/minecraft/";

        public const string OfficialMetadataUrl =
            "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

        private readonly IDownloadService _download;

        public ForgeCatalog(IDownloadService download)
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

            string version = minecraftVersion.Trim();
            string url = BuildListUrl + Uri.EscapeDataString(version);

            try
            {
                string json = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                return Parse(json, version);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // 镜像挂了不能连累整个下载页；官方 maven 的 metadata 仍能给出构建号。
                string xml = await _download
                    .GetStringAsync(OfficialMetadataUrl, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<LoaderBuild> official = ParseOfficialMetadata(xml, version);
                if (official.Count == 0)
                {
                    throw new InvalidOperationException(
                        "无法从镜像或官方源取得 Forge 构建列表：Minecraft " + version);
                }

                return official;
            }
        }

        internal static IReadOnlyList<LoaderBuild> Parse(string json, string minecraftVersion)
        {
            List<LoaderBuild> builds = new List<LoaderBuild>();

            JArray array;

            try
            {
                array = JArray.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("Forge 构建列表格式异常。");
            }

            foreach (JToken item in array)
            {
                string? version = item.Value<string>("version");
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                DateTimeOffset? modified = DateTimeOffset.TryParse(
                    item.Value<string>("modified"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed)
                    ? parsed
                    : (DateTimeOffset?)null;

                builds.Add(new LoaderBuild(version!, item.Value<string>("mcversion") ?? minecraftVersion, modified));
            }

            // 新版本在前：版本号按 "47.4.27" 的数字段比较，避免 47.10 < 47.9 这类字符串误区
            return builds
                .OrderByDescending(build => VersionKey(build.Version))
                .ToList();
        }

        /// <summary>解析官方 maven-metadata.xml；版本形如 1.20.1-47.10.2。</summary>
        internal static IReadOnlyList<LoaderBuild> ParseOfficialMetadata(string xml, string minecraftVersion)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("Forge 官方构建列表格式异常。");
            }

            string prefix = minecraftVersion.Trim() + "-";
            List<LoaderBuild> builds = new List<LoaderBuild>();

            foreach (XElement element in document.Descendants("version"))
            {
                string value = element.Value.Trim();
                if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string forge = value.Substring(prefix.Length);
                if (forge.Length > 0)
                {
                    builds.Add(new LoaderBuild(forge, minecraftVersion.Trim(), null));
                }
            }

            return builds
                .OrderByDescending(build => VersionKey(build.Version))
                .ToList();
        }

        internal static long VersionKey(string version)
        {
            string[] parts = version.Split('.');
            long key = 0;

            for (int index = 0; index < 3; index++)
            {
                key *= 1000;

                if (index < parts.Length && int.TryParse(parts[index], out int value))
                {
                    key += Math.Min(value, 999);
                }
            }

            return key;
        }
    }
}

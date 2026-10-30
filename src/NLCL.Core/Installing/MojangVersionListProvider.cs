using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLCL.Abstractions;

namespace NLCL.Core.Installing
{
    /// <summary>
    /// 从 Mojang 的 <c>version_manifest_v2.json</c> 读取可下载版本。
    ///
    /// 与安装编排同属 §2.1a 的豁免范围（版本 JSON 的枚举/解析层）。
    /// 传输仍走 <see cref="IDownloadService"/>（运行时是 PCL 的 `ModNet`）。
    /// </summary>
    public sealed class MojangVersionListProvider : IVersionListProvider
    {
        private readonly IDownloadService _download;
        private readonly InstallOptions _options;

        public MojangVersionListProvider(IDownloadService download)
            : this(download, new InstallOptions())
        {
        }

        public MojangVersionListProvider(IDownloadService download, InstallOptions options)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken)
        {
            string manifest = await _download
                .GetStringAsync(_options.ManifestUrl, cancellationToken)
                .ConfigureAwait(false);

            return Parse(manifest);
        }

        /// <summary>解析版本清单。按发布时间倒序，与 PCL / legacy 的展示顺序一致。</summary>
        internal static IReadOnlyList<VanillaVersion> Parse(string manifestJson)
        {
            JObject root = JObject.Parse(manifestJson);
            JArray versions = root["versions"] as JArray;

            if (versions == null)
            {
                return Array.Empty<VanillaVersion>();
            }

            List<VanillaVersion> result = new List<VanillaVersion>(versions.Count);

            foreach (JToken token in versions)
            {
                string? id = token.Value<string>("id");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                string type = token.Value<string>("type") ?? string.Empty;
                DateTimeOffset? releasedAt = ParseTime(token.Value<string>("releaseTime"));

                result.Add(new VanillaVersion(id!, type, releasedAt));
            }

            return result
                .OrderByDescending(version => version.ReleasedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }

        private static DateTimeOffset? ParseTime(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value))
            {
                return value;
            }

            return null;
        }
    }
}

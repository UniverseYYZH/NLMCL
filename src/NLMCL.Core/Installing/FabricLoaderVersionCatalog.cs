using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Fabric 加载器版本清单。
    ///
    /// 端点：<c>https://meta.fabricmc.net/v2/versions/loader/{游戏版本}</c>
    /// 退回 <c>.../v2/versions/loader</c>（不带游戏版本）会拿到全部加载器版本，
    /// 但可能包含与该游戏版本不兼容的项，所以优先用带版本的接口。
    ///
    /// 以前 UI 里把加载器版本写死成 0.20.1，新游戏版本一出来就可能装不上。
    /// </summary>
    public sealed class FabricLoaderVersionCatalog : IFabricLoaderCatalog
    {
        public const string BaseUrl = "https://meta.fabricmc.net/v2/versions/loader";

        private readonly IDownloadService _download;

        public FabricLoaderVersionCatalog(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<IReadOnlyList<string>> GetLoaderVersionsAsync(
            string gameVersion, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                throw new ArgumentException("游戏版本为空。", nameof(gameVersion));
            }

            string url = BaseUrl + "/" + Uri.EscapeDataString(gameVersion.Trim());

            string json = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            return Parse(json);
        }

        /// <summary>解析 meta 返回的数组：每项形如 { "separator": "...", "build": 1, "maven": "...", "version": "0.16.9", "stable": true }。</summary>
        internal static IReadOnlyList<string> Parse(string json)
        {
            JArray array;

            try
            {
                array = JArray.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("Fabric 加载器清单格式异常。");
            }

            List<string> versions = new List<string>();

            foreach (JToken item in array)
            {
                // meta 的真实结构是嵌套的：{ "loader": { "version": "0.16.9", ... }, ... }
                // 顶层 version 只在旧接口/手写清单里出现，保留作回退。
                string? version = item["loader"]?.Value<string>("version")
                    ?? item.Value<string>("version");
                if (!string.IsNullOrWhiteSpace(version) && !versions.Contains(version!))
                {
                    versions.Add(version!);
                }
            }

            if (versions.Count == 0)
            {
                throw new InvalidOperationException("这个游戏版本没有可用的 Fabric 加载器。");
            }

            // meta 的顺序就是推荐顺序（新 → 旧），保持不变
            return versions;
        }
    }
}

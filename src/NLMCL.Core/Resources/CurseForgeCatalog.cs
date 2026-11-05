using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// CurseForge 搜索与文件列表（走 mcim 公共镜像，不需要 API key）。
    ///
    /// 为什么能用：mcim 把 CurseForge v1 接口代理成公开只读 HTTP，
    /// 返回的 downloadUrl 直接指向 edge.forgecdn.net（实测可下、不需要 referer）。
    /// 代价：依赖第三方服务，它挂了这条路就断；Modrinth 搜索不受影响。
    /// </summary>
    public sealed class CurseForgeCatalog : IResourceCatalog
    {
        private const string ApiBase = "https://mod.mcimirror.top/curseforge/v1";

        private readonly IDownloadService _downloads;

        public CurseForgeCatalog(IDownloadService downloads)
        {
            _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        }

        public async Task<IReadOnlyList<ResourceSummary>> SearchAsync(
            ResourceQuery query, CancellationToken cancellationToken)
        {
            string url = ApiBase + "/mods/search?gameId=432"
                + "&classId=" + ClassIdOf(query.Kind).ToString(CultureInfo.InvariantCulture)
                + "&searchFilter=" + Uri.EscapeDataString(query.Text ?? string.Empty)
                + "&pageSize=" + Math.Max(1, Math.Min(50, query.Limit)).ToString(CultureInfo.InvariantCulture)
                + "&index=" + Math.Max(0, query.Offset / Math.Max(1, query.Limit)).ToString(CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(query.GameVersion))
            {
                url += "&gameVersion=" + Uri.EscapeDataString(query.GameVersion!);
            }

            if (!string.IsNullOrWhiteSpace(query.Loader) && query.Kind == ResourceKind.Mod)
            {
                url += "&modLoaderType=" + LoaderTypeOf(query.Loader!).ToString(CultureInfo.InvariantCulture);
            }

            string json = await _downloads.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            JObject root = JObject.Parse(json);
            JArray? data = root["data"] as JArray;
            List<ResourceSummary> result = new List<ResourceSummary>();

            if (data != null)
            {
                foreach (JToken token in data)
                {
                    if (token is not JObject item)
                    {
                        continue;
                    }

                    string? logo = item["logo"] is JObject logoObject
                        ? logoObject.Value<string>("thumbnailUrl") ?? logoObject.Value<string>("url")
                        : null;

                    result.Add(new ResourceSummary(
                        item.Value<long>("id").ToString(CultureInfo.InvariantCulture),
                        item.Value<string>("slug") ?? string.Empty,
                        item.Value<string>("name") ?? string.Empty,
                        item.Value<string>("summary") ?? string.Empty,
                        logo,
                        null,
                        item.Value<long?>("downloadCount") ?? 0,
                        query.Kind));
                }
            }

            return result;
        }

        public async Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
            string projectId,
            ResourceKind kind,
            string? gameVersion,
            string? loader,
            CancellationToken cancellationToken)
        {
            string url = ApiBase + "/mods/" + Uri.EscapeDataString(projectId) + "/files?pageSize=20&index=0";

            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                url += "&gameVersion=" + Uri.EscapeDataString(gameVersion!);
            }

            if (!string.IsNullOrWhiteSpace(loader) && kind == ResourceKind.Mod)
            {
                url += "&modLoaderType=" + LoaderTypeOf(loader!).ToString(CultureInfo.InvariantCulture);
            }

            string json = await _downloads.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            JObject root = JObject.Parse(json);
            JArray? data = root["data"] as JArray;
            List<ResourceFile> result = new List<ResourceFile>();

            if (data == null)
            {
                return result;
            }

            foreach (JToken token in data)
            {
                if (token is not JObject file)
                {
                    continue;
                }

                string? downloadUrl = file.Value<string>("downloadUrl");

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    // 作者设为"外部下载"或镜像未缓存：跳过，别让用户点了个空地址
                    continue;
                }

                List<string> versions = new List<string>();

                if (file["gameVersions"] is JArray gameVersions)
                {
                    foreach (JToken version in gameVersions)
                    {
                        string? value = version.Value<string>();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            versions.Add(value!);
                        }
                    }
                }

                result.Add(new ResourceFile(
                    file.Value<string>("fileName") ?? ("curseforge-" + file.Value<long>("id") + ".jar"),
                    downloadUrl!,
                    file.Value<long?>("fileLength") ?? 0,
                    file.Value<string>("displayName") ?? string.Empty,
                    versions,
                    loader));
            }

            return result;
        }

        /// <summary>CurseForge 的 classId：模组 6 / 资源包 12 / 光影 6552 / 数据包 6945。</summary>
        internal static int ClassIdOf(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.ResourcePack:
                    return 12;
                case ResourceKind.ShaderPack:
                    return 6552;
                case ResourceKind.DataPack:
                    return 6945;
                default:
                    return 6;
            }
        }

        /// <summary>CurseForge 的 modLoaderType：1 Forge / 4 Fabric / 6 NeoForge。</summary>
        internal static int LoaderTypeOf(string loader)
        {
            switch (loader.ToLowerInvariant())
            {
                case "fabric":
                    return 4;
                case "neoforge":
                    return 6;
                case "quilt":
                    return 5;
                default:
                    return 1;
            }
        }
    }
}

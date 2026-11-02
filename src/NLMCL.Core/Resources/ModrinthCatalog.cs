using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// Modrinth 的资源搜索。
    ///
    /// 为什么先只做 Modrinth：CurseForge 的 API 需要 key，PCL 内嵌的是它自己的，
    /// 不能挪用（凭据不是代码）。Modrinth 免费、无需 key，国内直连可用。
    ///
    /// 端点：
    ///   GET /v2/search?query=&facets=[[...]]&limit=&offset=
    ///   GET /v2/project/{id}/version?game_versions=["1.20.1"]&loaders=["fabric"]
    /// </summary>
    public sealed class ModrinthCatalog : IResourceCatalog
    {
        public const string ApiBase = "https://api.modrinth.com/v2";


        private readonly IDownloadService _download;

        public ModrinthCatalog(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<IReadOnlyList<ResourceSummary>> SearchAsync(
            ResourceQuery query, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            string url = BuildSearchUrl(query);

            string json;
            try
            {
                json = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log(url, "失败：" + ex.Message);
                throw;
            }

            IReadOnlyList<ResourceSummary> hits = ParseSearch(json, query.Kind);
            Log(url, "命中 " + hits.Count);

            return hits;
        }

        public async Task<IReadOnlyList<ResourceFile>> GetFilesAsync(
            string projectId, ResourceKind kind, string? gameVersion, string? loader,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("项目 id 为空。", nameof(projectId));
            }

            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                parts.Add("game_versions=" + Uri.EscapeDataString("[\"" + gameVersion + "\"]"));
            }

            string? normalizedLoader = NormalizeLoader(kind, loader);
            if (normalizedLoader != null)
            {
                parts.Add("loaders=" + Uri.EscapeDataString("[\"" + normalizedLoader + "\"]"));
            }

            string url = ApiBase + "/project/" + Uri.EscapeDataString(projectId) + "/version";
            if (parts.Count > 0)
            {
                url += "?" + string.Join("&", parts);
            }

            string json = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            return ParseFiles(json);
        }

        /// <summary>构造搜索地址；Modrinth 的 facets 是二维数组的 JSON 字符串。</summary>
        internal static string BuildSearchUrl(ResourceQuery query)
        {
            List<string> facets = new List<string>
            {
                "[\"project_type:" + ProjectTypeOf(query.Kind) + "\"]"
            };

            if (!string.IsNullOrWhiteSpace(query.GameVersion))
            {
                facets.Add("[\"versions:" + query.GameVersion!.Trim() + "\"]");
            }

            string? loader = NormalizeLoader(query.Kind, query.Loader);
            if (loader != null)
            {
                facets.Add("[\"categories:" + loader + "\"]");
            }

            string url = ApiBase + "/search?limit=" + Math.Max(1, Math.Min(50, query.Limit))
                + "&offset=" + Math.Max(0, query.Offset)
                + "&index=relevance";

            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                url += "&query=" + Uri.EscapeDataString(query.Text.Trim());
            }

            url += "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");

            return url;
        }

        private static string ProjectTypeOf(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.ResourcePack:
                    return "resourcepack";
                case ResourceKind.ShaderPack:
                    return "shader";
                case ResourceKind.DataPack:
                    return "datapack";
                default:
                    return "mod";
            }
        }

        /// <summary>资源包/光影/数据包没有加载器维度；模组才按加载器筛。</summary>
        internal static string? NormalizeLoader(ResourceKind kind, string? loader)
        {
            if (kind != ResourceKind.Mod || string.IsNullOrWhiteSpace(loader))
            {
                return null;
            }

            string value = loader!.Trim().ToLowerInvariant();

            if (value == "fabric" || value == "forge" || value == "neoforge" || value == "quilt")
            {
                return value;
            }

            return null;
        }

        /// <summary>把搜索地址与命中数记到临时文件，面板空白时能直接定位是请求失败还是 0 结果。</summary>
        private static void Log(string url, string message)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nlmcl-search.log");
                lock (typeof(ModrinthCatalog))
                {
                    System.IO.File.AppendAllText(
                        path,
                        DateTime.Now.ToString("HH:mm:ss") + "  " + message + "  " + url + Environment.NewLine);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static IReadOnlyList<ResourceSummary> ParseSearch(string json, ResourceKind kind)
        {
            JObject root;

            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("资源搜索返回的不是合法 JSON。");
            }

            List<ResourceSummary> result = new List<ResourceSummary>();

            if (root["hits"] is not JArray hits)
            {
                return result;
            }

            foreach (JToken hit in hits)
            {
                string? id = hit.Value<string>("project_id") ?? hit.Value<string>("slug");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                result.Add(new ResourceSummary(
                    id!,
                    hit.Value<string>("slug") ?? string.Empty,
                    hit.Value<string>("title") ?? string.Empty,
                    hit.Value<string>("description") ?? string.Empty,
                    hit.Value<string>("icon_url"),
                    hit.Value<string>("author"),
                    hit.Value<long?>("downloads") ?? 0,
                    kind));
            }

            return result;
        }

        internal static IReadOnlyList<ResourceFile> ParseFiles(string json)
        {
            JArray array;

            try
            {
                array = JArray.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("版本列表返回的不是合法 JSON 数组。");
            }

            List<ResourceFile> files = new List<ResourceFile>();

            foreach (JToken version in array)
            {
                string? versionName = version.Value<string>("version_number");
                string? loader = (version["loaders"] as JArray)?.FirstOrDefault()?.Value<string>();

                IReadOnlyList<string> gameVersions = (version["game_versions"] as JArray)
                    ?.Select(token => token.Value<string>() ?? string.Empty)
                    .Where(text => text.Length > 0)
                    .ToArray() ?? Array.Empty<string>();

                JToken? primary = null;

                if (version["files"] is JArray candidates)
                {
                    primary = candidates.FirstOrDefault(file => file.Value<bool?>("primary") == true)
                        ?? candidates.FirstOrDefault();
                }

                if (primary == null)
                {
                    continue;
                }

                string? name = primary.Value<string>("filename");
                string? url = primary.Value<string>("url");

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                {
                    continue;
                }

                files.Add(new ResourceFile(
                    name!,
                    url!,
                    primary.Value<long?>("size") ?? 0,
                    versionName,
                    gameVersions,
                    loader));
            }

            return files;
        }
    }
}

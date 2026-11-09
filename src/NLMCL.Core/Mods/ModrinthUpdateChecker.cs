using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Mods
{
    /// <summary>
    /// Modrinth 更新检查：文件 SHA1 → <c>/version_file/{sha1}</c> 反查项目，
    /// 再取该项目在「当前游戏版本 + 加载器」下的最新版本号对比。
    ///
    /// 不在 Modrinth 上的模组（CurseForge 独占、自编译）会查不到，静默跳过。
    /// </summary>
    public sealed class ModrinthUpdateChecker : IModUpdateChecker
    {
        private const string ApiRoot = "https://api.modrinth.com/v2";

        private readonly IDownloadService _download;

        public ModrinthUpdateChecker(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<ModUpdateInfo?> CheckAsync(
            ModEntry entry, string? gameVersion, string? loader, CancellationToken cancellationToken)
        {
            if (entry == null || !System.IO.File.Exists(entry.FilePath))
            {
                return null;
            }

            string sha1 = CryptographyUtils.ComputeFileHash(entry.FilePath, CryptographyUtils.HashMethod.Sha1);

            JObject version = await GetJsonAsync(ApiRoot + "/version_file/" + sha1, cancellationToken)
                .ConfigureAwait(false);

            string? projectId = version.Value<string>("project_id");

            if (string.IsNullOrEmpty(projectId))
            {
                return null;
            }

            string projectUrl = "https://modrinth.com/project/" + projectId;

            string query = "/project/" + projectId + "/version";

            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                query += "?game_versions=" + Uri.EscapeDataString("[\"" + gameVersion + "\"]");
            }

            if (!string.IsNullOrWhiteSpace(loader) && !string.Equals(loader, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                query += (query.IndexOf('?') >= 0 ? "&" : "?")
                    + "loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]");
            }

            JArray versions = await GetJsonArrayAsync(ApiRoot + query, cancellationToken).ConfigureAwait(false);

            JToken? latest = versions
                .OrderByDescending(item => item.Value<string>("date_published") ?? string.Empty)
                .FirstOrDefault();

            return new ModUpdateInfo
            {
                ProjectUrl = projectUrl,
                LatestVersion = latest?.Value<string>("version_number")
            };
        }

        private async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            string text = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return JObject.Parse(text);
        }

        private async Task<JArray> GetJsonArrayAsync(string url, CancellationToken cancellationToken)
        {
            string text = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return JArray.Parse(text);
        }
    }
}

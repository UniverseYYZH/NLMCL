using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Mods
{
    /// <summary>CurseForge 更新检查：文件指纹 → exactMatches → latestFilesIndexes。</summary>
    public sealed class CurseForgeUpdateChecker : IModUpdateChecker
    {
        private const string ApiBase = "https://mod.mcimirror.top/curseforge/v1";

        private readonly IDownloadService _download;

        public CurseForgeUpdateChecker(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<ModUpdateInfo?> CheckAsync(
            ModEntry entry,
            string? gameVersion,
            string? loader,
            CancellationToken cancellationToken)
        {
            if (entry == null || !System.IO.File.Exists(entry.FilePath))
            {
                return null;
            }

            uint fingerprint = CurseForgeFingerprint.Compute(entry.FilePath);
            string body = "{\"fingerprints\":[" + fingerprint.ToString(CultureInfo.InvariantCulture) + "]}";

            string text = await _download
                .PostJsonAsync(ApiBase + "/fingerprints", body, null, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(text);
            JArray? matches = root["data"]?["exactMatches"] as JArray;
            JObject? match = matches?.OfType<JObject>().FirstOrDefault();

            if (match == null)
            {
                return null;
            }

            long projectId = match.Value<long>("id");
            JObject? currentFile = match["file"] as JObject;
            long currentFileId = currentFile?.Value<long>("id") ?? 0;

            ModUpdateInfo result = new ModUpdateInfo
            {
                ProjectUrl = "https://www.curseforge.com/projects/"
                    + projectId.ToString(CultureInfo.InvariantCulture)
            };

            string projectText = await _download
                .GetStringAsync(
                    ApiBase + "/mods/" + projectId.ToString(CultureInfo.InvariantCulture),
                    cancellationToken)
                .ConfigureAwait(false);

            JObject project = JObject.Parse(projectText);
            JArray? indexes = project["data"]?["latestFilesIndexes"] as JArray;

            if (indexes == null)
            {
                return result;
            }

            int? wantedLoader = LoaderId(loader);

            JObject? latest = indexes
                .OfType<JObject>()
                .FirstOrDefault(item =>
                {
                    string? version = item.Value<string>("gameVersion");

                    if (!string.IsNullOrWhiteSpace(gameVersion)
                        && !string.Equals(version, gameVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (wantedLoader.HasValue && item.Value<int?>("modLoader") != wantedLoader.Value)
                    {
                        return false;
                    }

                    return true;
                });

            long latestFileId = latest?.Value<long>("fileId") ?? 0;

            if (latest == null || latestFileId == currentFileId)
            {
                return result;
            }

            string? latestName = latest.Value<string>("filename");

            if (!string.IsNullOrWhiteSpace(latestName))
            {
                result.LatestVersion = latestName;
            }

            return result;
        }

        private static int? LoaderId(string? loader)
        {
            switch ((loader ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "forge":
                    return 1;
                case "fabric":
                    return 4;
                case "quilt":
                    return 5;
                case "neoforge":
                    return 6;
                default:
                    return null;
            }
        }
    }

    /// <summary>先查 Modrinth，查不到再查 CurseForge；任一命中即返回。</summary>
    public sealed class CompositeModUpdateChecker : IModUpdateChecker
    {
        private readonly IModUpdateChecker _modrinth;
        private readonly IModUpdateChecker _curseForge;

        public CompositeModUpdateChecker(IModUpdateChecker modrinth, IModUpdateChecker curseForge)
        {
            _modrinth = modrinth ?? throw new ArgumentNullException(nameof(modrinth));
            _curseForge = curseForge ?? throw new ArgumentNullException(nameof(curseForge));
        }

        public async Task<ModUpdateInfo?> CheckAsync(
            ModEntry entry,
            string? gameVersion,
            string? loader,
            CancellationToken cancellationToken)
        {
            ModUpdateInfo? modrinth = null;

            try
            {
                modrinth = await _modrinth.CheckAsync(entry, gameVersion, loader, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(modrinth?.LatestVersion))
                {
                    return modrinth;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Modrinth 查不到（独占/自编译）继续查 CurseForge。
            }

            try
            {
                ModUpdateInfo? curseForge = await _curseForge
                    .CheckAsync(entry, gameVersion, loader, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(curseForge?.LatestVersion))
                {
                    return curseForge;
                }

                return curseForge ?? modrinth;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 两边都查不到就保持“无更新”，不阻塞列表。
                return modrinth;
            }
        }
    }
}

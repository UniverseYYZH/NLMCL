using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Modrinth 整合包的元数据、更新检查与本地备份。
    /// 只做只读检查/备份；实例替换必须由上层在备份成功后执行。
    /// </summary>
    public sealed class ModpackUpdateService : IModpackUpdateService
    {
        private const string ModrinthApi = "https://api.modrinth.com/v2";

        private readonly IDownloadService _download;
        private readonly IInstancePackageService _packages;
        private readonly IAppPaths _paths;

        public ModpackUpdateService(
            IDownloadService download,
            IInstancePackageService packages,
            IAppPaths paths)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public Task<ModpackManifest?> ReadAsync(string instanceDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ModpackManifestStore.Read(instanceDirectory));
        }

        public async Task<ModpackUpdateInfo?> CheckAsync(
            string instanceDirectory, CancellationToken cancellationToken)
        {
            ModpackManifest? manifest = ModpackManifestStore.Read(instanceDirectory);

            if (manifest == null
                || !string.Equals(manifest.Source, "modrinth", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(manifest.VersionId))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(manifest.ProjectId))
            {
                manifest.ProjectId = await ResolveProjectIdAsync(manifest.VersionId, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(manifest.ProjectId))
                {
                    return null;
                }

                ModpackManifestStore.Write(instanceDirectory, manifest);
            }

            List<string> query = new List<string>();
            if (!string.IsNullOrWhiteSpace(manifest.MinecraftVersion))
            {
                query.Add("game_versions=" + Uri.EscapeDataString(
                    "[\"" + manifest.MinecraftVersion + "\"]"));
            }

            string? loader = LoaderFilter(manifest.Loader);
            if (loader != null)
            {
                query.Add("loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]"));
            }

            string url = ModrinthApi + "/project/" + Uri.EscapeDataString(manifest.ProjectId) + "/version";
            if (query.Count > 0)
            {
                url += "?" + string.Join("&", query);
            }

            JArray versions = JArray.Parse(await _download.GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false));

            JObject? latest = versions
                .OfType<JObject>()
                .Where(item => !string.IsNullOrWhiteSpace(item.Value<string>("id")))
                .OrderByDescending(PublishedAt)
                .FirstOrDefault(item => !string.Equals(
                    item.Value<string>("id"), manifest.VersionId, StringComparison.Ordinal));

            if (latest == null)
            {
                return null;
            }

            string versionId = latest.Value<string>("id") ?? string.Empty;
            JObject? file = (latest["files"] as JArray)?
                .OfType<JObject>()
                .FirstOrDefault(item => item.Value<bool?>("primary") == true)
                ?? (latest["files"] as JArray)?.OfType<JObject>().FirstOrDefault();

            string download = file?.Value<string>("url") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(download))
            {
                return null;
            }

            return new ModpackUpdateInfo(
                manifest,
                versionId,
                latest.Value<string>("version_number") ?? latest.Value<string>("name") ?? versionId,
                download,
                file?.Value<string>("filename") ?? Path.GetFileName(new Uri(download).LocalPath),
                PublishedAt(latest));
        }

        public async Task<string> BackupAsync(
            string instanceDirectory, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(instanceDirectory))
            {
                throw new DirectoryNotFoundException("实例目录不存在：" + instanceDirectory);
            }

            string root = Path.Combine(_paths.DataRoot, "modpack-backups");
            Directory.CreateDirectory(root);

            string name = Path.GetFileName(instanceDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string safe = string.IsNullOrWhiteSpace(name) ? "modpack" : name;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid, '-');
            }

            string destination = Path.Combine(
                root,
                safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip");

            string backup = await _packages.ExportAsync(instanceDirectory, destination, cancellationToken)
                .ConfigureAwait(false);
            ModpackManifest? manifest = ModpackManifestStore.Read(instanceDirectory);
            if (manifest != null)
            {
                manifest.LastBackup = backup;
                ModpackManifestStore.Write(instanceDirectory, manifest);
            }

            return backup;
        }

        private async Task<string> ResolveProjectIdAsync(
            string versionId, CancellationToken cancellationToken)
        {
            string url = ModrinthApi + "/version/" + Uri.EscapeDataString(versionId);
            JObject version = JObject.Parse(await _download.GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false));

            return version.Value<string>("project_id") ?? string.Empty;
        }

        private static DateTimeOffset? PublishedAt(JObject version)
        {
            string? raw = version.Value<string>("date_published");

            return DateTimeOffset.TryParse(
                raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                ? parsed
                : (DateTimeOffset?)null;
        }

        private static string? LoaderFilter(string? loader)
        {
            if (string.IsNullOrWhiteSpace(loader))
            {
                return null;
            }

            switch (loader.Trim().ToLowerInvariant())
            {
                case "fabric":
                case "quilt":
                case "forge":
                case "neoforge":
                    return loader.Trim().ToLowerInvariant();
                default:
                    return null;
            }
        }
    }
}

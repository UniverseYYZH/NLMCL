using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
        private readonly IFileDownloader? _files;

        public ModpackUpdateService(
            IDownloadService download,
            IInstancePackageService packages,
            IAppPaths paths,
            IFileDownloader? files = null)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _files = files;
        }

        public Task<ModpackManifest?> ReadAsync(string instanceDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ModpackManifestStore.Read(instanceDirectory));
        }

        public Task<ModpackUpdateInfo?> CheckAsync(
            string instanceDirectory, CancellationToken cancellationToken)
        {
            // PCL 的下载器禁止在 UI 线程发请求；VM 的 async 命令会从 UI 线程进入这里。
            return Task.Run(
                () => CheckCoreAsync(instanceDirectory, cancellationToken),
                cancellationToken);
        }

        private async Task<ModpackUpdateInfo?> CheckCoreAsync(
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
                manifest.ProjectId = await ResolveProjectIdAsync(manifest, cancellationToken)
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

            List<JObject> ordered = versions
                .OfType<JObject>()
                .Where(item => !string.IsNullOrWhiteSpace(item.Value<string>("id")))
                .OrderByDescending(PublishedAt)
                .ToList();

            // 导入的 .mrpack 里 versionId 是作者写的版本号（如 6.3.1），
            // 只有从 Modrinth 目录直接安装时才是平台 id；两种都要认。
            JObject? installed = ordered.FirstOrDefault(item =>
                string.Equals(item.Value<string>("id"), manifest.VersionId, StringComparison.Ordinal)
                || string.Equals(
                    item.Value<string>("version_number"), manifest.VersionId,
                    StringComparison.OrdinalIgnoreCase));
            DateTimeOffset? installedAt = installed == null ? (DateTimeOffset?)null : PublishedAt(installed);

            // 已是最新版本；不能跳过当前版本去拿列表里的旧版本。
            if (ordered.Count == 0
                || (installed != null && ReferenceEquals(ordered[0], installed)))
            {
                return null;
            }

            JObject? latest = null;
            foreach (JObject candidate in ordered)
            {
                string candidateId = candidate.Value<string>("id") ?? string.Empty;
                if (string.Equals(candidateId, manifest.VersionId, StringComparison.Ordinal)
                    || string.Equals(
                        candidate.Value<string>("version_number"), manifest.VersionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTimeOffset? candidateAt = PublishedAt(candidate);
                if (installedAt.HasValue && candidateAt.HasValue && candidateAt.Value <= installedAt.Value)
                {
                    // 不提供降级；列表里比当前更旧的版本直接跳过。
                    continue;
                }

                if (await IsLoaderCompatibleAsync(candidate, manifest, cancellationToken)
                    .ConfigureAwait(false))
                {
                    latest = candidate;
                    break;
                }
            }

            if (latest == null)
            {
                // 有新版但都需要升级加载器；返回 null，避免把必然失败的更新推给用户。
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

            string sha1 = file?.SelectToken("hashes.sha1")?.Value<string>() ?? string.Empty;

            return new ModpackUpdateInfo(
                manifest,
                versionId,
                latest.Value<string>("version_number") ?? latest.Value<string>("name") ?? versionId,
                download,
                file?.Value<string>("filename") ?? Path.GetFileName(new Uri(download).LocalPath),
                PublishedAt(latest),
                sha1);
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
            ModpackManifest manifest, CancellationToken cancellationToken)
        {
            try
            {
                string url = ModrinthApi + "/version/" + Uri.EscapeDataString(manifest.VersionId);
                JObject version = JObject.Parse(await _download.GetStringAsync(url, cancellationToken)
                    .ConfigureAwait(false));

                string projectId = version.Value<string>("project_id") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(projectId))
                {
                    return projectId;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 手动导入的 .mrpack 里 versionId 是版本号，不是平台 id；回退按名称搜索。
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                return string.Empty;
            }

            string searchUrl = ModrinthApi + "/search?query="
                + Uri.EscapeDataString(manifest.Name)
                + "&facets=" + Uri.EscapeDataString("[[\"project_type:modpack\"]]")
                + "&limit=10";
            try
            {
                JObject search = JObject.Parse(await _download.GetStringAsync(searchUrl, cancellationToken)
                    .ConfigureAwait(false));
                JArray? hits = search["hits"] as JArray;
                if (hits == null)
                {
                    return string.Empty;
                }

                foreach (JObject hit in hits.OfType<JObject>())
                {
                    string? title = hit.Value<string>("title");
                    string? slug = hit.Value<string>("slug");
                    if (string.Equals(title, manifest.Name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(slug, manifest.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return hit.Value<string>("project_id") ?? string.Empty;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        private static DateTimeOffset? PublishedAt(JObject version)
        {
            string? raw = version.Value<string>("date_published");

            return DateTimeOffset.TryParse(
                raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                ? parsed
                : (DateTimeOffset?)null;
        }

        private async Task<bool> IsLoaderCompatibleAsync(
            JObject version, ModpackManifest manifest, CancellationToken cancellationToken)
        {
            if (_files == null || string.IsNullOrWhiteSpace(manifest.LoaderVersion))
            {
                return true;
            }

            string? dependency = LoaderDependencyKey(manifest.Loader);
            if (dependency == null)
            {
                return true;
            }

            JObject? file = (version["files"] as JArray)?
                .OfType<JObject>()
                .FirstOrDefault(item => item.Value<bool?>("primary") == true)
                ?? (version["files"] as JArray)?.OfType<JObject>().FirstOrDefault();
            string url = file?.Value<string>("url") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // 启动器可能运行在低完整性/受控目录环境，写不了 %TEMP%；
            // 候选包放到自己的 DataRoot 下，和正式更新下载同一个可写位置。
            string temporaryRoot = Path.Combine(_paths.DataRoot, "modpack-downloads");
            Directory.CreateDirectory(temporaryRoot);
            string temporary = Path.Combine(
                temporaryRoot, "check-" + Guid.NewGuid().ToString("N") + ".mrpack");
            try
            {
                await _files.DownloadAsync(url, temporary, cancellationToken).ConfigureAwait(false);

                using (ZipArchive archive = ZipFile.OpenRead(temporary))
                {
                    ZipArchiveEntry? entry = archive.GetEntry("modrinth.index.json");
                    if (entry == null)
                    {
                        return false;
                    }

                    using (StreamReader reader = new StreamReader(entry.Open()))
                    {
                        JObject index = JObject.Parse(reader.ReadToEnd());
                        string? required = index.SelectToken("dependencies." + dependency)?.Value<string>();
                        return string.IsNullOrWhiteSpace(required)
                            || string.Equals(required, manifest.LoaderVersion, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 候选包无法下载/解析时跳过它，不阻断其余候选检查。
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        private static string? LoaderDependencyKey(string? loader)
        {
            if (string.IsNullOrWhiteSpace(loader))
            {
                return null;
            }

            switch (loader.Trim().ToLowerInvariant())
            {
                case "fabric":
                    return "fabric-loader";
                case "quilt":
                    return "quilt-loader";
                case "forge":
                    return "forge";
                case "neoforge":
                    return "neoforge";
                default:
                    return null;
            }
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

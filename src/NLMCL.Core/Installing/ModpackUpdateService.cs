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
        private const string CurseForgeApi = "https://mod.mcimirror.top/curseforge/v1";

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

        public async Task<ModpackUpdateInfo?> CheckAsync(
            string instanceDirectory, CancellationToken cancellationToken)
        {
            ModpackUpdateCheckResult result = await CheckDetailedAsync(
                instanceDirectory, cancellationToken).ConfigureAwait(false);
            return result.Info;
        }

        public Task<ModpackUpdateCheckResult> CheckDetailedAsync(
            string instanceDirectory, CancellationToken cancellationToken)
        {
            // PCL 的下载器禁止在 UI 线程发请求；VM 的 async 命令会从 UI 线程进入这里。
            return Task.Run(
                () => CheckDetailedCoreAsync(instanceDirectory, cancellationToken),
                cancellationToken);
        }

        private async Task<ModpackUpdateCheckResult> CheckDetailedCoreAsync(
            string instanceDirectory, CancellationToken cancellationToken)
        {
            try
            {
                ModpackManifest? manifest = ModpackManifestStore.Read(instanceDirectory);

                if (manifest == null)
                {
                    return ModpackUpdateCheckResult.Unsupported("缺少整合包元数据，暂不支持检查。");
                }

                if (string.Equals(manifest.Source, "curseforge", StringComparison.OrdinalIgnoreCase))
                {
                    return await CheckCurseForgeAsync(manifest, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!string.Equals(manifest.Source, "modrinth", StringComparison.OrdinalIgnoreCase))
                {
                    return ModpackUpdateCheckResult.Unsupported("这个整合包来源暂不支持检查更新。");
                }

                ModpackUpdateInfo? update = await CheckCoreAsync(instanceDirectory, cancellationToken)
                    .ConfigureAwait(false);
                return update == null
                    ? ModpackUpdateCheckResult.UpToDate()
                    : ModpackUpdateCheckResult.Available(update);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 网络失败/限流/坏 JSON 一律降级为“暂不支持”，不能阻塞启动。
                return ModpackUpdateCheckResult.Unsupported(
                    "暂不支持检查更新（" + ex.Message + "）。");
            }
        }

        private async Task<ModpackUpdateCheckResult> CheckCurseForgeAsync(
            ModpackManifest manifest, CancellationToken cancellationToken)
        {
            long? projectId = manifest.CurseForgeProjectId;
            long? fileId = manifest.CurseForgeFileId;

            if (!projectId.HasValue || projectId.Value <= 0
                || !fileId.HasValue || fileId.Value <= 0)
            {
                return ModpackUpdateCheckResult.Unsupported(
                    "CurseForge 导入包未携带项目/文件 ID，暂不支持检查更新。");
            }

            string url = CurseForgeApi + "/mods/" + projectId.Value.ToString(CultureInfo.InvariantCulture)
                + "/files?pageSize=50&index=0";
            JObject root = JObject.Parse(await _download.GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false));

            JArray? files = root["data"] as JArray;
            JObject? latest = files?
                .OfType<JObject>()
                .Where(item => (item.Value<long?>("id") ?? 0) > 0)
                .OrderByDescending(item => PublishedAt(item))
                .ThenByDescending(item => item.Value<long?>("id") ?? 0)
                .FirstOrDefault();

            long latestId = latest?.Value<long?>("id") ?? 0;
            if (latest == null || latestId <= 0)
            {
                return ModpackUpdateCheckResult.Unsupported("CurseForge 没有返回可用的文件列表。");
            }

            if (latestId <= fileId.Value)
            {
                return ModpackUpdateCheckResult.UpToDate();
            }

            string download = latest.Value<string>("downloadUrl") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(download))
            {
                return ModpackUpdateCheckResult.Unsupported(
                    "CurseForge 新版没有提供可展示的下载地址。");
            }

            string sha1 = (latest["hashes"] as JArray)?
                .OfType<JObject>()
                .FirstOrDefault(item => item.Value<int?>("algo") == 1)?
                .Value<string>("value") ?? string.Empty;

            string fileName = latest.Value<string>("fileName") ?? string.Empty;
            string versionName = latest.Value<string>("displayName") ?? fileName;

            return ModpackUpdateCheckResult.Available(new ModpackUpdateInfo(
                manifest,
                latestId.ToString(CultureInfo.InvariantCulture),
                versionName,
                download,
                fileName,
                PublishedAt(latest),
                sha1,
                "curseforge"));
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

                // Fabric 的加载器版本可以在 UpdateAsync 中原地升级；无需为了判断
                // 兼容性下载整个 .mrpack。这样既避免大包超时导致误判“无更新”，
                // 也让 6.5.0 这类只升级 Fabric 的版本能够被正确发现。
                bool fabricCandidate = string.Equals(manifest.Loader, "Fabric", StringComparison.OrdinalIgnoreCase)
                    && (candidate["loaders"] as JArray)?.Any(loader =>
                        string.Equals(loader.Value<string>(), "fabric", StringComparison.OrdinalIgnoreCase)) == true;

                if (fabricCandidate
                    || await IsLoaderCompatibleAsync(candidate, manifest, cancellationToken)
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
            string? raw = version.Value<string>("date_published")
                ?? version.Value<string>("fileDate");

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
                        if (string.IsNullOrWhiteSpace(required))
                        {
                            return true;
                        }

                        // Fabric 已支持原地升级加载器；允许同版本或更高版本，
                        // 但不把降级包装成“更新”。Forge/NeoForge 仍要求完全一致。
                        if (string.Equals(dependency, "fabric-loader", StringComparison.OrdinalIgnoreCase)
                            && Version.TryParse(required, out Version? wanted)
                            && Version.TryParse(manifest.LoaderVersion, out Version? installed))
                        {
                            return wanted >= installed;
                        }

                        return string.Equals(required, manifest.LoaderVersion, StringComparison.OrdinalIgnoreCase);
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

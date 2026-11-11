using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using MeloongCore;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Modrinth 整合包（.mrpack）导入。
    ///
    /// 格式：zip，根目录 <c>modrinth.index.json</c>：
    /// <code>
    /// {
    ///   "name": "...", "versionId": "...",
    ///   "files": [ { "path": "mods/x.jar", "downloads": ["https://..."], "hashes": {"sha1": "..."} } ],
    ///   "dependencies": { "minecraft": "1.20.1", "fabric-loader": "0.16.9" }
    /// }
    /// </code>
    /// 外加可选的 <c>overrides/</c> 目录，整体覆盖到实例根。
    ///
    /// 落点策略：**新建一个隔离实例** <c>versions/&lt;包名&gt;/</c>，
    /// 它的版本 JSON 用 <c>inheritsFrom</c> 指向已安装的加载器版本；
    /// 包里的 mods/config 直接放进实例目录，隔离实例会优先使用它们。
    /// </summary>
    public sealed class ModpackInstaller : IModpackInstaller
    {
        private const string IndexEntryName = "modrinth.index.json";
        private const string CurseForgeManifestName = "manifest.json";
        private const string OverridesPrefix = "overrides/";
        private const string CurseForgeApiBase = "https://mod.mcimirror.top/curseforge/v1";

        private readonly IDownloadService _download;
        private readonly IVersionInstaller _versions;
        private readonly ILoaderInstaller _loaders;
        private readonly IFileDownloader _files;

        /// <summary>并发下载的文件数（整合包文件多，串行会慢到不可用）。</summary>
        public int Parallelism { get; set; } = 12;

        public ModpackInstaller(
            IDownloadService download,
            IVersionInstaller versions,
            ILoaderInstaller loaders,
            IFileDownloader? files = null)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _versions = versions ?? throw new ArgumentNullException(nameof(versions));
            _loaders = loaders ?? throw new ArgumentNullException(nameof(loaders));

            // 整合包文件用流式下载器：ModNet 的"小文件直下"会弄坏带 %2B 的 URL，且整包进内存
            _files = files ?? new HttpFileDownloader();
        }

        public Task<ModpackInfo> ReadAsync(string mrpackPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(mrpackPath))
            {
                throw new FileNotFoundException("找不到整合包文件。", mrpackPath);
            }

            using (ZipArchive archive = ZipFile.OpenRead(mrpackPath))
            {
                JObject index = ReadIndex(archive);
                return Task.FromResult(Parse(index));
            }
        }

        public async Task<string> InstallAsync(
            string mrpackPath,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(mrpackPath))
            {
                throw new FileNotFoundException("找不到整合包文件。", mrpackPath);
            }

            using ZipArchive archive = ZipFile.OpenRead(mrpackPath);

            JObject index = ReadIndex(archive);
            ModpackInfo info = Parse(index);

            progress?.Report(new InstallProgress("准备整合包：" + info.Name, 2, 0));

            string loaderVersionId = await EnsureLoaderAsync(
                info, minecraftDirectory, javaExecutable, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new InstallProgress("创建实例目录", 30, 0));

            string versionId = CreateVersionDirectory(minecraftDirectory, info, loaderVersionId);

            // 2) 下载包内文件。CurseForge 的 manifest 只有 projectID/fileID，
            //    先换直链再下载到 mods；Modrinth 走文件清单。
            string instanceDirectory = Path.Combine(minecraftDirectory, "versions", versionId);

            try
            {
                if (IsCurseForgeManifest(index))
                {
                    await DownloadCurseForgeFilesAsync(
                        index, instanceDirectory, progress, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DownloadModrinthFilesAsync(
                        index, instanceDirectory, progress, cancellationToken).ConfigureAwait(false);
                }

                // 3) overrides：整体铺到实例目录
                progress?.Report(new InstallProgress("展开 overrides", 88, 0));
                string overridesPrefix = (index.Value<string>("overrides") ?? "overrides").TrimEnd('/') + "/";
                ExtractOverrides(archive, instanceDirectory, overridesPrefix);
            }
            catch
            {
                // 下载/解压失败时不能留下只有 JSON 的空壳版本：
                // 用户会在世界库里看到一个永远启动不了、也无法增量修复的实例。
                TryDeleteDirectory(instanceDirectory);
                throw;
            }

            progress?.Report(new InstallProgress("整合包安装完成", 100, 0));

            return versionId;
        }

        private async Task DownloadModrinthFilesAsync(
            JObject index,
            string instanceDirectory,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<JToken> files = (index["files"] as JArray)?.ToList() ?? new List<JToken>();

            int total = 0;
            int completed = 0;
            long totalBytes = files
                .Where(item => item.Value<long?>("fileSize") > 0)
                .Sum(item => item.Value<long>("fileSize"));
            long completedBytes = 0;
            List<string> failures = new List<string>();
            object gate = new object();

            using (SemaphoreSlim slots = new SemaphoreSlim(Parallelism))
            {
                List<Task> tasks = new List<Task>();

                foreach (JToken file in files)
                {
                    if (string.Equals(file.SelectToken("env.client")?.Value<string>(), "unsupported",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? relative = file.Value<string>("path");
                    string? url = (file["downloads"] as JArray)?.FirstOrDefault()?.Value<string>();

                    if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    total++;

                    string captured = relative!;
                    string capturedUrl = url!;
                    string? expectedSha1 = file.SelectToken("hashes.sha1")?.Value<string>();
                    long expectedSize = file.Value<long?>("fileSize") ?? 0;
                    long[] currentBytes = new long[1];

                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string target = SafeCombine(instanceDirectory, captured);

                            if (!IsFileUpToDate(target, expectedSha1, expectedSize > 0 ? expectedSize : null))
                            {
                                IProgress<double>? fileProgress = expectedSize > 0
                                    ? new InlineRatioProgress(ratio =>
                                    {
                                        long current = (long)(ratio * expectedSize);
                                        Interlocked.Exchange(ref currentBytes[0], current);
                                        long received = Interlocked.Read(ref completedBytes) + current;
                                        int bytePercent = totalBytes > 0
                                            ? 30 + (int)(55.0 * received / totalBytes)
                                            : 30;
                                        ReportBytes(progress, "下载 " + captured, bytePercent, received, totalBytes);
                                    })
                                    : null;

                                await _files.DownloadAsync(capturedUrl, target, fileProgress, cancellationToken)
                                    .ConfigureAwait(false);

                                if (!string.IsNullOrEmpty(expectedSha1))
                                {
                                    string actual = CryptographyUtils.ComputeFileHash(
                                        target, CryptographyUtils.HashMethod.Sha1);

                                    if (!string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
                                    {
                                        throw new InvalidDataException("校验失败：" + captured);
                                    }
                                }

                                if (expectedSize > 0)
                                {
                                    Interlocked.Add(ref completedBytes, expectedSize);
                                }
                            }
                            else if (expectedSize > 0)
                            {
                                Interlocked.Add(ref completedBytes, expectedSize);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lock (gate)
                            {
                                failures.Add(captured + "（" + ex.Message + "）");
                            }
                        }
                        finally
                        {
                            int done;
                            lock (gate)
                            {
                                done = ++completed;
                            }

                            int percent = 30 + (int)(55.0 * done / Math.Max(1, total));
                            progress?.Report(new InstallProgress(
                                "下载包内文件 " + done + "/" + total,
                                percent,
                                0,
                                Interlocked.Read(ref completedBytes),
                                totalBytes));

                            slots.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            ThrowIfFailures(failures);
        }

        /// <summary>CurseForge 整合包：manifest 只有 projectID/fileID，按 mcim 接口换直链后下载到 mods。</summary>
        private async Task DownloadCurseForgeFilesAsync(
            JObject manifest,
            string instanceDirectory,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<JToken> files = (manifest["files"] as JArray)?.ToList() ?? new List<JToken>();

            int total = 0;
            int completed = 0;
            long totalBytes = 0;
            long completedBytes = 0;
            List<string> failures = new List<string>();
            object gate = new object();
            ConcurrentDictionary<long, string> folders = new ConcurrentDictionary<long, string>();

            using (SemaphoreSlim slots = new SemaphoreSlim(Parallelism))
            {
                List<Task> tasks = new List<Task>();

                foreach (JToken file in files)
                {
                    if (file.Value<bool?>("required") == false)
                    {
                        continue;
                    }

                    long? projectId = file.Value<long?>("projectID");
                    long? fileId = file.Value<long?>("fileID");

                    if (!projectId.HasValue || !fileId.HasValue)
                    {
                        continue;
                    }

                    total++;

                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

                    tasks.Add(Task.Run(async () =>
                    {
                        string name = "project " + projectId.Value + "/file " + fileId.Value;

                        try
                        {
                            string json = await _download.GetStringAsync(
                                CurseForgeApiBase + "/mods/" + projectId.Value
                                + "/files/" + fileId.Value,
                                cancellationToken).ConfigureAwait(false);

                            JObject? data = JObject.Parse(json)["data"] as JObject;

                            if (data == null)
                            {
                                throw new InvalidDataException("CurseForge 没有返回文件信息。");
                            }

                            string? url = data.Value<string>("downloadUrl");
                            string? fileName = data.Value<string>("fileName");

                            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(fileName))
                            {
                                throw new InvalidDataException("文件没有直链（可能被作者禁用分发）。");
                            }

                            name = Path.GetFileName(fileName!);
                            string folder = await ResolveCurseForgeFolderAsync(
                                projectId.Value, folders, cancellationToken).ConfigureAwait(false);
                            string target = SafeCombine(instanceDirectory, folder + "/" + name);
                            long size = data.Value<long?>("fileLength") ?? 0;
                            string? sha1 = data.SelectToken("hashes.sha1")?.Value<string>();

                            if (size > 0)
                            {
                                Interlocked.Add(ref totalBytes, size);
                            }

                            if (!IsFileUpToDate(target, sha1, size > 0 ? size : null))
                            {
                                if (size > 0)
                                {
                                    long[] currentBytes = new long[1];
                                    IProgress<double> fileProgress = new InlineRatioProgress(ratio =>
                                    {
                                        long current = (long)(ratio * size);
                                        Interlocked.Exchange(ref currentBytes[0], current);
                                        long received = Interlocked.Read(ref completedBytes) + current;
                                        int bytePercent = totalBytes > 0
                                            ? 30 + (int)(55.0 * received / totalBytes)
                                            : 30;
                                        ReportBytes(progress, "下载 CurseForge " + name, bytePercent, received, totalBytes);
                                    });

                                    await _files.DownloadAsync(url!, target, fileProgress, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                else
                                {
                                    await _files.DownloadAsync(url!, target, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                            }

                            if (size > 0)
                            {
                                Interlocked.Add(ref completedBytes, size);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lock (gate)
                            {
                                failures.Add(name + "：" + ex.Message);
                            }
                        }
                        finally
                        {
                            int done;
                            lock (gate)
                            {
                                done = ++completed;
                            }

                            int percent = 30 + (int)(55.0 * done / Math.Max(1, total));
                            progress?.Report(new InstallProgress(
                                "下载 CurseForge 文件 " + done + "/" + total,
                                percent,
                                0,
                                Interlocked.Read(ref completedBytes),
                                Interlocked.Read(ref totalBytes)));

                            slots.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            ThrowIfFailures(failures);
        }

        private async Task<string> ResolveCurseForgeFolderAsync(
            long projectId,
            ConcurrentDictionary<long, string> cache,
            CancellationToken cancellationToken)
        {
            if (cache.TryGetValue(projectId, out string? cached))
            {
                return cached;
            }

            string folder = "mods";

            try
            {
                string text = await _download.GetStringAsync(
                    CurseForgeApiBase + "/mods/" + projectId,
                    cancellationToken).ConfigureAwait(false);

                int? classId = JObject.Parse(text)["data"]?.Value<int?>("classId");

                switch (classId)
                {
                    case 12:
                        folder = "resourcepacks";
                        break;
                    case 6552:
                        folder = "shaderpacks";
                        break;
                    case 6945:
                        folder = "datapacks";
                        break;
                }
            }
            catch (Exception)
            {
                // 项目信息拿不到时退回 mods，不影响安装。
            }

            cache[projectId] = folder;
            return folder;
        }

        private static void ReportBytes(
            IProgress<InstallProgress>? progress,
            string stage,
            int percent,
            long received,
            long total)
        {
            progress?.Report(new InstallProgress(stage, percent, 0, received, total));
        }

        private sealed class InlineRatioProgress : IProgress<double>
        {
            private readonly Action<double> _apply;

            public InlineRatioProgress(Action<double> apply)
            {
                _apply = apply;
            }

            public void Report(double value) => _apply(value);
        }

        private static void ThrowIfFailures(List<string> failures)
        {
            if (failures.Count == 0)
            {
                return;
            }

            string sample = string.Join("\n", failures.Take(5));
            throw new InvalidOperationException(
                "有 " + failures.Count + " 个文件下载失败（已下载的文件会保留，重试会跳过）：\n" + sample);
        }

        /// <summary>按依赖把加载器装好，返回加载器版本目录 id。</summary>
        private async Task<string> EnsureLoaderAsync(
            ModpackInfo info,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            switch (info.Loader)
            {
                case ModpackLoader.Fabric:
                    progress?.Report(new InstallProgress("安装 Fabric " + info.LoaderVersion, 8, 0));

                    await _versions.InstallAsync(
                        new InstallRequest(minecraftDirectory, info.MinecraftVersion)
                        {
                            Loader = "fabric",
                            LoaderVersion = info.LoaderVersion,
                            Parallelism = 8
                        },
                        progress ?? new Progress<InstallProgress>(),
                        cancellationToken).ConfigureAwait(false);

                    return ResolveFabricVersionId(minecraftDirectory, info.MinecraftVersion, info.LoaderVersion!);

                case ModpackLoader.Forge:
                    progress?.Report(new InstallProgress("安装 Forge " + info.LoaderVersion, 8, 0));

                    return await _loaders.InstallForgeAsync(
                        info.MinecraftVersion, info.LoaderVersion!, minecraftDirectory, javaExecutable,
                        progress, cancellationToken).ConfigureAwait(false);

                case ModpackLoader.NeoForge:
                    progress?.Report(new InstallProgress("安装 NeoForge " + info.LoaderVersion, 8, 0));

                    return await _loaders.InstallNeoForgeAsync(
                        info.MinecraftVersion, info.LoaderVersion!, minecraftDirectory, javaExecutable,
                        progress, cancellationToken).ConfigureAwait(false);

                default:
                    progress?.Report(new InstallProgress("安装原版 " + info.MinecraftVersion, 8, 0));

                    await _versions.InstallAsync(
                        new InstallRequest(minecraftDirectory, info.MinecraftVersion),
                        progress ?? new Progress<InstallProgress>(),
                        cancellationToken).ConfigureAwait(false);

                    return info.MinecraftVersion;
            }
        }

        /// <summary>
        /// Fabric 安装器生成的版本目录名是 <c>fabric-loader-{loader}-{mc}</c>（PCL 的约定），
        /// 不是 <c>{mc}-fabric-{loader}</c>。装完先按约定找，找不到再扫一遍版本 JSON 兜底。
        /// </summary>
        internal static string ResolveFabricVersionId(string minecraftDirectory, string minecraftVersion, string loaderVersion)
        {
            string conventional = "fabric-loader-" + loaderVersion + "-" + minecraftVersion;
            string versions = Path.Combine(minecraftDirectory, "versions");

            if (File.Exists(Path.Combine(versions, conventional, conventional + ".json")))
            {
                return conventional;
            }

            if (Directory.Exists(versions))
            {
                foreach (string directory in Directory.GetDirectories(versions))
                {
                    string name = Path.GetFileName(directory);
                    string jsonPath = Path.Combine(directory, name + ".json");

                    if (!File.Exists(jsonPath))
                    {
                        continue;
                    }

                    try
                    {
                        JObject json = JObject.Parse(File.ReadAllText(jsonPath));

                        if (string.Equals(json.Value<string>("inheritsFrom"), minecraftVersion, StringComparison.OrdinalIgnoreCase)
                            && (json.Value<string>("id") ?? name).IndexOf("fabric", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return name;
                        }
                    }
                    catch (Exception)
                    {
                        // 坏 JSON 跳过
                    }
                }
            }

            return conventional;
        }

        /// <summary>建实例目录并写一个 inheritsFrom 加载器的版本 JSON。</summary>
        private static string CreateVersionDirectory(
            string minecraftDirectory, ModpackInfo info, string loaderVersionId)
        {
            string baseId = Sanitize(info.Name);
            string versionId = baseId;
            int suffix = 2;

            while (Directory.Exists(Path.Combine(minecraftDirectory, "versions", versionId)))
            {
                versionId = baseId + "-" + suffix;
                suffix++;
            }

            string directory = Path.Combine(minecraftDirectory, "versions", versionId);
            Directory.CreateDirectory(directory);

            // 优先复制加载器版本 JSON 再改 id：
            // 只写一个 inheritsFrom 的极简 JSON，PCL 的版本合并拿不到 libraries/mainClass 会启动失败。
            JObject json = CloneLoaderVersionJson(minecraftDirectory, loaderVersionId, versionId)
                ?? new JObject
                {
                    ["id"] = versionId,
                    ["inheritsFrom"] = loaderVersionId,
                    ["type"] = "release"
                };

            json["id"] = versionId;
            json["modpack"] = info.Name;
            json["time"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
            json["releaseTime"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);

            File.WriteAllText(
                Path.Combine(directory, versionId + ".json"),
                json.ToString(),
                new UTF8Encoding(false));

            return versionId;
        }

        private static JObject ReadIndex(ZipArchive archive)
        {
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                item => string.Equals(item.FullName, IndexEntryName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                entry = archive.Entries.FirstOrDefault(
                    item => string.Equals(item.FullName, CurseForgeManifestName, StringComparison.OrdinalIgnoreCase));
            }

            if (entry == null)
            {
                throw new InvalidDataException(
                    "不是有效的整合包：缺少 modrinth.index.json 或 CurseForge manifest.json。");
            }

            using (Stream stream = entry.Open())
            using (StreamReader reader = new StreamReader(stream))
            {
                string text = reader.ReadToEnd();

                try
                {
                    return JObject.Parse(text);
                }
                catch (Exception)
                {
                    throw new InvalidDataException("整合包索引不是合法 JSON。");
                }
            }
        }

        internal static bool IsCurseForgeManifest(JObject index)
        {
            string type = index.Value<string>("manifestType") ?? string.Empty;

            if (type.IndexOf("curse", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            JObject? minecraft = index["minecraft"] as JObject;
            return minecraft?["modLoaders"] is JArray
                && (index["files"] as JArray)?.FirstOrDefault()?["projectID"] != null;
        }

        /// <summary>把加载器版本的完整 JSON 复制一份作为整合包实例（改 id，保留 librar* 与 mainClass）。</summary>
        internal static JObject? CloneLoaderVersionJson(string minecraftDirectory, string loaderVersionId, string newId)
        {
            string path = Path.Combine(
                minecraftDirectory, "versions", loaderVersionId, loaderVersionId + ".json");

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                JObject json = JObject.Parse(File.ReadAllText(path));

                // 复制出来的 JSON 直接以原版为父；不能再指向加载器自己（会形成二级继承）
                string? parent = json.Value<string>("inheritsFrom");

                if (string.IsNullOrEmpty(parent))
                {
                    json.Remove("inheritsFrom");
                }

                return json;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static ModpackInfo Parse(JObject index)
        {
            if (IsCurseForgeManifest(index))
            {
                return ParseCurseForge(index);
            }

            string name = index.Value<string>("name") ?? "未命名整合包";
            string versionId = index.Value<string>("versionId") ?? string.Empty;

            JObject? dependencies = index["dependencies"] as JObject;

            string? minecraft = dependencies?.Value<string>("minecraft");

            if (string.IsNullOrWhiteSpace(minecraft))
            {
                throw new InvalidDataException("整合包没有声明 Minecraft 版本。");
            }

            ModpackLoader loader = ModpackLoader.Vanilla;
            string? loaderVersion = null;

            if (dependencies!["fabric-loader"] != null)
            {
                loader = ModpackLoader.Fabric;
                loaderVersion = dependencies.Value<string>("fabric-loader");
            }
            else if (dependencies["forge"] != null)
            {
                loader = ModpackLoader.Forge;
                loaderVersion = dependencies.Value<string>("forge");
            }
            else if (dependencies["neoforge"] != null)
            {
                loader = ModpackLoader.NeoForge;
                loaderVersion = dependencies.Value<string>("neoforge");
            }
            else if (dependencies["quilt-loader"] != null)
            {
                throw new NotSupportedException("暂不支持 Quilt 整合包。");
            }

            int files = (index["files"] as JArray)?.Count ?? 0;

            return new ModpackInfo(name, versionId, minecraft!, loader, loaderVersion, files);
        }

        internal static ModpackInfo ParseCurseForge(JObject manifest)
        {
            string name = manifest.Value<string>("name") ?? "未命名整合包";
            string versionId = manifest.Value<string>("version") ?? string.Empty;

            JObject? minecraft = manifest["minecraft"] as JObject;
            string? minecraftVersion = minecraft?.Value<string>("version");

            if (string.IsNullOrWhiteSpace(minecraftVersion))
            {
                throw new InvalidDataException("CurseForge 整合包没有声明 Minecraft 版本。");
            }

            JArray? loaders = minecraft?["modLoaders"] as JArray;

            JObject? primary = loaders?
                .OfType<JObject>()
                .FirstOrDefault(item => item.Value<bool?>("primary") == true)
                ?? loaders?.OfType<JObject>().FirstOrDefault();

            string loaderId = primary?.Value<string>("id") ?? "vanilla";

            ModpackLoader loader;
            string? loaderVersion;

            if (loaderId.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase))
            {
                loader = ModpackLoader.Fabric;
                loaderVersion = loaderId.Substring("fabric-".Length);
            }
            else if (loaderId.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
            {
                loader = ModpackLoader.Forge;
                loaderVersion = loaderId.Substring("forge-".Length);
            }
            else if (loaderId.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
            {
                loader = ModpackLoader.NeoForge;
                loaderVersion = loaderId.Substring("neoforge-".Length);
            }
            else if (loaderId.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("暂不支持 Quilt 整合包。");
            }
            else
            {
                loader = ModpackLoader.Vanilla;
                loaderVersion = null;
            }

            int files = (manifest["files"] as JArray)?.Count ?? 0;

            return new ModpackInfo(name, versionId, minecraftVersion!, loader, loaderVersion, files);
        }

        /// <summary>已存在且 sha1 匹配就跳过（重试/断点续传）。</summary>
        internal static bool IsFileUpToDate(string path, string? expectedSha1, long? expectedSize = null)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            long actualSize = new FileInfo(path).Length;

            if (actualSize == 0)
            {
                return false;
            }

            // 清单里有文件大小就先比大小：重试时不必把几十 MB 的文件重新算一遍 sha1
            if (expectedSize.HasValue && expectedSize.Value > 0)
            {
                return actualSize == expectedSize.Value;
            }

            if (string.IsNullOrEmpty(expectedSha1))
            {
                return true;
            }

            try
            {
                string actual = CryptographyUtils.ComputeFileHash(path, CryptographyUtils.HashMethod.Sha1);
                return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>把 overrides/ 下的内容铺到实例目录（挡住路径穿越）。</summary>
        internal static void ExtractOverrides(
            ZipArchive archive, string targetDirectory, string overridesPrefix = OverridesPrefix)
        {
            string prefix = string.IsNullOrEmpty(overridesPrefix) ? OverridesPrefix : overridesPrefix;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = entry.FullName.Substring(prefix.Length).Replace('\\', '/');

                if (relative.Length == 0 || relative.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                string target = SafeCombine(targetDirectory, relative);
                string? parent = Path.GetDirectoryName(target);

                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent!);
                }

                entry.ExtractToFile(target, overwrite: true);
            }
        }

        /// <summary>拼接并挡住 ../ 逃逸：结果必须仍在 root 下。</summary>
        internal static string SafeCombine(string root, string relative)
        {
            string normalizedRoot = Path.GetFullPath(root);
            string combined = Path.GetFullPath(Path.Combine(normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!combined.StartsWith(
                    normalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("整合包里有非法路径：" + relative);
            }

            string? parent = Path.GetDirectoryName(combined);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent!);
            }

            return combined;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string Sanitize(string name)
        {
            StringBuilder builder = new StringBuilder();

            foreach (char ch in name.Trim())
            {
                char value = char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.' ? ch : '-';
                builder.Append(value);
            }

            string result = builder.ToString().Trim('-', '.');

            if (result.Length == 0)
            {
                result = "modpack";
            }

            return result.Length > 60 ? result.Substring(0, 60) : result;
        }
    }
}

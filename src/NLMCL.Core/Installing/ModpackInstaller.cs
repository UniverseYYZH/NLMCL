using System;
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
        private const string OverridesPrefix = "overrides/";

        private readonly IDownloadService _download;
        private readonly IVersionInstaller _versions;
        private readonly ILoaderInstaller _loaders;

        /// <summary>并发下载的文件数（整合包文件多，串行会慢到不可用）。</summary>
        public int Parallelism { get; set; } = 8;

        public ModpackInstaller(
            IDownloadService download, IVersionInstaller versions, ILoaderInstaller loaders)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _versions = versions ?? throw new ArgumentNullException(nameof(versions));
            _loaders = loaders ?? throw new ArgumentNullException(nameof(loaders));
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

            // 2) 下载包内文件（mods / config / resourcepacks …）
            //    并发 + 停滞检测 + 已有文件跳过：整合包动辄 200+ 文件，
            //    串行直连 cdn.modrinth.com 又慢又容易整体失败。
            IReadOnlyList<JToken> files = (index["files"] as JArray)?.ToList() ?? new List<JToken>();
            string instanceDirectory = Path.Combine(minecraftDirectory, "versions", versionId);

            int total = 0;
            int completed = 0;
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

                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string target = SafeCombine(instanceDirectory, captured);

                            if (!IsFileUpToDate(target, expectedSha1))
                            {
                                await ResilientDownload
                                    .DownloadAsync(_download, capturedUrl, target, cancellationToken, attempts: 3)
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
                                "下载包内文件 " + done + "/" + total, percent, 0));

                            slots.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            if (failures.Count > 0)
            {
                string sample = string.Join("\n", failures.Take(5));
                throw new InvalidOperationException(
                    "有 " + failures.Count + " 个文件下载失败（已下载的文件会保留，重试会跳过）：\n" + sample);
            }

            // 3) overrides：整体铺到实例目录
            progress?.Report(new InstallProgress("展开 overrides", 88, 0));
            ExtractOverrides(archive, Path.Combine(minecraftDirectory, "versions", versionId));

            progress?.Report(new InstallProgress("整合包安装完成", 100, 0));

            return versionId;
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

            JObject json = new JObject
            {
                ["id"] = versionId,
                ["inheritsFrom"] = loaderVersionId,
                ["type"] = "release",
                ["time"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                ["releaseTime"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                ["modpack"] = info.Name
            };

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
                throw new InvalidDataException("不是有效的 Modrinth 整合包：缺少 modrinth.index.json。");
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

        internal static ModpackInfo Parse(JObject index)
        {
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

        /// <summary>已存在且 sha1 匹配就跳过（重试/断点续传）。</summary>
        internal static bool IsFileUpToDate(string path, string? expectedSha1)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                return false;
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
        internal static void ExtractOverrides(ZipArchive archive, string targetDirectory)
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(OverridesPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = entry.FullName.Substring(OverridesPrefix.Length).Replace('\\', '/');

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

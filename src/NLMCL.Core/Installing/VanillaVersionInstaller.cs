using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 原版版本的安装编排器。
    ///
    /// 这是 §2.1a 批准的唯一豁免点：PCL 的等价实现无法从 UI 中分离（见 `docs/M3-NOTES.md` §8–§9）。
    /// 但**底层能力全部复用**：
    /// <list type="bullet">
    ///   <item>HTTP / 镜像 / 重试 → <see cref="IDownloadService"/>（运行时是 PCL 的 `ModNet`）</item>
    ///   <item>哈希校验 → MeloongCore 的 `CryptographyUtils`</item>
    ///   <item>版本识别 / 版本模型 → `McVersion` / `McInstance`（由版本目录负责）</item>
    /// </list>
    /// 本类只做：枚举文件、串行下载、校验、落盘、解压 natives、上报进度。
    /// </summary>
    public sealed class VanillaVersionInstaller : IVersionInstaller
    {
        private readonly IDownloadService _download;
        private readonly IFileDownloader _files;
        private readonly InstallOptions _options;

        public VanillaVersionInstaller(IDownloadService download)
            : this(download, new InstallOptions())
        {
        }

        public VanillaVersionInstaller(
            IDownloadService download, InstallOptions options, IFileDownloader? files = null)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // 备用下载器：ModNet 的小文件直下在部分网络会失败，用它兜底
            _files = files ?? new HttpFileDownloader();
        }

        public async Task InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!string.IsNullOrEmpty(request.Loader))
            {
                // 加载器安装走 PCL 的 ModLoader.vb（已 vendor），但要等 Launcher 侧接入；
                // 这里不假装支持，避免静默装出一个不能用的版本。
                throw new NotSupportedException(
                    "M3-B 只支持原版安装；加载器（" + request.Loader + "）安装尚未接入。");
            }

            ProgressRelay relay = new ProgressRelay(progress);

            // 1–16 之间，默认 8；太小会退化成串行，太大容易被服务器限速
            int parallelism = Math.Max(1, Math.Min(16, request.Parallelism));

            // 1. 版本清单 → 该版本的 JSON 地址
            relay.Report("读取版本清单", 1);
            string manifest = await GetStringWithFallbackAsync(_options.ManifestUrl, cancellationToken).ConfigureAwait(false);
            string versionUrl = ResolveVersionUrl(manifest, request.VersionId);

            // 2. 版本 JSON
            relay.Report("下载版本定义", 3);
            string jsonText = await GetStringWithFallbackAsync(versionUrl, cancellationToken).ConfigureAwait(false);

            VersionInstallPlan plan = VersionPlanBuilder.Build(
                request.VersionId, jsonText, request.MinecraftDirectory, _options);

            Directory.CreateDirectory(plan.VersionDirectory);

            // 3. 客户端 JAR + 库
            await DownloadAllAsync(plan.Files, relay, 5, 55, "下载库与客户端", parallelism, cancellationToken).ConfigureAwait(false);

            // 4. 资源索引 → 资源对象
            if (plan.AssetIndex != null)
            {
                relay.Report("下载资源索引", 56);
                await EnsureFileAsync(plan.AssetIndex, null, cancellationToken, preferDirect: true).ConfigureAwait(false);

                string indexText = File.ReadAllText(plan.AssetIndex.TargetPath);
                List<RemoteFile> assets = new List<RemoteFile>(
                    VersionPlanBuilder.BuildAssetFiles(
                        indexText, request.MinecraftDirectory, _options.ResourcesBaseUrl, _options.MaxAssetFiles));

                await DownloadAllAsync(assets, relay, 60, 95, "下载游戏资源", parallelism, cancellationToken).ConfigureAwait(false);
            }

            // 5. natives 解压
            if (plan.NativeArchives.Count > 0)
            {
                relay.Report("解压 natives", 96);
                Directory.CreateDirectory(plan.NativesDirectory);

                foreach (RemoteFile archive in plan.NativeArchives)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await EnsureFileAsync(archive, null, cancellationToken).ConfigureAwait(false);
                    ExtractNatives(archive.TargetPath, plan.NativesDirectory);
                }
            }

            // 6. 最后才落版本 JSON。
            // 版本目录里"有 JSON"就等于"已安装"（PclVersionCatalog 就是这么判断的），
            // 先写它会让中途失败的安装变成列表里一个能点、点开却起不来的半成品。
            File.WriteAllText(plan.VersionJsonPath, jsonText);

            relay.Report("安装完成", 100);
        }

        private async Task DownloadAllAsync(
            IReadOnlyList<RemoteFile> files,
            ProgressRelay relay,
            int fromPercent,
            int toPercent,
            string stage,
            int maxParallelism,
            CancellationToken cancellationToken)
        {
            if (files.Count == 0)
            {
                return;
            }

            float span = toPercent - fromPercent;

            long totalBytes = files.Sum(item => item.Size > 0 ? item.Size : 0);
            long completedBytes = 0;

            // 资源动辄几千个，串行下载会慢到不可用（一个文件一次 TLS 往返）。
            // 这里开有限并发：ModNet 每次调用都是独立请求，PCL 自己也是并行下的。
            int parallelism = Math.Max(1, Math.Min(16, maxParallelism));
            using (SemaphoreSlim gate = new SemaphoreSlim(parallelism))
            {
                List<Task> tasks = new List<Task>(files.Count);
                int completed = 0;

                foreach (RemoteFile file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    gate.Wait(cancellationToken);

                    RemoteFile current = file;
                    long[] currentBytes = new long[1];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            IProgress<FileDownloadProgress>? byteSink = _download is IByteProgressFileDownloader
                                ? new InlineByteProgress(value =>
                                {
                                    Interlocked.Exchange(ref currentBytes[0], value.Received);
                                    long received = Interlocked.Read(ref completedBytes) + value.Received;
                                    int bytePercent = totalBytes > 0
                                        ? fromPercent + (int)(span * received / totalBytes)
                                        : fromPercent;
                                    relay.ReportBytes(
                                        stage + " " + (totalBytes > 0 ? received * 100 / totalBytes : 0) + "%",
                                        bytePercent,
                                        received,
                                        totalBytes);
                                })
                                : null;

                            await EnsureFileAsync(current, byteSink, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            Interlocked.Add(
                                ref completedBytes,
                                current.Size > 0 ? current.Size : currentBytes[0]);
                            gate.Release();
                        }

                        int done = Interlocked.Increment(ref completed);
                        int percent = fromPercent + (int)(span * done / files.Count);
                        relay.ReportBytes(
                            stage + " " + done + "/" + files.Count,
                            percent,
                            Interlocked.Read(ref completedBytes),
                            totalBytes);
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 元数据也走直连兜底：PCL ModNet 在部分网络下连不上 piston-meta，
        /// 但清单/版本 JSON 都是公开小文件，系统 HTTP 栈可以直接取。
        /// </summary>
        private async Task<string> GetStringWithFallbackAsync(
            string url,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                DownloadDiagnostics.Write("metadata fallback " + url);
                return await _files.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>文件已存在且哈希匹配时直接跳过；否则下载到临时文件、校验后再替换。</summary>
        private async Task EnsureFileAsync(
            RemoteFile file,
            IProgress<FileDownloadProgress>? byteProgress,
            CancellationToken cancellationToken,
            bool preferDirect = false)
        {
            if (IsUpToDate(file))
            {
                return;
            }

            string directory = Path.GetDirectoryName(file.TargetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = file.TargetPath + ".nlmcl-tmp";
            Stopwatch watch = Stopwatch.StartNew();

            // PCL LoaderDownload 对大量几 KB 的资源文件有明显额外开销；
            // 大文件用它拿字节进度，小文件仍走 NetDownloadByClient 快速直下。
            const long ByteProgressThreshold = 1024 * 1024;
            bool usedPclBytePath = byteProgress != null
                && _download is IByteProgressFileDownloader
                && file.Size >= ByteProgressThreshold;
            DownloadDiagnostics.Write(
                "start " + (usedPclBytePath ? "pcl " : "small/fb ") + file.Url
                + " size=" + file.Size + " target=" + file.TargetPath);

            try
            {
                // 单个文件有超时：ModNet 在某些网络/镜像状态下会挂住不返回，
                // 并发下几个挂住的请求会把整个安装拖死（实测停在同一进度不动）。
                if (usedPclBytePath && _download is IByteProgressFileDownloader byteDownloader)
                {
                    try
                    {
                        await byteDownloader
                            .DownloadAsync(file.Url, temporary, byteProgress!, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        DownloadDiagnostics.Write(
                            "byte-path fallback " + file.Url + " after " + watch.ElapsedMilliseconds + "ms: "
                            + ex.GetType().Name + ": " + ex.Message);
                        // PCL Loader 会先写 %TEMP%\PCL\Download；低权限/受限环境会直接拒绝。
                        // 不只处理超时，任何传输失败都改走自带 HTTP 下载，避免安装卡死。
                        usedPclBytePath = false;
                        await _files
                            .DownloadAsync(file.Url, temporary, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                else if (preferDirect)
                {
                    // 资源索引优先直连：PCL 的 ModNet 在个别网络会对小请求
                    // 长时间不返回，300 秒默认超时会让整个安装卡在“下载资源索引”。
                    using (CancellationTokenSource timeout =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        await _files.DownloadAsync(file.Url, temporary, timeout.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await ResilientDownload
                        .DownloadAsync(_download, file.Url, temporary, cancellationToken, fallback: _files)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(file.Sha1))
                {
                    string actual = CryptographyUtils.ComputeFileHash(temporary, CryptographyUtils.HashMethod.Sha1);
                    if (!string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"文件校验失败：{file.Url}（期望 {file.Sha1}，实际 {actual}）");
                    }
                }

                if (File.Exists(file.TargetPath))
                {
                    File.Delete(file.TargetPath);
                }

                File.Move(temporary, file.TargetPath);
                DownloadDiagnostics.Write(
                    "done " + (usedPclBytePath ? "pcl " : "small/fb ") + file.Url
                    + " elapsed=" + watch.ElapsedMilliseconds + "ms size="
                    + (File.Exists(file.TargetPath) ? new FileInfo(file.TargetPath).Length : 0));
            }
            catch (Exception ex)
            {
                DownloadDiagnostics.Write(
                    "fail " + file.Url + " elapsed=" + watch.ElapsedMilliseconds + "ms "
                    + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
            finally
            {
                // 失败时不要留下半截文件，否则下次会被误判为已下载。
                if (File.Exists(temporary))
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private static bool IsUpToDate(RemoteFile file)
        {
            if (!File.Exists(file.TargetPath))
            {
                return false;
            }

            if (string.IsNullOrEmpty(file.Sha1))
            {
                // 没有哈希可校验时，只要求文件非空。
                return new FileInfo(file.TargetPath).Length > 0;
            }

            try
            {
                string actual = CryptographyUtils.ComputeFileHash(file.TargetPath, CryptographyUtils.HashMethod.Sha1);
                return string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void ExtractNatives(string archivePath, string targetDirectory)
        {
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    // 只要本机可加载的二进制；META-INF 等元数据跳过。
                    string extension = Path.GetExtension(entry.Name);
                    if (!string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string destination = Path.Combine(targetDirectory, entry.Name);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }
        }

        private static string ResolveVersionUrl(string manifestJson, string versionId)
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(manifestJson);
            Newtonsoft.Json.Linq.JArray versions = root["versions"] as Newtonsoft.Json.Linq.JArray;

            if (versions == null)
            {
                throw new InvalidDataException("版本清单缺少 versions 数组。");
            }

            foreach (Newtonsoft.Json.Linq.JToken version in versions)
            {
                if (string.Equals(version.Value<string>("id"), versionId, StringComparison.Ordinal))
                {
                    string? url = version.Value<string>("url");
                    if (url != null && url.Length > 0)
                    {
                        return url;
                    }
                }
            }

            throw new InvalidOperationException("版本清单中找不到版本：" + versionId);
        }

        /// <summary>把重复/倒退的进度挡掉，保证对外单调不减。</summary>
        private sealed class ProgressRelay
        {
            private readonly IProgress<InstallProgress> _progress;

            public ProgressRelay(IProgress<InstallProgress> progress)
            {
                _progress = progress;
            }

            private int _lastPercent = -1;
            private string _lastStage = string.Empty;

            private readonly object _lock = new object();

            public void Report(string stage, int percent)
            {
                ReportBytes(stage, percent, 0, 0);
            }

            public void ReportBytes(string stage, int percent, long received, long total)
            {
                // 并发下载后多个线程会同时上报，必须串行化，否则 _lastPercent 会竞争
                lock (_lock)
                {
                    if (percent < _lastPercent)
                    {
                        percent = _lastPercent;
                    }

                    _lastPercent = percent;
                    _lastStage = stage;
                    _progress?.Report(new InstallProgress(stage, percent, 0, received, total));
                }
            }
        }

        private sealed class InlineByteProgress : IProgress<FileDownloadProgress>
        {
            private readonly Action<FileDownloadProgress> _apply;

            public InlineByteProgress(Action<FileDownloadProgress> apply)
            {
                _apply = apply;
            }

            public void Report(FileDownloadProgress value) => _apply(value);
        }
    }
}

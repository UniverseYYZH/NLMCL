using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 流式文件下载：HttpClient + 边下边写 + 重试；大文件走**多连接分块**。
    ///
    /// 为什么要分块：modrinth 的 CDN 在校园网下**按连接限速**，
    /// 实测单连接 ~45 KB/s、4 连接合计 ~220 KB/s。15 MB 的 jar 单流要 5 分钟以上，
    /// 会撞上原来的 300 秒超时；所以必须多连接，且超时按体积放大。
    ///
    /// 失败策略：每个分块单独重试；分块留在 &lt;目标&gt;.parts 目录，下次可续传；
    /// 只有服务器不支持 Range 才回落单连接。
    /// </summary>
    public sealed class HttpFileDownloader : IFileDownloader, IByteProgressFileDownloader
    {
        private const int Attempts = 3;

        private const int BaseTimeoutSeconds = 300;

        /// <summary>并行分块的最小文件大小：小文件多连接反而更慢。</summary>
        private const long ParallelThreshold = 1024 * 1024;

        private const int ParallelConnections = 6;

        private const int MaxPerConnection = 4;

        /// <summary>分块首字节超时。</summary>
        private static readonly TimeSpan FirstByteTimeout = TimeSpan.FromSeconds(15);

        /// <summary>分块"无进展"超时（每收到数据就重置）。</summary>
        private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(60);

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            // .NET Framework 默认每个 endpoint 只允许 2 个并发连接（ServicePointManager 限制），
            // 这会让"多连接下载"实际只有 2 条流，速度上不去。这里放开到 16。
            try
            {
                System.Net.ServicePointManager.DefaultConnectionLimit = 16;
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch (Exception)
            {
                // 受限环境可能不允许修改，忽略
            }
            HttpClientHandler handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.None
            };

            HttpClient client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "NLMCL/1.0 (Minecraft launcher)");

            return client;
        }

        public async Task DownloadAsync(
            string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (progress == null)
            {
                await DownloadAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
                return;
            }

            RemoteFile? remote = await ProbeAsync(url, cancellationToken).ConfigureAwait(false);
            long total = remote?.Length ?? 0;

            using (CancellationTokenSource polling =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task reporter = ReportProgressAsync(targetPath, total, progress, polling.Token);
                bool completed = false;

                try
                {
                    await DownloadAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
                    completed = true;
                }
                finally
                {
                    polling.Cancel();

                    try
                    {
                        await reporter.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    // 取消/失败不能假装 100% 完成，否则用户看到“满进度 + 安装失败”。
                    progress.Report(completed ? 1 : 0);
                }
            }
        }

        /// <summary>字节级进度：received 为已写入目标/分块的字节，total 为探测到的总长（0 表示未知）。</summary>
        public async Task DownloadAsync(
            string url,
            string targetPath,
            IProgress<FileDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (progress == null)
            {
                await DownloadAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
                return;
            }

            RemoteFile? remote = await ProbeAsync(url, cancellationToken).ConfigureAwait(false);
            long total = remote?.Length ?? 0;

            using (CancellationTokenSource polling =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task reporter = ReportByteProgressAsync(targetPath, total, progress, polling.Token);
                bool completed = false;

                try
                {
                    await DownloadAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
                    completed = true;
                }
                finally
                {
                    polling.Cancel();

                    try
                    {
                        await reporter.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    long final = completed ? CurrentDownloadedBytes(targetPath) : 0;
                    progress.Report(new FileDownloadProgress(final, total > 0 ? total : final));
                }
            }
        }

        /// <summary>每 250ms 上报一次「目标文件 + 分块」已下载字节占理论总长的比例。</summary>
        private static async Task ReportProgressAsync(
            string targetPath, long total, IProgress<double> progress, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    long done = CurrentDownloadedBytes(targetPath);

                    if (total > 0)
                    {
                        progress.Report(Math.Min(1, done / (double)total));
                    }
                }
                catch (IOException)
                {
                    // 分块正在写入，这一轮跳过
                }

                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static async Task ReportByteProgressAsync(
            string targetPath,
            long total,
            IProgress<FileDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    long done = CurrentDownloadedBytes(targetPath);
                    progress.Report(new FileDownloadProgress(done, total));
                }
                catch (IOException)
                {
                }

                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static long CurrentDownloadedBytes(string targetPath)
        {
            long done = 0;

            // 下载先写同目录临时文件，完成后才原子替换目标文件；进度优先读临时文件。
            string partialPath = PartialPath(targetPath);

            if (File.Exists(partialPath))
            {
                done += new FileInfo(partialPath).Length;
            }
            else if (File.Exists(targetPath))
            {
                done += new FileInfo(targetPath).Length;
            }

            string partsDirectory = targetPath + ".parts";

            if (Directory.Exists(partsDirectory))
            {
                foreach (string part in Directory.GetFiles(partsDirectory, "*.part"))
                {
                    done += new FileInfo(part).Length;
                }
            }

            return done;
        }

        public async Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("下载地址为空。", nameof(url));
            }

            RemoteFile? remote = await ProbeAsync(url, cancellationToken).ConfigureAwait(false);

            Exception? last = null;

            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (remote != null && remote.SupportsRanges && remote.Length >= ParallelThreshold)
                    {
                        if (await ParallelAsync(url, remote.Length, targetPath, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            return;
                        }

                        Trace("并行路径没成功，改用单连接：" + url);
                    }

                    await DownloadOnceAsync(url, targetPath, remote?.Length, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    TryDelete(PartialPath(targetPath));

                    if (attempt < Attempts)
                    {
                        await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            throw new InvalidOperationException(
                "下载失败（已重试 " + Attempts + " 次）：" + url + " —— " + last?.Message, last);
        }

        /// <summary>把失败原因写到 %TEMP%\nlmcl-download.log（排查用）。</summary>
        internal static void Trace(string message)
        {
            DownloadDiagnostics.Write("http " + message);
        }

        private sealed class RemoteFile
        {
            public RemoteFile(long length, bool supportsRanges)
            {
                Length = length;
                SupportsRanges = supportsRanges;
            }

            public long Length { get; }

            public bool SupportsRanges { get; }
        }

        private static async Task<RemoteFile?> ProbeAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpRequestMessage head = new HttpRequestMessage(HttpMethod.Head, url))
                using (HttpResponseMessage response = await Client
                    .SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    long? length = response.Content.Headers.ContentLength;

                    if (length == null)
                    {
                        return null;
                    }

                    bool ranged = false;

                    foreach (string header in response.Headers.AcceptRanges)
                    {
                        if (string.Equals(header, "bytes", StringComparison.OrdinalIgnoreCase))
                        {
                            ranged = true;
                            break;
                        }
                    }

                    return new RemoteFile(length.Value, ranged);
                }
            }
            catch (Exception ex)
            {
                Trace("HEAD 失败：" + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        // ---------- 并行分块 ----------

        private static async Task<bool> ParallelAsync(
            string url, long length, string targetPath, CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            string partsDirectory = targetPath + ".parts";
            Directory.CreateDirectory(partsDirectory);

            // 分块数自适应：大文件切更多块。
            // 原来固定 6 块，93 MB 的包一块 15 MB，只要有一块慢，整体就卡在 70% 附近挪不动。
            int chunkCount = ChunkCountFor(length);
            List<(long Start, long End)> chunks = BuildChunks(length, chunkCount);
            bool[] failed = new bool[chunks.Count];

            using (SemaphoreSlim gate = new SemaphoreSlim(MaxPerConnection))
            {
                List<Task> tasks = new List<Task>(chunks.Count);

                for (int i = 0; i < chunks.Count; i++)
                {
                    int index = i;
                    long start = chunks[i].Start;
                    long end = chunks[i].End;
                    string part = Path.Combine(partsDirectory, index + ".part");

                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

                        try
                        {
                            failed[index] = !await EnsurePartAsync(url, start, end, part, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            for (int i = 0; i < failed.Length; i++)
            {
                if (failed[i])
                {
                    Trace("分块 " + i + " 最终失败，保留 .parts 供下次续传：" + targetPath);
                    return false;
                }
            }

            string partialPath = PartialPath(targetPath);

            try
            {
                using (FileStream merged = new FileStream(
                    partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        string part = Path.Combine(partsDirectory, i + ".part");

                        using (FileStream source = new FileStream(
                            part, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                        {
                            await source.CopyToAsync(merged, 81920, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                if (new FileInfo(partialPath).Length != length)
                {
                    Trace("合并后大小不符：" + targetPath);
                    TryDelete(partialPath);
                    return false;
                }

                CommitPartial(partialPath, targetPath);

                foreach (string part in Directory.GetFiles(partsDirectory, "*.part"))
                {
                    TryDelete(part);
                }

                TryDeleteDirectory(partsDirectory);
                return true;
            }
            catch (Exception ex)
            {
                Trace("合并失败：" + ex.GetType().Name + " " + ex.Message);
                TryDelete(partialPath);
                return false;
            }
        }

        private static async Task<bool> EnsurePartAsync(
            string url, long start, long end, string partPath, CancellationToken cancellationToken)
        {
            long expected = end - start + 1;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (File.Exists(partPath) && new FileInfo(partPath).Length == expected)
                {
                    return true;
                }

                try
                {
                    long done = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

                    if (done >= expected)
                    {
                        TryDelete(partPath);
                        done = 0;
                    }

                    // 重要：重试时从断点继续，而不是把已经下好的部分丢掉重来
                    await DownloadRangeAsync(url, start + done, end, partPath, done > 0, cancellationToken)
                        .ConfigureAwait(false);

                    if (new FileInfo(partPath).Length == expected)
                    {
                        return true;
                    }

                    Trace("分块大小不符：" + Path.GetFileName(partPath));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Trace("分块失败（第 " + attempt + " 次）：" + ex.GetType().Name + " " + ex.Message);
                }

                if (attempt < 3)
                {
                    await Task.Delay(400 * attempt, cancellationToken).ConfigureAwait(false);
                }
            }

            return false;
        }

        /// <summary>
        /// 下载一个分块，带**两级超时**：
        ///   首字节 15 秒内没到 → 失败；已连上但 60 秒没有新数据 → 失败。
        ///
        /// 为什么必须有：调用方大多传 CancellationToken.None，
        /// 一个卡住的连接会让整次下载既不失败也不重试（成员审阅 3.5 指出的问题）。
        /// </summary>
        private static async Task DownloadRangeAsync(
            string url, long start, long end, string partPath, bool append, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource watchdog =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                watchdog.CancelAfter(FirstByteTimeout);

                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(start, end);

                    try
                    {
                        using (HttpResponseMessage response = await Client
                            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, watchdog.Token)
                            .ConfigureAwait(false))
                        {
                            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                            {
                                throw new InvalidOperationException("服务器不支持分块下载：" + response.StatusCode);
                            }

                            using (Stream remote = await response.Content
                                .ReadAsStreamAsync()
                                .ConfigureAwait(false))
                            using (FileStream local = new FileStream(
                                partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                            {
                                byte[] buffer = new byte[81920];
                                int read;

                                while ((read = await remote
                                    .ReadAsync(buffer, 0, buffer.Length, watchdog.Token)
                                    .ConfigureAwait(false)) > 0)
                                {
                                    await local
                                        .WriteAsync(buffer, 0, read, watchdog.Token)
                                        .ConfigureAwait(false);

                                    // 每收到数据就重置"无进展"计时
                                    watchdog.CancelAfter(NoProgressTimeout);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            "分块超时（首字节 " + FirstByteTimeout.TotalSeconds + " 秒 / 无进展 "
                            + NoProgressTimeout.TotalSeconds + " 秒）：" + url);
                    }
                }
            }
        }

        /// <summary>按体积决定切几块：每块约 4 MB，最少 6 块、最多 16 块。</summary>
        internal static int ChunkCountFor(long length)
        {
            if (length <= 0)
            {
                return ParallelConnections;
            }

            int bySize = (int)(length / (4L * 1024 * 1024));
            return Math.Min(16, Math.Max(ParallelConnections, bySize));
        }

        /// <summary>把总长度切成 <paramref name="count"/> 段（最后一段收到末尾）。</summary>
        internal static List<(long Start, long End)> BuildChunks(long length, int count)
        {
            List<(long Start, long End)> chunks = new List<(long Start, long End)>();

            if (length <= 0 || count <= 0)
            {
                return chunks;
            }

            long size = (length + count - 1) / count;

            for (long start = 0; start < length; start += size)
            {
                long end = Math.Min(start + size - 1, length - 1);
                chunks.Add((start, end));
            }

            return chunks;
        }

        // ---------- 单连接 ----------

        private static async Task DownloadOnceAsync(
            string url, string targetPath, long? expectedLength, CancellationToken cancellationToken)
        {
            int timeoutSeconds = BaseTimeoutSeconds;

            if (expectedLength != null)
            {
                // 按 20 KB/s 的保守速度估算；上限 30 分钟，避免大文件必然超时
                long estimate = expectedLength.Value / (20 * 1024) + 60;
                timeoutSeconds = (int)Math.Min(30 * 60, Math.Max(BaseTimeoutSeconds, estimate));
            }

            using (CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    using (HttpResponseMessage response = await Client
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                        .ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        string? directory = Path.GetDirectoryName(targetPath);

                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory!);
                        }

                        string partialPath = PartialPath(targetPath);

                        try
                        {
                            long written;

                            using (Stream remote = await response.Content
                                .ReadAsStreamAsync()
                                .ConfigureAwait(false))
                            using (FileStream local = new FileStream(
                                partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                            {
                                await remote.CopyToAsync(local, 81920, timeout.Token).ConfigureAwait(false);
                                written = local.Length;
                            }

                            if (expectedLength.HasValue && written != expectedLength.Value)
                            {
                                throw new IOException(
                                    "下载不完整：期望 " + expectedLength.Value + " 字节，实际 " + written + " 字节。");
                            }

                            CommitPartial(partialPath, targetPath);
                        }
                        catch
                        {
                            TryDelete(partialPath);
                            throw;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("超过 " + timeoutSeconds + " 秒未完成：" + url);
                }
            }
        }

        /// <summary>取文本（清单用）：走同一个 HttpClient，带超时与重试。</summary>
        public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Exception? last = null;

            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));

                    try
                    {
                        using (HttpResponseMessage response = await Client
                            .GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token)
                            .ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();

                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        last = ex;

                        if (attempt < Attempts)
                        {
                            await Task.Delay(400 * attempt, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            throw new InvalidOperationException("下载文本失败：" + url + " —— " + last?.Message, last);
        }

        private static string PartialPath(string targetPath)
        {
            return targetPath + ".nlmcl-tmp";
        }

        /// <summary>只有完整下载/合并成功后才把临时文件替换到目标位置，取消或失败不会留下半截 jar。</summary>
        private static void CommitPartial(string partialPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                try
                {
                    File.Replace(partialPath, targetPath, destinationBackupFileName: null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (IOException)
                {
                }

                File.Copy(partialPath, targetPath, overwrite: true);
                TryDelete(partialPath);
                return;
            }

            File.Move(partialPath, targetPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

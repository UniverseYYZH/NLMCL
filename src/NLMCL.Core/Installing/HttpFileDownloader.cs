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
    public sealed class HttpFileDownloader : IFileDownloader
    {
        private const int Attempts = 3;

        private const int BaseTimeoutSeconds = 300;

        /// <summary>并行分块的最小文件大小：小文件多连接反而更慢。</summary>
        private const long ParallelThreshold = 1024 * 1024;

        private const int ParallelConnections = 6;

        private const int MaxPerConnection = 4;

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
                    TryDelete(targetPath);

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
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "nlmcl-download.log");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // 日志失败不影响下载
            }
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

            List<(long Start, long End)> chunks = BuildChunks(length, ParallelConnections);
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

            try
            {
                using (FileStream merged = new FileStream(
                    targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
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

                if (new FileInfo(targetPath).Length != length)
                {
                    Trace("合并后大小不符：" + targetPath);
                    TryDelete(targetPath);
                    return false;
                }

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
                TryDelete(targetPath);
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
                    await DownloadRangeAsync(url, start, end, partPath, cancellationToken).ConfigureAwait(false);

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

        private static async Task DownloadRangeAsync(
            string url, long start, long end, string partPath, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Range = new RangeHeaderValue(start, end);

                using (HttpResponseMessage response = await Client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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
                        partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await remote.CopyToAsync(local, 81920, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
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

                        using (Stream remote = await response.Content
                            .ReadAsStreamAsync()
                            .ConfigureAwait(false))
                        using (FileStream local = new FileStream(
                            targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                        {
                            await remote.CopyToAsync(local, 81920, timeout.Token).ConfigureAwait(false);
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

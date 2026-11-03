using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 流式文件下载：HttpClient + 边下边写 + 重试。
    ///
    /// 只用于整合包文件这类"直连 CDN、文件多且大"的场景。
    /// 元数据/搜索仍然走 PCL 的 ModNet（有镜像与重试策略）。
    /// </summary>
    public sealed class HttpFileDownloader : IFileDownloader
    {
        private const int Attempts = 3;
        private const int TimeoutSeconds = 300;

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
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

            Exception? last = null;

            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await DownloadOnceAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
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

        private static async Task DownloadOnceAsync(
            string url, string targetPath, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

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
                    throw new TimeoutException("超过 " + TimeoutSeconds + " 秒未完成：" + url);
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
    }
}

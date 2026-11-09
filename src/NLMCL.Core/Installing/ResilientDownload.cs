using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 带重试与单文件超时的下载封装。
    ///
    /// 注意：**不能用"文件大小是否增长"来判断停滞**。
    /// PCL 的 ModNet 实现是 <c>FileUtils.Write(path, SendRequest(url))</c>——
    /// 整包下载到内存后才一次性写盘，所以目标文件在完成前根本不存在，
    /// 按大小判停滞会把所有大文件（client.jar、几十 MB 的模组）误杀，
    /// 表现为"下载很慢然后失败"。
    ///
    /// 因此这里只做：每个文件一个总超时（默认 300 秒）+ 最多 3 次重试。
    /// 真挂死的连接由超时兜底；慢但正常的下载不会被掐断。
    /// </summary>
    internal static class ResilientDownload
    {
        public const int DefaultTimeoutSeconds = DownloadRetryPolicy.FileTimeoutSeconds;

        public static async Task DownloadAsync(
            IDownloadService download,
            string url,
            string targetPath,
            CancellationToken cancellationToken,
            int attempts = DownloadRetryPolicy.Attempts,
            int timeoutSeconds = DefaultTimeoutSeconds,
            IFileDownloader? fallback = null)
        {
            if (download == null)
            {
                throw new ArgumentNullException(nameof(download));
            }

            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, timeoutSeconds)));

                    try
                    {
                        await download
                            .DownloadFileAsync(url, targetPath, timeout.Token)
                            .ConfigureAwait(false);

                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        TryDelete(targetPath);

                        if (attempt >= attempts)
                        {
                            // 最后兜底：ModNet 的小文件直下在部分网络会失败，改用流式下载器
                            if (fallback != null)
                            {
                                try
                                {
                                    await fallback.DownloadAsync(url, targetPath, cancellationToken)
                                        .ConfigureAwait(false);

                                    return;
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch (Exception fallbackError)
                                {
                                    string reasonBoth = ex is OperationCanceledException
                                        ? "超过 " + timeoutSeconds + " 秒未完成"
                                        : ex.Message;

                                    throw new InvalidOperationException(
                                        "下载失败（ModNet 重试 " + attempts + " 次：" + reasonBoth
                                        + "；备用下载：" + fallbackError.Message + "）：" + url, fallbackError);
                                }
                            }

                            string reason = ex is OperationCanceledException
                                ? "超过 " + timeoutSeconds + " 秒未完成"
                                : ex.Message;

                            throw new InvalidOperationException(
                                "下载失败（已重试 " + attempts + " 次）：" + url + " —— " + reason, ex);
                        }

                        // 退避一下，避免立刻撞上同一个坏连接
                        await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
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

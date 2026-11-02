using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 带"停滞检测"的下载封装。
    ///
    /// 为什么不直接用固定超时：Fabric 的 maven 在国内没有镜像，慢是常态；
    /// 固定超时会把还在传输的文件误杀，而真正的死连接特征是**文件大小不再增长**。
    /// 所以这里每 2 秒看一次临时文件大小，连续 30 秒不增长才判定连接已死。
    /// </summary>
    internal static class ResilientDownload
    {
        private const int PollMilliseconds = 2000;
        private const int StallSeconds = 30;
        private const int TotalSeconds = 600;

        public static async Task DownloadAsync(
            IDownloadService download,
            string url,
            string targetPath,
            CancellationToken cancellationToken,
            int attempts = 3)
        {
            if (download == null)
            {
                throw new ArgumentNullException(nameof(download));
            }

            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await AttemptAsync(download, url, targetPath, cancellationToken).ConfigureAwait(false);
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
                        throw new InvalidOperationException(
                            "下载失败（已重试 " + attempts + " 次）：" + url + " —— " + ex.Message, ex);
                    }

                    // 退避一下，避免立刻撞上同一个坏连接
                    await Task.Delay(500 * attempt, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task AttemptAsync(
            IDownloadService download, string url, string targetPath, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task downloadTask = download.DownloadFileAsync(url, targetPath, timeout.Token);

                DateTime started = DateTime.UtcNow;
                DateTime lastGrowth = started;
                long lastSize = -1;

                while (true)
                {
                    Task finished = await Task.WhenAny(downloadTask, Task.Delay(PollMilliseconds, cancellationToken))
                        .ConfigureAwait(false);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (ReferenceEquals(finished, downloadTask))
                    {
                        break;
                    }

                    long size = 0;
                    try
                    {
                        if (File.Exists(targetPath))
                        {
                            size = new FileInfo(targetPath).Length;
                        }
                    }
                    catch (IOException)
                    {
                    }

                    DateTime now = DateTime.UtcNow;

                    if (size != lastSize)
                    {
                        lastSize = size;
                        lastGrowth = now;
                    }

                    if ((now - lastGrowth).TotalSeconds >= StallSeconds)
                    {
                        Abandon(downloadTask, timeout);
                        throw new TimeoutException("下载停滞 " + StallSeconds + " 秒（已下载 " + size + " 字节）");
                    }

                    if ((now - started).TotalSeconds >= TotalSeconds)
                    {
                        Abandon(downloadTask, timeout);
                        throw new TimeoutException("下载超过 " + TotalSeconds + " 秒");
                    }
                }

                await downloadTask.ConfigureAwait(false);
            }
        }

        private static void Abandon(Task downloadTask, CancellationTokenSource timeout)
        {
            try
            {
                timeout.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _ = downloadTask.ContinueWith(task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 下载装饰器：元数据优先走 BMCLAPI 镜像，失败回退官方；
    /// 二进制文件（client.jar / assets）优先官方 CDN，失败回退 BMCLAPI。
    ///
    /// 为什么二进制不镜像优先：真机实测 BMCLAPI 二进制 20–30 秒/文件，
    /// 官方 CDN 毫秒到几秒。保留回退是为了官方 CDN 不可用的网络。
    /// </summary>
    public sealed class MirroredDownloadService : IDownloadService, IMirrorSwitch, IByteProgressFileDownloader
    {
        private readonly IDownloadService _inner;

        /// <summary>
        /// 开关：false 时直接走原始下载服务（官方源）。
        /// 由设置页控制，运行时可随时切换，不需要重启。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 开关关掉时返回 null，调用方就会按"没有可用镜像"走官方源。
        /// </summary>
        private string? RewriteIfEnabled(string url)
            => Enabled ? MojangMirror.TryRewrite(url) : null;

        public MirroredDownloadService(IDownloadService inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public async Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
        {
            string? mirror = RewriteIfEnabled(url);

            if (mirror == null)
            {
                if (!Enabled || !MojangMirror.IsBinaryHost(url))
                {
                    await _inner.DownloadFileAsync(url, localPath, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // 二进制：官方优先，失败再回退 BMCLAPI。
                try
                {
                    await _inner.DownloadFileAsync(url, localPath, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    TryDelete(localPath);
                    string? fallback = MojangMirror.TryRewriteForFallback(url);

                    if (fallback == null)
                    {
                        throw;
                    }

                    await _inner.DownloadFileAsync(fallback, localPath, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            try
            {
                await _inner.DownloadFileAsync(mirror, localPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 镜像失败：把可能残留的半截文件清掉，再走官方
                TryDelete(localPath);
                await _inner.DownloadFileAsync(url, localPath, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>字节进度也要穿过镜像装饰器，否则安装器看不到 PCL 的 LoaderDownload 进度。</summary>
        public async Task DownloadAsync(
            string url,
            string localPath,
            IProgress<FileDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (progress == null || _inner is not IByteProgressFileDownloader byteDownloader)
            {
                await DownloadFileAsync(url, localPath, cancellationToken).ConfigureAwait(false);
                return;
            }

            string? mirror = RewriteIfEnabled(url);

            if (mirror == null)
            {
                if (!Enabled || !MojangMirror.IsBinaryHost(url))
                {
                    await byteDownloader.DownloadAsync(url, localPath, progress, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                // 二进制：官方优先，失败再回退 BMCLAPI。
                try
                {
                    await byteDownloader.DownloadAsync(url, localPath, progress, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    TryDelete(localPath);
                    string? fallback = MojangMirror.TryRewriteForFallback(url);

                    if (fallback == null)
                    {
                        throw;
                    }

                    await byteDownloader.DownloadAsync(fallback, localPath, progress, cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }

            try
            {
                await byteDownloader.DownloadAsync(mirror, localPath, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                TryDelete(localPath);
                await byteDownloader.DownloadAsync(url, localPath, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            string? mirror = RewriteIfEnabled(url);

            if (mirror == null)
            {
                return await _inner.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await _inner.GetStringAsync(mirror, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return await _inner.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<string> GetStringAsync(
            string url, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            string? mirror = RewriteIfEnabled(url);

            if (mirror == null)
            {
                return await _inner.GetStringAsync(url, headers, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await _inner.GetStringAsync(mirror, headers, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return await _inner.GetStringAsync(url, headers, cancellationToken).ConfigureAwait(false);
            }
        }

        // 登录 / 令牌类请求不镜像：它们必须打到微软自己的端点。
        public Task<string> PostJsonAsync(string url, string jsonBody,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
            => _inner.PostJsonAsync(url, jsonBody, headers, cancellationToken);

        public Task<string> PostFormAsync(string url,
            IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
            => _inner.PostFormAsync(url, fields, cancellationToken);

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

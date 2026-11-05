using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 下载装饰器：对 Mojang 域名**优先走 BMCLAPI 镜像**，失败再回退官方源。
    ///
    /// 为什么不做成"设置里选源"：镜像慢/挂掉时回退官方已经能保证正确性，
    /// 而多一个开关就多一份需要用户理解的东西（真需要再加）。
    /// 安装器本身（版本、资产、Java 运行时）不需要知道这件事——它们只认下载端口。
    /// </summary>
    public sealed class MirroredDownloadService : IDownloadService, IMirrorSwitch
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
                await _inner.DownloadFileAsync(url, localPath, cancellationToken).ConfigureAwait(false);
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

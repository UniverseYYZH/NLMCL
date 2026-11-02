using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using NLMCL.Abstractions;

namespace NLMCL.Core.Accounts
{
    /// <summary>
    /// 给登录流程用的下载装饰器：把 4xx 的正文当作"正常响应"返回。
    ///
    /// 为什么需要：PCL 的 ModNet 对任何非 2xx 都抛 <see cref="HttpRequestCodeException"/>，
    /// 但 OAuth / Xbox 的错误恰恰是用 4xx + 正文表达的：
    ///   - token 端点：400 + <c>{"error":"authorization_pending"}</c>（用户还没在浏览器确认）
    ///   - XSTS：401 + <c>{"XErr":2148916233}</c>（没有 Xbox 档案）
    ///   - profile：404（没买游戏）
    /// 如果不把正文放回来，认证器里那些 <c>switch (error)</c> / <c>XErr</c> 分支全是死代码。
    ///
    /// 只包在 <see cref="MicrosoftAuthenticator"/> 外面，不影响其它下载。
    /// </summary>
    public sealed class OAuthDownloadService : IDownloadService
    {
        private readonly IDownloadService _inner;

        public OAuthDownloadService(IDownloadService inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken)
            => _inner.DownloadFileAsync(url, localPath, cancellationToken);

        public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                return await _inner.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestCodeException ex) when (!string.IsNullOrEmpty(ex.Response))
            {
                return ex.Response!;
            }
        }

        public async Task<string> GetStringAsync(
            string url, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            try
            {
                return await _inner.GetStringAsync(url, headers, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestCodeException ex) when (!string.IsNullOrEmpty(ex.Response))
            {
                return ex.Response!;
            }
        }

        public async Task<string> PostJsonAsync(string url, string jsonBody,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            try
            {
                return await _inner.PostJsonAsync(url, jsonBody, headers, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestCodeException ex) when (!string.IsNullOrEmpty(ex.Response))
            {
                return ex.Response!;
            }
        }

        public async Task<string> PostFormAsync(string url,
            IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
        {
            try
            {
                return await _inner.PostFormAsync(url, fields, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestCodeException ex) when (!string.IsNullOrEmpty(ex.Response))
            {
                return ex.Response!;
            }
        }
    }
}

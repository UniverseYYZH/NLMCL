using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLCL.Abstractions
{
    /// <summary>
    /// 下载端口。当前由 PCL 的 <c>ModNet</c> 实现（多镜像、重试、429 退避）。
    /// </summary>
    public interface IDownloadService
    {
        /// <summary>把 <paramref name="url"/> 的内容下载到 <paramref name="localPath"/>，成功后覆盖旧文件。</summary>
        Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken);

        /// <summary>读取文本内容（用于版本清单等小文件）。</summary>
        Task<string> GetStringAsync(string url, CancellationToken cancellationToken);

        /// <summary>带请求头的 GET（例如 Minecraft Services 需要 <c>Authorization: Bearer</c>）。</summary>
        Task<string> GetStringAsync(
            string url,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken);

        /// <summary>POST 一个 JSON 正文（Xbox Live / XSTS / Minecraft Services 用）。</summary>
        Task<string> PostJsonAsync(
            string url,
            string jsonBody,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken);

        /// <summary>POST 表单（OAuth 的 devicecode / token 端点用 <c>x-www-form-urlencoded</c>）。</summary>
        Task<string> PostFormAsync(
            string url,
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken);
    }
}

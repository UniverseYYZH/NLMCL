using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 单纯的流式文件下载（整合包文件用）。
    ///
    /// 为什么不复用 <see cref="IDownloadService"/>：PCL 的 ModNet 在"小文件直下"路径上
    /// 会把带 <c>%2B</c> 的 URL 弄坏（实测同一 URL 用 HttpWebRequest 200、走 ModNet 400），
    /// 而且是整包下到内存后一次写盘、并发时会互相排队。
    /// 整合包动辄 200+ 文件，必须用流式 + 真并发。
    /// </summary>
    public interface IFileDownloader
    {
        Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken);

        /// <summary>取文本（清单、元数据）。</summary>
        Task<string> GetStringAsync(string url, CancellationToken cancellationToken);
    }

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

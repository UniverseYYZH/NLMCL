using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 单纯的流式文件下载（整合包文件用）。
    ///
    /// 为什么不复用 <see cref="IDownloadService"/>：PCL 的 ModNet 在“小文件直下”路径上
    /// 会把带 <c>%2B</c> 的 URL 弄坏，而且是整包下到内存后一次写盘、并发时会互相排队。
    /// </summary>
    public interface IFileDownloader
    {
        Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken);

        /// <summary>带进度的下载：0~1 比例；传 null 等价于不带进度。</summary>
        Task DownloadAsync(
            string url, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken);

        Task<string> GetStringAsync(string url, CancellationToken cancellationToken);
    }

    /// <summary>下载端口。当前由 PCL 的 ModNet 实现（多镜像、重试、429 退避）。</summary>
    public interface IDownloadService
    {
        Task DownloadFileAsync(string url, string localPath, CancellationToken cancellationToken);

        Task<string> GetStringAsync(string url, CancellationToken cancellationToken);

        Task<string> GetStringAsync(
            string url,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken);

        Task<string> PostJsonAsync(
            string url,
            string jsonBody,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken);

        Task<string> PostFormAsync(
            string url,
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken);
    }

    /// <summary>下载镜像开关（实现：MirroredDownloadService）。</summary>
    public interface IMirrorSwitch
    {
        bool Enabled { get; set; }
    }

    /// <summary>流式下载的字节进度（received 为已写入字节，total 为 0 表示未知）。</summary>
    public readonly struct FileDownloadProgress
    {
        public FileDownloadProgress(long received, long total)
        {
            Received = received;
            Total = total;
        }

        public long Received { get; }

        public long Total { get; }

        public double Percent => Total > 0 ? Math.Min(1, Received / (double)Total) : 0;
    }

    /// <summary>可选：提供 received/total 的流式下载器；引擎优先用它。</summary>
    public interface IByteProgressFileDownloader
    {
        Task DownloadAsync(
            string url,
            string targetPath,
            IProgress<FileDownloadProgress>? progress,
            CancellationToken cancellationToken);
    }
}

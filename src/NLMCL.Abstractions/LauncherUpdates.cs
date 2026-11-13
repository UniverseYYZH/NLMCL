using System;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>远端更新源里的一条启动器版本信息。</summary>
    public sealed class LauncherUpdateInfo
    {
        public LauncherUpdateInfo(
            string version,
            string? notes,
            string downloadUrl,
            string? sha256,
            DateTimeOffset? publishedAt)
        {
            Version = version;
            Notes = notes;
            DownloadUrl = downloadUrl;
            Sha256 = sha256;
            PublishedAt = publishedAt;
        }

        public string Version { get; }

        public string? Notes { get; }

        public string DownloadUrl { get; }

        public string? Sha256 { get; }

        public DateTimeOffset? PublishedAt { get; }

        public string Summary
        {
            get
            {
                string summary = "发现新版本 " + Version;
                if (PublishedAt.HasValue)
                {
                    summary += " · " + PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd");
                }

                return summary;
            }
        }
    }

    /// <summary>
    /// 只检查启动器更新，不下载、不替换 exe。
    /// 更新源由用户/维护者配置，默认不启用。
    /// </summary>
    public interface ILauncherUpdateService
    {
        Task<LauncherUpdateInfo?> CheckAsync(string feedUrl, CancellationToken cancellationToken);
    }
}

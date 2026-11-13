using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Updates
{
    /// <summary>
    /// 读取一个很简单的更新源 JSON：
    /// { "version": "1.2.0", "notes": "...", "url": "https://...",
    ///   "sha256": "...", "publishedAt": "2026-01-01T00:00:00Z" }
    /// 不下载文件，只把信息交给 UI。
    /// </summary>
    public sealed class LauncherUpdateService : ILauncherUpdateService
    {
        private readonly IDownloadService _downloads;
        private readonly string _currentVersion;

        public LauncherUpdateService(IDownloadService downloads, string currentVersion)
        {
            _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
            _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        }

        public async Task<LauncherUpdateInfo?> CheckAsync(string feedUrl, CancellationToken cancellationToken)
        {
            if (!TryNormalizeUrl(feedUrl, out string normalized))
            {
                return null;
            }

            string json;
            try
            {
                json = await _downloads.GetStringAsync(normalized, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }

            string? version = null;
            string? notes = null;
            string? url = null;
            string? sha256 = null;
            DateTimeOffset? publishedAt = null;

            try
            {
                JObject? root = JObject.Parse(json);
                version = (string?)root["version"];
                notes = (string?)root["notes"];
                url = (string?)root["url"];
                sha256 = (string?)root["sha256"];
                if (DateTimeOffset.TryParse((string?)root["publishedAt"], out DateTimeOffset parsed))
                {
                    publishedAt = parsed;
                }
            }
            catch (JsonException)
            {
                return null;
            }

            if (!TryNormalizeUrl(url, out string downloadUrl)
                || !TryParseVersion(version, out Version? remote)
                || !TryParseVersion(_currentVersion, out Version? current))
            {
                return null;
            }

            if (remote <= current)
            {
                return null;
            }

            return new LauncherUpdateInfo(version!.Trim(), notes, downloadUrl, sha256, publishedAt);
        }

        private static bool TryNormalizeUrl(string? value, out string normalized)
        {
            normalized = string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }

            normalized = uri.AbsoluteUri;
            return true;
        }

        internal static bool TryParseVersion(string? value, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value!.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }

            int cut = text.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                text = text.Substring(0, cut);
            }

            if (!Version.TryParse(text, out Version? parsed) || parsed.Major < 0)
            {
                return false;
            }

            version = parsed;
            return true;
        }
    }
}

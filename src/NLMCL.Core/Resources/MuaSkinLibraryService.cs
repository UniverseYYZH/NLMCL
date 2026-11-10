using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// Blessing Skin（MUA 用户中心）的公开皮肤库。
    ///
    /// 列表：GET {root}/skinlib/list?page=N
    /// 原图：GET {root}/raw/{tid}
    /// 头像：GET {root}/avatar/{tid}
    ///
    /// 站点根地址由外置账号的 AuthServer 推导；微软账号使用默认 MUA。
    /// </summary>
    public sealed class MuaSkinLibraryService : IMuaSkinLibrary
    {
        public const string DefaultSiteRoot = "https://skin.mualliance.ltd";

        private static readonly HttpClient DirectHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private readonly IDownloadService _download;

        public MuaSkinLibraryService(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<MuaSkinPage> ListAsync(
            string siteRoot, int page, CancellationToken cancellationToken)
        {
            string root = ResolveSiteRoot(siteRoot);
            int safePage = Math.Max(1, page);

            string url = root + "/skinlib/list?page=" + safePage;
            string json;

            try
            {
                json = await _download
                    .GetStringAsync(url, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 启动器自带的下载栈在部分网络环境下会被代理/线程检查卡住；
                // 列表是公开 JSON，直接请求一次作为兜底。
                json = await GetStringDirectAsync(url, cancellationToken).ConfigureAwait(false);
            }

            JObject document;

            try
            {
                document = JObject.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("皮肤库返回的不是合法 JSON。");
            }

            List<MuaSkin> items = new List<MuaSkin>();

            if (document["data"] is JArray data)
            {
                foreach (JToken token in data)
                {
                    int tid = token.Value<int?>("tid") ?? 0;

                    if (tid <= 0)
                    {
                        continue;
                    }

                    string type = token.Value<string>("type") ?? "steve";
                    string name = token.Value<string>("name") ?? ("皮肤 #" + tid);
                    string uploader = token.Value<string>("nickname") ?? "未知";

                    items.Add(new MuaSkin(
                        tid,
                        name,
                        string.Equals(type, "alex", StringComparison.OrdinalIgnoreCase)
                            ? "slim"
                            : "default",
                        uploader,
                        token.Value<int?>("likes") ?? 0,
                        root + "/raw/" + tid,
                        root + "/avatar/" + tid));
                }
            }

            return new MuaSkinPage(
                items,
                document.Value<int?>("current_page") ?? safePage,
                Math.Max(1, document.Value<int?>("last_page") ?? safePage),
                document.Value<int?>("total") ?? items.Count);
        }

        public async Task<string> DownloadAsync(
            MuaSkin skin, string targetPath, CancellationToken cancellationToken)
        {
            if (skin == null)
            {
                throw new ArgumentNullException(nameof(skin));
            }

            try
            {
                await _download
                    .DownloadFileAsync(skin.DownloadUrl, targetPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                byte[] bytes = await GetBytesDirectAsync(skin.DownloadUrl, cancellationToken)
                    .ConfigureAwait(false);
                string? directory = Path.GetDirectoryName(targetPath);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(targetPath, bytes);
            }

            return targetPath;
        }

        private static async Task<string> GetStringDirectAsync(
            string url, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await DirectHttp
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> GetBytesDirectAsync(
            string url, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await DirectHttp
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        /// <summary>把 AuthServer（.../api/yggdrasil）还原为站点根地址。</summary>
        public static string ResolveSiteRoot(string? authServer)
        {
            if (string.IsNullOrWhiteSpace(authServer)
                || !Uri.TryCreate(authServer, UriKind.Absolute, out Uri? uri))
            {
                return DefaultSiteRoot;
            }

            string path = uri.AbsolutePath.TrimEnd('/');

            if (path.EndsWith("/api/yggdrasil", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/api/yggdrasil".Length);
            }
            else if (path.EndsWith("/yggdrasil", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/yggdrasil".Length);
            }

            return uri.GetLeftPart(UriPartial.Authority) + path;
        }
    }
}

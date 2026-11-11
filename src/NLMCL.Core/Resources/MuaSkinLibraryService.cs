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

        public Task<MuaSkinPage> ListAsync(
            string siteRoot, int page, CancellationToken cancellationToken)
        {
            return QueryAsync(
                siteRoot,
                new MuaSkinQuery { Page = Math.Max(1, page) },
                cancellationToken);
        }

        public async Task<MuaSkinPage> QueryAsync(
            string siteRoot, MuaSkinQuery query, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            string root = ResolveSiteRoot(siteRoot);
            int wantedPage = Math.Max(1, query.Page);

            if (query.Tid is int tid && tid > 0)
            {
                return await GetByTidAsync(root, tid, cancellationToken).ConfigureAwait(false);
            }

            // 皮肤站只支持单向排序（收藏/名称/上传时间都是降序）。升序时反向取页，
            // 再把页内顺序反过来，就能得到跨页的升序结果。
            if (query.Descending)
            {
                MuaSkinPage page = await FetchPageAsync(root, wantedPage, query, cancellationToken)
                    .ConfigureAwait(false);
                return new MuaSkinPage(page.Items, wantedPage, page.LastPage, page.Total);
            }

            int lastPage = query.KnownLastPage;
            MuaSkinPage? probe = null;

            if (lastPage <= 0)
            {
                probe = await FetchPageAsync(root, 1, query, cancellationToken).ConfigureAwait(false);
                lastPage = probe.LastPage;
            }

            int serverPage = Math.Max(1, lastPage - wantedPage + 1);
            MuaSkinPage result = probe != null && serverPage == 1
                ? probe
                : await FetchPageAsync(root, serverPage, query, cancellationToken).ConfigureAwait(false);

            List<MuaSkin> items = new List<MuaSkin>(result.Items.Count);

            for (int i = result.Items.Count - 1; i >= 0; i--)
            {
                items.Add(result.Items[i]);
            }

            return new MuaSkinPage(
                items,
                wantedPage,
                Math.Max(lastPage, result.LastPage),
                result.Total);
        }

        private async Task<MuaSkinPage> FetchPageAsync(
            string root, int page, MuaSkinQuery query, CancellationToken cancellationToken)
        {
            string url = root + "/skinlib/list?page=" + Math.Max(1, page);

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                url += "&keyword=" + Uri.EscapeDataString(query.Keyword.Trim());
            }

            if (query.UploaderId is int uploader && uploader > 0)
            {
                url += "&uploader=" + uploader;
            }

            if (string.Equals(query.Model, "slim", StringComparison.OrdinalIgnoreCase))
            {
                url += "&filter=alex";
            }
            else if (string.Equals(query.Model, "default", StringComparison.OrdinalIgnoreCase))
            {
                url += "&filter=steve";
            }

            switch (query.Sort)
            {
                case MuaSkinSort.MostFavorited:
                    url += "&sort=likes";
                    break;
                case MuaSkinSort.Name:
                    url += "&sort=name";
                    break;
                case MuaSkinSort.Newest:
                    // 站点默认就是按上传时间倒序，省略参数保持 URL 简洁。
                    break;
                default:
                    break;
            }

            string json = await GetStringWithFallbackAsync(url, cancellationToken).ConfigureAwait(false);
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
                    string uploaderName = token.Value<string>("nickname") ?? "未知";

                    items.Add(new MuaSkin(
                        tid,
                        name,
                        string.Equals(type, "alex", StringComparison.OrdinalIgnoreCase)
                            ? "slim"
                            : "default",
                        uploaderName,
                        token.Value<int?>("likes") ?? 0,
                        root + "/raw/" + tid,
                        root + "/avatar/" + tid,
                        token.Value<int?>("uploader") ?? 0));
                }
            }

            int serverPage = Math.Max(1, page);
            return new MuaSkinPage(
                items,
                serverPage,
                Math.Max(1, document.Value<int?>("last_page") ?? serverPage),
                document.Value<int?>("total") ?? items.Count);
        }

        private async Task<MuaSkinPage> GetByTidAsync(
            string root, int tid, CancellationToken cancellationToken)
        {
            string url = root + "/skinlib/show/" + tid;
            string html = await GetStringWithFallbackAsync(url, cancellationToken).ConfigureAwait(false);

            string name = "皮肤 #" + tid;
            System.Text.RegularExpressions.Match title = System.Text.RegularExpressions.Regex.Match(
                html,
                @"<title>\s*(.*?)\s*-\s*[^<]+</title>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.Singleline);

            if (title.Success)
            {
                string value = System.Net.WebUtility.HtmlDecode(title.Groups[1].Value).Trim();

                if (value.Length > 0)
                {
                    name = value;
                }
            }

            string uploader = "未知";
            System.Text.RegularExpressions.Match nickname = System.Text.RegularExpressions.Regex.Match(
                html,
                @"""nickname""\s*:\s*""([^""]*)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (nickname.Success)
            {
                uploader = System.Net.WebUtility.HtmlDecode(nickname.Groups[1].Value);
            }

            MuaSkin fallback = new MuaSkin(
                tid,
                name,
                "default",
                uploader,
                0,
                root + "/raw/" + tid,
                root + "/avatar/" + tid);

            // show 页面不含收藏数/类型/作者 ID。再用名称搜一次列表，按 tid 取回完整字段；
            // 找不到或网络失败时仍返回 show 页面解析出的结果。
            try
            {
                MuaSkinPage search = await FetchPageAsync(
                    root, 1, new MuaSkinQuery { Keyword = name }, cancellationToken)
                    .ConfigureAwait(false);

                foreach (MuaSkin item in search.Items)
                {
                    if (item.Tid == tid)
                    {
                        return new MuaSkinPage(new[] { item }, 1, 1, 1);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 列表补全失败不影响按 ID 查看皮肤。
            }

            return new MuaSkinPage(new[] { fallback }, 1, 1, 1);
        }

        private async Task<string> GetStringWithFallbackAsync(
            string url, CancellationToken cancellationToken)
        {
            try
            {
                return await _download
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
                // 公开 JSON/HTML 直接请求一次作为兜底。
                return await GetStringDirectAsync(url, cancellationToken).ConfigureAwait(false);
            }
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

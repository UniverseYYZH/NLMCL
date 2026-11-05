using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// CurseForge 兜底安装（走 cfwidget 这个第三方只读接口）。
    ///
    /// 流程：
    ///   1. 从链接里取 slug（www.curseforge.com/minecraft/mc-mods/jei → jei）
    ///   2. GET https://api.cfwidget.com/minecraft/mc-mods/&lt;slug&gt; 拿文件清单
    ///   3. 按「游戏版本 + 加载器」挑第一个匹配的文件
    ///   4. 用文件 id 组装 ForgeCDN 直链并下载（实测 206 可用，不需要 referer）
    ///
    /// 限制：cfwidget 没有搜索接口，所以只能"按链接装"，不能像 Modrinth 那样搜索。
    /// </summary>
    public sealed class CurseForgeInstaller : ICurseForgeInstaller
    {
        private static readonly Regex SlugPattern = new Regex(
            @"curseforge\.com/minecraft/(?<type>mc-mods|texture-packs|worlds|modpacks|customization)/(?<slug>[^/?#]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IDownloadService _downloads;
        private readonly IFileDownloader _files;

        public CurseForgeInstaller(IDownloadService downloads, IFileDownloader files)
        {
            _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
            _files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public async Task<string> InstallFileAsync(
            string url,
            string fileName,
            ResourceKind kind,
            string minecraftDirectory,
            string versionFolder,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            string safeName = string.IsNullOrWhiteSpace(fileName) ? "curseforge-file.jar" : fileName;

            // mcim 返回的是 edge.forgecdn.net，换成 mediafilez 才能直连下载 + 分块
            if (url.IndexOf("edge.forgecdn.net", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                url = url.Replace("edge.forgecdn.net", "mediafilez.forgecdn.net");
            }
            string dir = Path.Combine(minecraftDirectory, "versions", versionFolder, SubDirectoryOf(kind));
            Directory.CreateDirectory(dir);

            string target = Path.Combine(dir, safeName);
            string temp = target + ".nlmcl-tmp";

            await _files.DownloadAsync(url, temp, progress, cancellationToken).ConfigureAwait(false);
            File.Copy(temp, target, overwrite: true);
            File.Delete(temp);

            return target;
        }

        private static string SubDirectoryOf(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.ResourcePack:
                    return "resourcepacks";
                case ResourceKind.ShaderPack:
                    return "shaderpacks";
                case ResourceKind.DataPack:
                    return "datapacks";
                default:
                    return "mods";
            }
        }

        public async Task<string> InstallAsync(
            string projectUrl,
            string minecraftDirectory,
            string versionFolder,
            string? gameVersion,
            string? loader,
            CancellationToken cancellationToken)
        {
            string slug = ExtractSlug(projectUrl);
            string type = ExtractType(projectUrl);

            string json = await _downloads
                .GetStringAsync("https://api.cfwidget.com/minecraft/" + type + "/" + slug, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(json);
            string title = root.Value<string>("title") ?? slug;

            JArray? files = root["files"] as JArray;

            if (files == null || files.Count == 0)
            {
                throw new InvalidOperationException("「" + title + "」没有可下载的文件（项目可能被作者设为外部下载）");
            }

            JObject? picked = PickFile(files, gameVersion, loader);

            if (picked == null)
            {
                throw new InvalidOperationException(
                    "「" + title + "」没有匹配 " + (gameVersion ?? "任意版本")
                    + (string.IsNullOrEmpty(loader) ? string.Empty : " + " + loader) + " 的文件");
            }

            long id = picked.Value<long>("id");
            string name = picked.Value<string>("name") ?? ("curseforge-" + id + ".jar");
            long size = picked.Value<long?>("filesize") ?? 0;

            string url = BuildDownloadUrl(id, name);

            string mods = Path.Combine(minecraftDirectory, "versions", versionFolder, "mods");
            Directory.CreateDirectory(mods);

            string target = Path.Combine(mods, name);

            await _downloads.DownloadFileAsync(url, target, cancellationToken).ConfigureAwait(false);

            FileInfo info = new FileInfo(target);

            if (size > 0 && info.Length != size)
            {
                throw new InvalidOperationException(
                    "下载不完整：" + info.Length + " / " + size + " 字节（可以重试一次）");
            }

            return target;
        }

        /// <summary>项目类型（cfwidget 路径里的那一段）；默认 mc-mods。</summary>
        internal static string ExtractType(string input)
        {
            Match match = SlugPattern.Match(input ?? string.Empty);

            return match.Success ? match.Groups["type"].Value.ToLowerInvariant() : "mc-mods";
        }

        /// <summary>从项目链接里取 slug；也接受直接输入 slug。</summary>
        internal static string ExtractSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("请填入 CurseForge 项目链接", nameof(input));
            }

            string text = input.Trim();

            Match match = SlugPattern.Match(text);

            if (match.Success)
            {
                return match.Groups["slug"].Value.TrimEnd('/');
            }

            // 用户可能只粘贴了 slug
            return text.Trim('/');
        }

        /// <summary>
        /// ForgeCDN 的分片规则：文件 id 前几位 / 后三位 / 文件名。
        /// 例：9407746 → /files/9407/746/xxx.jar
        /// </summary>
        internal static string BuildDownloadUrl(long fileId, string fileName)
        {
            long head = fileId / 1000;
            long tail = fileId % 1000;

            // 统一用 mediafilez：edge.forgecdn.net 对 GET 会直接 404（只对 HEAD 返回 302），
            // 而 mediafilez 支持 Range，6 路并行才能生效。
            return "https://mediafilez.forgecdn.net/files/"
                + head.ToString(CultureInfo.InvariantCulture) + "/"
                + tail.ToString(CultureInfo.InvariantCulture) + "/"
                + Uri.EscapeDataString(fileName);
        }

        /// <summary>
        /// 挑文件：优先「版本 + 加载器」都对上；不行就只按版本；再不行取最新的。
        /// cfwidget 的 versions 数组形如 ["Client","Forge","Server","1.12.2"]。
        /// </summary>
        internal static JObject? PickFile(JArray files, string? gameVersion, string? loader)
        {
            string? wantedLoader = string.IsNullOrWhiteSpace(loader) ? null : loader!.ToLowerInvariant();

            JObject? versionAndLoader = null;
            JObject? versionOnly = null;

            foreach (JToken token in files)
            {
                if (token is not JObject file)
                {
                    continue;
                }

                List<string> tags = new List<string>();

                if (file["versions"] is JArray versions)
                {
                    foreach (JToken tag in versions)
                    {
                        string? value = tag.Value<string>();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            tags.Add(value!.ToLowerInvariant());
                        }
                    }
                }

                bool versionOk = string.IsNullOrWhiteSpace(gameVersion)
                    || tags.Contains(gameVersion!.ToLowerInvariant());

                if (!versionOk)
                {
                    continue;
                }

                bool loaderOk = wantedLoader != null && tags.Contains(wantedLoader);

                if (loaderOk && versionAndLoader == null)
                {
                    versionAndLoader = file;
                }

                if (versionOnly == null)
                {
                    versionOnly = file;
                }
            }

            return versionAndLoader ?? versionOnly ?? (files.Count > 0 ? files[0] as JObject : null);
        }
    }
}

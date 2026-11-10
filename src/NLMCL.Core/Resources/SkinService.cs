using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;
using NLMCL.Core.Skins;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// 皮肤读取与保存。
    ///
    /// 外置登录（MUA 联合认证 / 皮肤站）：GET <c>{apiRoot}/sessionserver/session/minecraft/profile/{uuid}</c>
    /// 返回 <c>properties[{name:"textures", value:"&lt;base64 JSON&gt;"}]</c>，
    /// base64 解开后是 <c>{"textures":{"SKIN":{"url":"...","metadata":{"model":"slim"}}}}</c>。
    ///
    /// 微软账号：<c>GET api.minecraftservices.com/minecraft/profile</c>（带 Bearer），结构相同。
    /// </summary>
    public sealed class SkinService : ISkinService
    {
        public const string MicrosoftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

        /// <summary>按玩家名查正版 UUID（离线账号用）。</summary>
        public const string MojangNameLookupUrl = "https://api.mojang.com/users/profiles/minecraft/";

        /// <summary>会话服务器的档案接口（拿 textures）。</summary>
        public const string SessionserverUrl = "https://sessionserver.mojang.com/session/minecraft/profile/";

        private readonly IDownloadService _download;
        private readonly IAppPaths _paths;
        private readonly object _localSkinGate = new object();

        public SkinService(IDownloadService download, IAppPaths paths)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public async Task<SkinInfo?> GetAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                return null;
            }

            if (account.Type == AccountType.External && !string.IsNullOrWhiteSpace(account.AuthServer))
            {
                string uuid = account.Uuid.Replace("-", string.Empty);
                string url = account.AuthServer!.TrimEnd('/')
                    + "/sessionserver/session/minecraft/profile/" + uuid;

                string json = await _download.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

                return ParseProfile(json, account.Name, account.AuthServer);
            }

            if (account.Type == AccountType.Microsoft && !string.IsNullOrEmpty(account.AccessToken))
            {
                Dictionary<string, string> headers = new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer " + account.AccessToken
                };

                string json = await _download
                    .GetStringAsync(MicrosoftProfileUrl, headers, cancellationToken)
                    .ConfigureAwait(false);

                return ParseProfile(json, account.Name, "Minecraft Services");
            }

            // 离线账号默认没有皮肤；仅在用户点「查同名正版皮肤」时才去 Mojang 查
            // （自动按名字匹配会冒用他人皮肤，观感上也容易误会，见 FIX-14 讨论）。

            return null;
        }

        /// <summary>
        /// 用玩家名向 Mojang 查正版档案（离线账号往往就是玩家自己的正版名）。
        /// 名字不存在 / 网络失败都返回 null——不编造皮肤。
        /// </summary>
        public async Task<SkinInfo?> LookupByNameAsync(string playerName, CancellationToken cancellationToken)
        {
            try
            {
                string lookupUrl = MojangNameLookupUrl + Uri.EscapeDataString(playerName.Trim());
                string json = await _download.GetStringAsync(lookupUrl, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                JObject root = JObject.Parse(json);
                string? id = root.Value<string>("id");

                if (string.IsNullOrWhiteSpace(id))
                {
                    return null;
                }

                string profileUrl = SessionserverUrl + id;
                string profileJson = await _download
                    .GetStringAsync(profileUrl, cancellationToken).ConfigureAwait(false);

                return ParseProfile(profileJson, playerName, "Mojang（按名字匹配）");
            }
            catch (Exception)
            {
                // 名字不存在（404）或接口异常：当作"没有皮肤"
                return null;
            }
        }

        public async Task<string> SaveAsync(SkinInfo skin, CancellationToken cancellationToken)
        {
            if (skin == null)
            {
                throw new ArgumentNullException(nameof(skin));
            }

            if (string.IsNullOrWhiteSpace(skin.SkinUrl))
            {
                throw new InvalidOperationException("这个账号没有皮肤可保存。");
            }

            Directory.CreateDirectory(_paths.SkinDirectory);

            string name = SafeName(skin.PlayerName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
            string target = Path.Combine(_paths.SkinDirectory, name);

            await _download.DownloadFileAsync(skin.SkinUrl!, target, cancellationToken).ConfigureAwait(false);

            return target;
        }

        public async Task SavePreviewAsync(string url, string targetPath, CancellationToken cancellationToken)
        {
            await _download.DownloadFileAsync(url, targetPath, cancellationToken).ConfigureAwait(false);
        }

        public Task<string> ImportLocalAsync(
            string sourcePath, string playerName, CancellationToken cancellationToken)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("找不到皮肤文件。", sourcePath);
            }

            if (!HasPngHeader(sourcePath))
            {
                throw new InvalidDataException("不是有效的 PNG 文件（皮肤必须是 PNG）。");
            }

            Directory.CreateDirectory(_paths.SkinDirectory);

            string name = SafeName(playerName) + "-local-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
            string target = Path.Combine(_paths.SkinDirectory, name);

            File.Copy(sourcePath, target, overwrite: true);

            return Task.FromResult(target);
        }

        public async Task<string> ImportLocalAsync(
            Account account, string sourcePath, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            string saved = await ImportLocalAsync(sourcePath, account.Name, cancellationToken)
                .ConfigureAwait(false);

            RememberLocalSkin(account, saved);
            return saved;
        }

        public Task<string?> GetLocalSkinPathAsync(
            Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                return Task.FromResult<string?>(null);
            }

            lock (_localSkinGate)
            {
                Dictionary<string, string> map = ReadLocalSkinMap();

                if (map.TryGetValue(AccountKey(account), out string? mapped)
                    && !string.IsNullOrWhiteSpace(mapped)
                    && File.Exists(mapped))
                {
                    return Task.FromResult<string?>(mapped);
                }
            }

            // 兼容旧版本：当时只把文件写进 skins 目录，还没有 manifest。
            return Task.FromResult(FindLatestLocalSkin(account));
        }

        private string LocalSkinMapPath => Path.Combine(_paths.SkinDirectory, "local-skins.json");

        private static string AccountKey(Account account)
        {
            string uuid = (account.Uuid ?? string.Empty).Replace("-", string.Empty).Trim();
            return uuid.Length > 0 ? uuid.ToLowerInvariant() : SafeName(account.Name).ToLowerInvariant();
        }

        private void RememberLocalSkin(Account account, string path)
        {
            Directory.CreateDirectory(_paths.SkinDirectory);

            lock (_localSkinGate)
            {
                Dictionary<string, string> map = ReadLocalSkinMap();
                map[AccountKey(account)] = path;

                JObject root = new JObject
                {
                    ["version"] = 1
                };

                JObject accounts = new JObject();

                foreach (KeyValuePair<string, string> pair in map)
                {
                    accounts[pair.Key] = pair.Value;
                }

                root["accounts"] = accounts;

                string temp = LocalSkinMapPath + ".tmp";
                File.WriteAllText(temp, root.ToString(), new UTF8Encoding(false));

                if (File.Exists(LocalSkinMapPath))
                {
                    File.Replace(temp, LocalSkinMapPath, null);
                }
                else
                {
                    File.Move(temp, LocalSkinMapPath);
                }
            }
        }

        private Dictionary<string, string> ReadLocalSkinMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(LocalSkinMapPath))
                {
                    return map;
                }

                JObject root = JObject.Parse(File.ReadAllText(LocalSkinMapPath, Encoding.UTF8));
                JObject? accounts = root["accounts"] as JObject;

                if (accounts == null)
                {
                    return map;
                }

                foreach (JProperty property in accounts.Properties())
                {
                    string? path = property.Value.Type == JTokenType.String
                        ? property.Value.ToString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        map[property.Name] = path!;
                    }
                }
            }
            catch (Exception)
            {
                // manifest 损坏就当没有；旧文件兜底还能找到皮肤
            }

            return map;
        }

        private string? FindLatestLocalSkin(Account account)
        {
            try
            {
                if (!Directory.Exists(_paths.SkinDirectory))
                {
                    return null;
                }

                string prefix = SafeName(account.Name) + "-local-";

                string? newest = Directory.GetFiles(_paths.SkinDirectory, "*.png")
                    .Where(file => Path.GetFileName(file)
                        .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                return newest != null && File.Exists(newest) ? newest : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken)
        {
            string output = Path.Combine(
                Path.GetTempPath(),
                "nlmcl-skin-doll-" + DateTime.Now.Ticks + ".png");

            SkinPreviewRenderer.RenderPaperDoll(skinPngPath, output, 8);

            return Task.FromResult(output);
        }

        public Task<byte[]> Create3DPreviewAsync(
            string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
            CancellationToken cancellationToken)
        {
            // 光栅化是 CPU 密集的（320² × 2×2 超采样），放到线程池，避免自动旋转时卡 UI。
            // 直接返回 PNG 字节，不产生临时文件。
            return Task.Run(
                () => SkinRender3D.RenderPng(skinPngPath, 320, yawDegrees, pitchDegrees, slim),
                cancellationToken);
        }

        /// <summary>
        /// 内置默认皮肤（经典 Steve 布局）的临时文件路径。
        /// 账号没有自定义皮肤时用它兜底：头像与预览都要有东西可看，而不是留个 "?"。
        /// </summary>
        /// <summary>官方 Steve 皮肤的下载地址（mc-heads 直接给 64x64 PNG）。</summary>
        public const string DefaultSkinUrl = "https://mc-heads.net/skin/Steve";

        public async Task<string> GetDefaultSkinAsync(CancellationToken cancellationToken)
        {
            string target = GetDefaultSkinPath();
            string cached = Path.Combine(Path.GetTempPath(), "nlmcl-default-skin-downloaded.png");

            if (File.Exists(cached) && new FileInfo(cached).Length > 1024 && HasPngHeader(cached))
            {
                return cached;
            }

            try
            {
                await _download.DownloadFileAsync(DefaultSkinUrl, cached, cancellationToken).ConfigureAwait(false);

                if (File.Exists(cached) && HasPngHeader(cached) && new FileInfo(cached).Length > 1024)
                {
                    return cached;
                }
            }
            catch (Exception)
            {
                // 联网失败就用内置副本（内置的也是真实 Steve 皮肤，不是生成的）
            }

            return target;
        }

        public string GetDefaultSkinPath()
        {
            string target = Path.Combine(Path.GetTempPath(), "nlmcl-default-skin.png");

            // 太小的文件是早先版本生成的占位图，必须重新解出真实皮肤
            if (File.Exists(target) && new FileInfo(target).Length > 1024 && HasPngHeader(target))
            {
                return target;
            }

            using (Stream? resource = typeof(SkinService).Assembly
                .GetManifestResourceStream("NLMCL.Core.Assets.default-skin.png"))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("默认皮肤资源缺失（NLMCL.Core.Assets.default-skin.png）");
                }

                using (FileStream file = File.Create(target))
                {
                    resource.CopyTo(file);
                }
            }

            return target;
        }

        public async Task<string?> GetAvatarAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                return null;
            }

            // 同一个账号会被侧栏、账号列表、皮肤页同时请求头像；
            // 用唯一文件名，避免并发写同一个 PNG 时互相覆盖/触发 GDI+ 占用错误。
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 10);

            string? localSkin = await GetLocalSkinPathAsync(account, cancellationToken)
                .ConfigureAwait(false);

            string? source = null;

            if (account.Type == AccountType.Offline && localSkin != null)
            {
                // 离线账号没有服务端皮肤：优先用用户自己保存的那张。
                source = localSkin;
            }
            else
            {
                SkinInfo? skin = null;

                try
                {
                    skin = await GetAsync(account, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 皮肤接口挂了不该让头像消失：退回本地皮肤/默认皮肤
                }

                if (skin?.SkinUrl != null)
                {
                    try
                    {
                        string downloaded = Path.Combine(
                            Path.GetTempPath(),
                            "nlmcl-skin-src-" + AccountKey(account) + "-" + requestId + ".png");

                        await _download
                            .DownloadFileAsync(skin.SkinUrl, downloaded, cancellationToken)
                            .ConfigureAwait(false);

                        source = downloaded;
                    }
                    catch (Exception)
                    {
                        source = localSkin;
                    }
                }
                else
                {
                    source = localSkin;
                }
            }

            if (source == null)
            {
                source = await GetDefaultSkinAsync(cancellationToken).ConfigureAwait(false);
            }

            string avatar = Path.Combine(
                Path.GetTempPath(),
                "nlmcl-avatar-" + AccountKey(account) + "-" + requestId + ".png");

            try
            {
                SkinAvatarRenderer.RenderHead(source, avatar, 8);
                return avatar;
            }
            catch (Exception)
            {
                // 官方皮肤 URL 偶尔会返回损坏/HTML 内容；再用默认 Steve 渲染一次，
                // 不要因为一张坏图把侧栏头像变成空白。
                try
                {
                    string fallback = await GetDefaultSkinAsync(cancellationToken)
                        .ConfigureAwait(false);

                    SkinAvatarRenderer.RenderHead(fallback, avatar, 8);
                    return avatar;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        // ---------- 内置预设皮肤 ----------

        private static readonly SkinPreset[] PresetTable =
        {
            new SkinPreset("steve", "Steve（经典）", "default", 64),
            new SkinPreset("alex", "Alex（纤细）", "slim", 64),
            new SkinPreset("notch", "Notch（经典）", "default", 32),
            new SkinPreset("jeb", "jeb_（经典）", "default", 32)
        };

        public IReadOnlyList<SkinPreset> GetPresets() => PresetTable;

        public string GetPresetPath(string presetId)
        {
            SkinPreset? preset = PresetTable.FirstOrDefault(
                p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));

            if (preset == null)
            {
                throw new InvalidOperationException("没有这个预设皮肤：" + presetId);
            }

            string directory = Path.Combine(Path.GetTempPath(), "nlmcl-presets");
            Directory.CreateDirectory(directory);

            string target = Path.Combine(directory, preset.Id + ".png");

            if (File.Exists(target) && new FileInfo(target).Length > 256)
            {
                return target;
            }

            using (Stream? resource = typeof(SkinService).Assembly
                .GetManifestResourceStream("NLMCL.Core.Assets.presets." + preset.Id + ".png"))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("预设皮肤资源缺失：" + preset.Id);
                }

                using (FileStream file = File.Create(target))
                {
                    resource.CopyTo(file);
                }
            }

            return target;
        }

        public Task<string> SavePresetAsync(string presetId, string playerName, CancellationToken cancellationToken)
        {
            string source = GetPresetPath(presetId);

            Directory.CreateDirectory(_paths.SkinDirectory);

            string name = SafeName(playerName) + "-preset-" + presetId + "-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";

            string target = Path.Combine(_paths.SkinDirectory, name);
            File.Copy(source, target, overwrite: true);

            return Task.FromResult(target);
        }
        /// <summary>PNG 魔数：89 50 4E 47 0D 0A 1A 0A。只认 PNG，避免把 jpg 改名混进来。</summary>
        internal static bool HasPngHeader(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] header = new byte[8];
                    int read = stream.Read(header, 0, header.Length);

                    if (read < header.Length)
                    {
                        return false;
                    }

                    return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                        && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>解析 profile JSON 里的 textures 属性；没有皮肤时返回带 null 地址的 SkinInfo。</summary>
        internal static SkinInfo? ParseProfile(string json, string fallbackName, string source)
        {
            JObject root;

            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("皮肤接口返回的不是合法 JSON。");
            }

            string name = root.Value<string>("name") ?? fallbackName;

            JToken? property = (root["properties"] as JArray)?
                .FirstOrDefault(item => string.Equals(
                    item.Value<string>("name"), "textures", StringComparison.OrdinalIgnoreCase));

            if (property != null)
            {
                string? value = property.Value<string>("value");

                if (!string.IsNullOrWhiteSpace(value))
                {
                    SkinInfo parsed = ParseTexturesProperty(value!, name, source);

                    // 老格式（Mojang sessionserver / Yggdrasil）能解析就直接用。
                    if (parsed.SkinUrl != null)
                    {
                        return parsed;
                    }
                }
            }

            // 新版 Minecraft Services 的 /minecraft/profile 不再返回
            // properties[textures]，而是在顶层给 skins / capes 数组：
            //   "skins":[{"url":"...","variant":"CLASSIC","state":"ACTIVE"}]
            if (root["skins"] is JArray skins && skins.Count > 0)
            {
                JToken? active = skins.FirstOrDefault(item => string.Equals(
                    item.Value<string>("state"), "ACTIVE", StringComparison.OrdinalIgnoreCase));
                JToken selected = active ?? skins[0];

                string? url = selected.Value<string>("url");
                string variant = selected.Value<string>("variant") ?? "CLASSIC";

                if (!string.IsNullOrWhiteSpace(url))
                {
                    return new SkinInfo(
                        name,
                        NormalizeTextureUrl(url),
                        string.Equals(variant, "SLIM", StringComparison.OrdinalIgnoreCase)
                            ? "slim"
                            : "default",
                        source);
                }
            }

            return new SkinInfo(name, null, "default", source);
        }

        /// <summary>base64 → JSON → 皮肤 URL 与模型。</summary>
        internal static SkinInfo ParseTexturesProperty(string base64, string playerName, string source)
        {
            string decoded;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                decoded = Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return new SkinInfo(playerName, null, "default", source);
            }

            try
            {
                JObject root = JObject.Parse(decoded);
                JToken? skin = root.SelectToken("textures.SKIN");

                string? url = skin?.Value<string>("url");
                string model = skin?.SelectToken("metadata.model")?.Value<string>() ?? "default";

                return new SkinInfo(playerName, NormalizeTextureUrl(url), model, source);
            }
            catch (Exception)
            {
                return new SkinInfo(playerName, null, "default", source);
            }
        }

        private static string? NormalizeTextureUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            // Mojang 的 profile 仍返回 http://textures.minecraft.net/...；
            // 用 https 下载更稳，也避免中间网络设备拦截明文 HTTP。
            const string httpPrefix = "http://textures.minecraft.net/";

            if (url.StartsWith(httpPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return "https://textures.minecraft.net/" + url.Substring(httpPrefix.Length);
            }

            return url;
        }

        private static string SafeName(string name)
        {
            StringBuilder builder = new StringBuilder();

            foreach (char ch in (name ?? "player").Trim())
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            }

            string result = builder.ToString();
            return result.Length == 0 ? "player" : result;
        }
    }
}

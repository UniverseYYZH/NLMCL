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

            // 离线账号：名字可能正好是某个正版账号，按名字查一次 Mojang。
            // 查不到就返回 null（界面显示"没有皮肤"，不伪造）。
            if (account.Type == AccountType.Offline && !string.IsNullOrWhiteSpace(account.Name))
            {
                return await LookupOfflineByNameAsync(account.Name, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// 用玩家名向 Mojang 查正版档案（离线账号往往就是玩家自己的正版名）。
        /// 名字不存在 / 网络失败都返回 null——不编造皮肤。
        /// </summary>
        internal async Task<SkinInfo?> LookupOfflineByNameAsync(string playerName, CancellationToken cancellationToken)
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

        public Task<string> CreatePreviewAsync(string skinPngPath, CancellationToken cancellationToken)
        {
            string output = Path.Combine(
                Path.GetTempPath(),
                "nlmcl-skin-doll-" + DateTime.Now.Ticks + ".png");

            SkinPreviewRenderer.RenderPaperDoll(skinPngPath, output, 8);

            return Task.FromResult(output);
        }

        public async Task<string> Create3DPreviewAsync(
            string skinPngPath, double yawDegrees, double pitchDegrees, bool slim,
            CancellationToken cancellationToken)
        {
            string output = Path.Combine(
                Path.GetTempPath(),
                "nlmcl-skin-3d-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");

            // 光栅化是 CPU 密集的（320² × 2×2 超采样），放到线程池，避免自动旋转时卡 UI
            await Task.Run(
                () => SkinRender3D.Render(skinPngPath, output, 320, yawDegrees, pitchDegrees, slim),
                cancellationToken).ConfigureAwait(false);

            return output;
        }

        /// <summary>
        /// 内置默认皮肤（经典 Steve 布局）的临时文件路径。
        /// 账号没有自定义皮肤时用它兜底：头像与预览都要有东西可看，而不是留个 "?"。
        /// </summary>
        public string GetDefaultSkinPath()
        {
            string target = Path.Combine(Path.GetTempPath(), "nlmcl-default-skin.png");

            if (File.Exists(target) && new FileInfo(target).Length > 256)
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
            SkinInfo? skin;

            try
            {
                skin = await GetAsync(account, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 皮肤接口挂了不该让头像消失：退回默认皮肤
                skin = null;
            }

            string source;

            if (skin == null || string.IsNullOrWhiteSpace(skin.SkinUrl))
            {
                source = GetDefaultSkinPath();
            }
            else
            {
                source = Path.Combine(
                    Path.GetTempPath(), "nlmcl-skin-src-" + SafeName(account.Name) + ".png");

                await _download.DownloadFileAsync(skin.SkinUrl!, source, cancellationToken).ConfigureAwait(false);
            }

            string avatar = Path.Combine(
                Path.GetTempPath(), "nlmcl-avatar-" + SafeName(account.Name) + ".png");

            SkinAvatarRenderer.RenderHead(source, avatar, 8);

            return avatar;
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

            if (property == null)
            {
                return new SkinInfo(name, null, "default", source);
            }

            string? value = property.Value<string>("value");

            if (string.IsNullOrWhiteSpace(value))
            {
                return new SkinInfo(name, null, "default", source);
            }

            return ParseTexturesProperty(value!, name, source);
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

                return new SkinInfo(playerName, url, model, source);
            }
            catch (Exception)
            {
                return new SkinInfo(playerName, null, "default", source);
            }
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

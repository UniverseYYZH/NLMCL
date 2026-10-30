using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using Newtonsoft.Json.Linq;
using NLCL.Abstractions;

namespace NLCL.Core.Accounts
{
    /// <summary>
    /// 微软账号登录。
    ///
    /// 流程（与 PCL 的 <c>ModLaunch.vb</c> L881+ 一致）：
    ///   1. POST devicecode         → 拿 user_code 给用户去浏览器输入
    ///   2. 轮询 POST token          → 拿 OAuth access_token
    ///   3. POST user.auth.xboxlive  → XBL token + uhs
    ///   4. POST xsts.auth.xboxlive  → XSTS token
    ///   5. POST login_with_xbox     → Minecraft access_token
    ///   6. GET  /minecraft/profile  → 真正的 UUID 与名字
    ///
    /// **client_id 不内置**：与 PCL 一样从配置里读（他自己也是读 <c>PCL_MS_CLIENT_ID</c>）。
    /// 每个用户 / 每个分支要注册自己的 Azure 应用并勾选"允许公共客户端流"。
    /// 传输全部走 <see cref="IDownloadService"/>（运行时是 PCL 的 ModNet），不另写 HTTP。
    /// </summary>
    public sealed class MicrosoftAuthenticator : IMicrosoftAuthenticator
    {
        public const string DeviceCodeUrl =
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        public const string TokenUrl =
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

        private const string XboxLiveUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string MinecraftLoginUrl =
            "https://api.minecraftservices.com/authentication/login_with_xbox";

        private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

        private const string Scope = "XboxLive.signin offline_access";

        private readonly IDownloadService _download;

        public MicrosoftAuthenticator(IDownloadService download, string? clientId)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId!.Trim();
        }

        /// <summary>
        /// Azure 应用的 client id。运行期可改：设置页填的值在登录前会覆盖构造时的环境变量。
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>是否配置了 client_id。没配置时界面应提示，而不是让用户点进死胡同。</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

        private void EnsureConfigured()
        {
            if (IsConfigured)
            {
                return;
            }

            throw new InvalidOperationException(
                "缺少微软应用 ID（client_id）。请设置环境变量 NLCL_MS_CLIENT_ID，" +
                "值为你在 Azure 注册的公共客户端应用 ID（需开启「允许公共客户端流」）。");
        }

        public async Task<DeviceCodeChallenge> StartAsync(CancellationToken cancellationToken)
        {
            EnsureConfigured();

            Dictionary<string, string> fields = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["tenant"] = "/consumers",
                ["scope"] = Scope
            };

            string response = await _download
                .PostFormAsync(DeviceCodeUrl, fields, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(response);

            string? deviceCode = root.Value<string>("device_code");
            string? userCode = root.Value<string>("user_code");
            string? uri = root.Value<string>("verification_uri");
            int interval = root.Value<int?>("interval") ?? 5;
            int expiresIn = root.Value<int?>("expires_in") ?? 900;

            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode))
            {
                throw new InvalidOperationException(
                    "设备码响应缺少字段：" + Truncate(response));
            }

            return new DeviceCodeChallenge(
                deviceCode!,
                userCode!,
                uri ?? "https://microsoft.com/link",
                interval,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }

        public async Task<Account> WaitForAccountAsync(
            DeviceCodeChallenge challenge,
            CancellationToken cancellationToken)
        {
            if (challenge == null)
            {
                throw new ArgumentNullException(nameof(challenge));
            }

            (string oauthToken, string? refreshToken) = await
                PollForTokenAsync(challenge, cancellationToken).ConfigureAwait(false);

            (string xblToken, string userHash) = await
                AuthenticateXboxLiveAsync(oauthToken, cancellationToken).ConfigureAwait(false);

            string xstsToken = await AuthorizeXstsAsync(xblToken, cancellationToken).ConfigureAwait(false);

            string minecraftToken = await LoginWithXboxAsync(userHash, xstsToken, cancellationToken)
                .ConfigureAwait(false);

            Account account = await FetchProfileAsync(minecraftToken, cancellationToken).ConfigureAwait(false);

            // 带上刷新令牌：下次过期可以静默续期
            return new Account(account.Name, account.Uuid, account.Type, account.AccessToken, refreshToken);
        }

        /// <summary>
        /// 用 refresh_token 续期。微软会在响应里给新的 refresh_token，
        /// 因此返回的账号必须整体替换旧的（旧 refresh_token 可能已失效）。
        /// </summary>
        public async Task<Account> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException("缺少刷新令牌。", nameof(refreshToken));
            }

            Dictionary<string, string> fields = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = Scope
            };

            string response = await _download
                .PostFormAsync(TokenUrl, fields, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(response);

            string? error = root.Value<string>("error");
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(
                    "续期失败：" + error + " " + (root.Value<string>("error_description") ?? ""));
            }

            string? oauthToken = root.Value<string>("access_token");
            if (string.IsNullOrEmpty(oauthToken))
            {
                throw new InvalidOperationException("续期响应缺少 access_token。");
            }

            string? newRefresh = root.Value<string>("refresh_token") ?? refreshToken;

            (string xblToken, string userHash) = await
                AuthenticateXboxLiveAsync(oauthToken!, cancellationToken).ConfigureAwait(false);

            string xstsToken = await AuthorizeXstsAsync(xblToken, cancellationToken).ConfigureAwait(false);
            string minecraftToken = await LoginWithXboxAsync(userHash, xstsToken, cancellationToken)
                .ConfigureAwait(false);

            Account fresh = await FetchProfileAsync(minecraftToken, cancellationToken).ConfigureAwait(false);
            return new Account(fresh.Name, fresh.Uuid, fresh.Type, fresh.AccessToken, newRefresh);
        }

        /// <summary>轮询 token 端点直到用户完成授权。</summary>
        private async Task<(string AccessToken, string? RefreshToken)> PollForTokenAsync(
            DeviceCodeChallenge challenge,
            CancellationToken cancellationToken)
        {
            int interval = challenge.IntervalSeconds;

            while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Dictionary<string, string> fields = new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = challenge.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["scope"] = Scope
                };

                string response = await _download
                    .PostFormAsync(TokenUrl, fields, cancellationToken)
                    .ConfigureAwait(false);

                JObject root = JObject.Parse(response);
                string? error = root.Value<string>("error");

                if (string.IsNullOrEmpty(error))
                {
                    string? token = root.Value<string>("access_token");
                    if (!string.IsNullOrEmpty(token))
                    {
                        return (token!, root.Value<string>("refresh_token"));
                    }

                    throw new InvalidOperationException("令牌响应既没有 access_token 也没有 error。");
                }

                switch (error)
                {
                    case "authorization_pending":
                        break;

                    case "slow_down":
                        interval += 5;
                        break;

                    case "expired_token":
                        throw new TimeoutException("设备码已过期，请重新登录。");

                    case "authorization_declined":
                        throw new OperationCanceledException("用户拒绝了授权。");

                    default:
                        throw new InvalidOperationException(
                            "登录失败：" + error + " " + (root.Value<string>("error_description") ?? ""));
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("等待授权超时，请重新登录。");
        }

        private async Task<(string Token, string UserHash)> AuthenticateXboxLiveAsync(
            string oauthToken,
            CancellationToken cancellationToken)
        {
            JObject body = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = "d=" + oauthToken
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT"
            };

            string response = await _download
                .PostJsonAsync(XboxLiveUrl, body.ToString(), null, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(response);
            string? token = root.Value<string>("Token");
            string? userHash = root.SelectToken("DisplayClaims.xui[0].uhs")?.Value<string>();

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userHash))
            {
                throw new InvalidOperationException("Xbox Live 认证响应缺少 Token 或 uhs：" + Truncate(response));
            }

            return (token!, userHash!);
        }

        private async Task<string> AuthorizeXstsAsync(
            string xblToken,
            CancellationToken cancellationToken)
        {
            JObject body = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = new JArray(xblToken)
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT"
            };

            string response = await _download
                .PostJsonAsync(XstsUrl, body.ToString(), null, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(response);
            string? token = root.Value<string>("Token");

            if (!string.IsNullOrEmpty(token))
            {
                return token!;
            }

            // XSTS 用 XErr 表达具体原因（常见：没有 Xbox 档案、儿童账号需家长同意、地区不支持）
            long? xerr = root.Value<long?>("XErr");
            throw new InvalidOperationException(XerrMessage(xerr));
        }

        /// <summary>把 XSTS 的 XErr 翻成人话——这些错误用户自己无法从错误码看懂。</summary>
        public static string XerrMessage(long? xerr)
        {
            switch (xerr)
            {
                case 2148916233:
                    return "该微软账号还没有 Xbox 档案。请先到 xbox.com 创建档案后重试。";
                case 2148916235:
                    return "该账号所在地区不支持 Xbox Live。";
                case 2148916238:
                    return "该账号是儿童账号，需要家长同意后才能登录。";
                default:
                    return "XSTS 授权失败（XErr=" + (xerr?.ToString(CultureInfo.InvariantCulture) ?? "无") + "）。";
            }
        }

        private async Task<string> LoginWithXboxAsync(
            string userHash,
            string xstsToken,
            CancellationToken cancellationToken)
        {
            JObject body = new JObject
            {
                ["identityToken"] = "XBL3.0 x=" + userHash + ";" + xstsToken
            };

            string response = await _download
                .PostJsonAsync(MinecraftLoginUrl, body.ToString(), null, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(response);
            string? token = root.Value<string>("access_token");

            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("Minecraft 登录响应缺少 access_token：" + Truncate(response));
            }

            return token!;
        }

        private async Task<Account> FetchProfileAsync(
            string minecraftToken,
            CancellationToken cancellationToken)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + minecraftToken
            };

            string response = await _download
                .GetStringAsync(ProfileUrl, headers, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(response);
            string? id = root.Value<string>("id");
            string? name = root.Value<string>("name");

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
            {
                // 这个接口 404 通常意味着账号没有购买 Minecraft
                throw new InvalidOperationException(
                    "该账号没有可用的 Minecraft 档案（可能未购买游戏）。");
            }

            return new Account(name!, FormatUuid(id!), AccountType.Microsoft, minecraftToken);
        }

        /// <summary>把 32 位无连字符的 id 加上连字符。</summary>
        public static string FormatUuid(string raw)
        {
            if (raw == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            string trimmed = raw.Replace("-", string.Empty);
            if (trimmed.Length != 32)
            {
                return raw;
            }

            return trimmed.Substring(0, 8) + "-" +
                   trimmed.Substring(8, 4) + "-" +
                   trimmed.Substring(12, 4) + "-" +
                   trimmed.Substring(16, 4) + "-" +
                   trimmed.Substring(20, 12);
        }

        private static string Truncate(string text)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= 300 ? text : text.Substring(0, 300) + "…";
        }
    }
}

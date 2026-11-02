using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Accounts
{
    /// <summary>
    /// 外置登录（Yggdrasil 协议）。
    ///
    /// MUA「联合认证」、Blessing Skin / LittleSkin 皮肤站都走这套：
    ///   1. POST {apiRoot}/authserver/authenticate
    ///      body: { agent, username, password }
    ///      → accessToken + clientToken + selectedProfile{ id, name }
    ///   2. POST {apiRoot}/authserver/refresh（accessToken + clientToken）
    ///   3. 启动时由 authlib-injector 用同一个 apiRoot 接管客户端认证
    ///
    /// 错误用 4xx + <c>{"error":"ForbiddenOperationException","errorMessage":"..."}</c> 表达，
    /// 因此这里依赖一个"不把 4xx 当异常"的下载实现（见 OAuthDownloadService）。
    /// </summary>
    public sealed class YggdrasilAuthenticator : IExternalAuthenticator
    {
        private readonly IDownloadService _download;

        public YggdrasilAuthenticator(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        public async Task<Account> LoginAsync(
            string apiRoot, string username, string password, CancellationToken cancellationToken)
        {
            string root = NormalizeRoot(apiRoot);

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("请填写外置登录的账号或邮箱。");
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("请填写外置登录的密码。");
            }

            JObject body = new JObject
            {
                ["agent"] = new JObject
                {
                    ["name"] = "Minecraft",
                    ["version"] = 1
                },
                ["username"] = username.Trim(),
                ["password"] = password,
                ["requestUser"] = false
            };

            string response = await _download
                .PostJsonAsync(root + "/authserver/authenticate", body.ToString(), null, cancellationToken)
                .ConfigureAwait(false);

            Account account = ToAccount(response, root);
            return account;
        }

        public async Task<Account> RefreshAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            if (account.Type != AccountType.External || string.IsNullOrEmpty(account.AuthServer))
            {
                throw new InvalidOperationException("不是外置登录账号，无法续期。");
            }

            JObject body = new JObject
            {
                ["accessToken"] = account.EffectiveAccessToken,
                ["clientToken"] = account.RefreshToken ?? string.Empty
            };

            string response = await _download
                .PostJsonAsync(
                    NormalizeRoot(account.AuthServer!) + "/authserver/refresh",
                    body.ToString(),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            Account refreshed = ToAccount(response, NormalizeRoot(account.AuthServer!), requireProfile: false);

            // 续期响应通常不带 selectedProfile：沿用原来的名字/UUID
            return new Account(
                refreshed.Name == "Player" ? account.Name : refreshed.Name,
                string.IsNullOrEmpty(refreshed.Uuid) ? account.Uuid : refreshed.Uuid,
                AccountType.External,
                refreshed.AccessToken,
                refreshed.RefreshToken ?? account.RefreshToken,
                account.AuthServer);
        }

        /// <summary>把响应转成账号；错误响应抛带中文说明的异常。</summary>
        internal static Account ToAccount(string response, string apiRoot, bool requireProfile = true)
        {
            JObject root;
            try
            {
                root = JObject.Parse(response);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("认证服务器返回的不是 JSON：" + Truncate(response));
            }

            string? error = root.Value<string>("error");
            if (!string.IsNullOrEmpty(error))
            {
                string message = root.Value<string>("errorMessage") ?? error!;
                throw new InvalidOperationException(FriendlyError(error!, message));
            }

            string? accessToken = root.Value<string>("accessToken");
            string? clientToken = root.Value<string>("clientToken");

            JToken? profile = root["selectedProfile"];
            string? id = profile?.Value<string>("id");
            string? name = profile?.Value<string>("name");

            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("认证响应缺少 accessToken：" + Truncate(response));
            }

            if (requireProfile && (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)))
            {
                // 账号存在但还没建角色：Blessing Skin 上叫"角色管理"
                throw new InvalidOperationException(
                    "该账号还没有游戏角色。请先在皮肤站（如 MUA 用户中心）创建一个角色再登录。");
            }

            return new Account(
                string.IsNullOrEmpty(name) ? "Player" : name!,
                string.IsNullOrEmpty(id) ? string.Empty : MicrosoftAuthenticator.FormatUuid(id!),
                AccountType.External,
                accessToken,
                clientToken,
                apiRoot);
        }

        /// <summary>把 Yggdrasil 的错误码翻成人话。</summary>
        internal static string FriendlyError(string error, string message)
        {
            switch (error)
            {
                case "ForbiddenOperationException":
                    // 最常见的两种：密码错、被服务器限制
                    if (message.IndexOf("Invalid credentials", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        message.IndexOf("密码", StringComparison.Ordinal) >= 0)
                    {
                        return "账号或密码错误。";
                    }

                    return "登录被拒绝：" + message;

                case "IllegalArgumentException":
                    return "请求无效：" + message;

                default:
                    return message;
            }
        }

        internal static string NormalizeRoot(string apiRoot)
        {
            if (string.IsNullOrWhiteSpace(apiRoot))
            {
                throw new InvalidOperationException("请填写认证服务器地址（Yggdrasil API 根）。");
            }

            string root = apiRoot.Trim().TrimEnd('/');

            if (!root.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !root.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("认证服务器地址必须以 http:// 或 https:// 开头。");
            }

            return root;
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= 200 ? text : text.Substring(0, 200) + "…";
        }
    }
}

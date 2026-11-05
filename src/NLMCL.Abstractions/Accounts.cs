using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>账号类型。</summary>
    public enum AccountType
    {
        /// <summary>离线账号：名字自定，UUID 由名字推导。</summary>
        Offline = 0,

        /// <summary>微软账号（正版）。</summary>
        Microsoft = 1,

        /// <summary>外置登录：Yggdrasil / authlib-injector 生态（如 MUA 联合认证）。</summary>
        External = 2
    }

    /// <summary>一个可用于启动的账号。</summary>
    public sealed class Account
    {
        public Account(string name, string uuid, AccountType type,
            string? accessToken = null, string? refreshToken = null, string? authServer = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Uuid = uuid ?? throw new ArgumentNullException(nameof(uuid));
            Type = type;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            AuthServer = authServer;
        }

        public string Name { get; }

        /// <summary>不带花括号的 UUID 字符串。</summary>
        public string Uuid { get; }

        public AccountType Type { get; }

        /// <summary>正版账号的访问令牌；离线账号为 null。</summary>
        public string? AccessToken { get; }

        /// <summary>
        /// OAuth 刷新令牌。正版账号用它续期，避免访问令牌过期后要求重新登录。
        /// </summary>
        public string? RefreshToken { get; }

        /// <summary>是否可以自动续期。</summary>
        public bool CanRefresh => (Type == AccountType.Microsoft || Type == AccountType.External)
            && !string.IsNullOrEmpty(RefreshToken);

        /// <summary>
        /// 外置登录的认证服务器根地址（如 <c>https://skin.mualliance.ltd/api/yggdrasil</c>）。
        /// 微软/离线账号为 null。
        /// </summary>
        public string? AuthServer { get; }

        /// <summary>上次使用时间（登录历史排序用，可为空）。</summary>
        public DateTimeOffset? LastUsed { get; set; }

        /// <summary>界面上显示的「上次使用 …」。</summary>
        public string LastUsedLabel
        {
            get
            {
                if (LastUsed == null)
                {
                    return string.Empty;
                }

                TimeSpan ago = DateTimeOffset.Now - LastUsed.Value;

                if (ago.TotalMinutes < 1)
                {
                    return "刚刚使用";
                }

                if (ago.TotalHours < 1)
                {
                    return (int)ago.TotalMinutes + " 分钟前使用";
                }

                if (ago.TotalDays < 1)
                {
                    return (int)ago.TotalHours + " 小时前使用";
                }

                return LastUsed.Value.LocalDateTime.ToString("MM-dd HH:mm") + " 使用";
            }
        }

        /// <summary>界面上显示的类型标签。</summary>
        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case AccountType.Microsoft:
                        return "正版";
                    case AccountType.External:
                        return "外置";
                    default:
                        return "离线";
                }
            }
        }

        /// <summary>启动时传给游戏的 accessToken。离线账号用占位值。</summary>
        public string EffectiveAccessToken =>
            string.IsNullOrEmpty(AccessToken) ? "0" : AccessToken!;

        public override string ToString() => Name + " (" + TypeLabel + ")";
    }

    /// <summary>
    /// 外置登录：Yggdrasil 协议（MUA 联合认证、Blessing Skin / LittleSkin 皮肤站）。
    /// 与微软登录并列，互不影响。
    /// </summary>
    public interface IExternalAuthenticator
    {
        /// <summary>用账号密码登录；成功返回带 accessToken 的账号。失败抛带中文说明的异常。</summary>
        Task<Account> LoginAsync(string apiRoot, string username, string password, CancellationToken cancellationToken);

        /// <summary>用 accessToken + clientToken 续期。</summary>
        Task<Account> RefreshAsync(Account account, CancellationToken cancellationToken);
    }
    /// <summary>账号的持久化。实现可以是 JSON 文件。</summary>
    public interface IAccountStore
    {
        /// <summary>当前账号。</summary>
        Task<Account?> LoadAsync(CancellationToken cancellationToken);

        /// <summary>所有可切换的账号（当前账号排第一）。</summary>
        Task<IReadOnlyList<Account>> LoadAllAsync(CancellationToken cancellationToken);

        /// <summary>保存账号并设为当前（登录时调用）。</summary>
        Task SaveAsync(Account account, CancellationToken cancellationToken);

        /// <summary>把已有账号设为当前（不重新登录）。</summary>
        Task SwitchAsync(Account account, CancellationToken cancellationToken);

        /// <summary>删除一个账号；删的是当前账号时，自动切到剩下的第一个。</summary>
        Task RemoveAsync(Account account, CancellationToken cancellationToken);

        /// <summary>
        /// 退出登录：只清掉"当前账号"，**保留**所有已保存账号。
        /// 下次启动不再自动登录，但账号仍在列表里可随时切换。
        /// </summary>
        Task SignOutAsync(CancellationToken cancellationToken);

        Task ClearAsync(CancellationToken cancellationToken);
    }

    /// <summary>微软设备码登录的一次挑战。</summary>
    public sealed class DeviceCodeChallenge
    {
        public DeviceCodeChallenge(
            string deviceCode,
            string userCode,
            string verificationUri,
            int intervalSeconds,
            DateTimeOffset expiresAt)
        {
            DeviceCode = deviceCode ?? throw new ArgumentNullException(nameof(deviceCode));
            UserCode = userCode ?? throw new ArgumentNullException(nameof(userCode));
            VerificationUri = verificationUri ?? string.Empty;
            IntervalSeconds = intervalSeconds <= 0 ? 5 : intervalSeconds;
            ExpiresAt = expiresAt;
        }

        /// <summary>内部设备码（轮询时用，给用户看的是下面那个）。</summary>
        public string DeviceCode { get; }

        /// <summary>给用户输入的短码，例如 ABCD-1234。</summary>
        public string UserCode { get; }

        /// <summary>用户要打开的网址。</summary>
        public string VerificationUri { get; }

        public int IntervalSeconds { get; }

        public DateTimeOffset ExpiresAt;
    }

    /// <summary>
    /// 微软账号登录（OAuth 2.0 设备码 → Xbox Live → XSTS → Minecraft Services）。
    /// </summary>
    public interface IMicrosoftAuthenticator
    {
        /// <summary>
        /// Azure 应用的 client id。界面允许运行期填写（登录弹层），因此必须可写；
        /// 为空时 <c>StartAsync</c> 会抛出带说明的异常，而不是发一个注定失败的请求。
        /// </summary>
        string? ClientId { get; set; }

        /// <summary>取设备码，并给用户一个短码去浏览器输入。</summary>
        Task<DeviceCodeChallenge> StartAsync(CancellationToken cancellationToken);

        /// <summary>轮询直到用户完成授权，返回可用账号；超时或出错则抛异常。</summary>
        Task<Account> WaitForAccountAsync(DeviceCodeChallenge challenge, CancellationToken cancellationToken);

        /// <summary>
        /// 用 refresh_token 续期。失败时抛异常，调用方应保留旧账号并提示重新登录。
        /// </summary>
        Task<Account> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Accounts
{
    /// <summary>
    /// 账号存储：一个文件里存**多个账号** + 当前选中项。
    ///
    /// 文件格式（新）：
    /// <code>
    /// { "current": "&lt;uuid&gt;", "accounts": [ { name, uuid, type, accessToken, refreshToken, authServer } ] }
    /// </code>
    ///
    /// 兼容老格式（单个账号对象，顶层直接是 name/uuid/type），读取时识别并迁移，
    /// 下次保存自动写成新格式。
    ///
    /// 令牌用 DPAPI（当前用户作用域）加密，见 <see cref="EncryptToken"/>。
    /// </summary>
    public sealed class JsonAccountStore : IAccountStore
    {
        private readonly IAppPaths _paths;

        public JsonAccountStore(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            CleanupTemporaryFiles();
        }

        public async Task<Account?> LoadAsync(CancellationToken cancellationToken)
        {
            AccountSet set = await ReadAsync().ConfigureAwait(false);

            if (set.Accounts.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(set.CurrentKey))
            {
                Account? current = set.Accounts.FirstOrDefault(
                    account => string.Equals(account.Uuid, set.CurrentKey, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(account.Name, set.CurrentKey, StringComparison.OrdinalIgnoreCase));

                if (current != null)
                {
                    return current;
                }
            }

            // 没有 current 说明用户点了「退出登录」：不自动登录，但账号仍保留
            return null;
        }

        public async Task<IReadOnlyList<Account>> LoadAllAsync(CancellationToken cancellationToken)
        {
            AccountSet set = await ReadAsync().ConfigureAwait(false);

            // 当前账号排第一，其余按"上次使用"倒序
            if (!string.IsNullOrEmpty(set.CurrentKey))
            {
                Account? current = set.Accounts.FirstOrDefault(
                    account => string.Equals(account.Uuid, set.CurrentKey, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(account.Name, set.CurrentKey, StringComparison.OrdinalIgnoreCase));

                if (current != null)
                {
                    List<Account> ordered = new List<Account> { current };
                    ordered.AddRange(set.Accounts
                        .Where(account => !ReferenceEquals(account, current))
                        .OrderByDescending(account => account.LastUsed ?? DateTimeOffset.MinValue));
                    return ordered;
                }
            }

            return set.Accounts;
        }

        public async Task SaveAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            AccountSet set = await ReadAsync().ConfigureAwait(false);
            JObject entry = ToJson(account);

            // 记录使用时间：登录历史按它排序
            entry["lastUsed"] = DateTimeOffset.Now.ToString("o");
            account.LastUsed = DateTimeOffset.Now;

            int index = set.Entries.FindIndex(item => IsSame(item, account));

            if (index >= 0)
            {
                set.Entries[index] = entry;
            }
            else
            {
                set.Entries.Add(entry);
            }

            set.CurrentKey = account.Uuid;
            Write(set.Entries, account.Uuid);
        }

        public async Task SwitchAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            AccountSet set = await ReadAsync().ConfigureAwait(false);
            Write(set.Entries, account.Uuid);
        }

        public async Task RemoveAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            AccountSet set = await ReadAsync().ConfigureAwait(false);

            set.Entries.RemoveAll(item => IsSame(item, account));

            string current = set.CurrentKey ?? string.Empty;

            if (string.Equals(current, account.Uuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(current, account.Name, StringComparison.OrdinalIgnoreCase))
            {
                // 删的是当前账号：切到剩下的第一个
                JObject? next = set.Entries.FirstOrDefault();

                current = next == null ? string.Empty : next.Value<string>("uuid") ?? string.Empty;
            }

            Write(set.Entries, current);
        }

        public async Task SignOutAsync(CancellationToken cancellationToken)
        {
            AccountSet set = await ReadAsync().ConfigureAwait(false);

            // 只把 current 置空，账号条目全部保留
            Write(set.Entries, string.Empty);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            string file = _paths.AccountsFile;

            if (File.Exists(file))
            {
                File.Delete(file);
            }

            return Task.CompletedTask;
        }

        // ---------- 读写 ----------

        private sealed class AccountSet
        {
            public List<JObject> Entries { get; } = new List<JObject>();

            public string? CurrentKey { get; set; }

            public List<Account> Accounts { get; } = new List<Account>();
        }

        private Task<AccountSet> ReadAsync()
        {
            AccountSet set = new AccountSet();
            string file = _paths.AccountsFile;

            if (!File.Exists(file))
            {
                return Task.FromResult(set);
            }

            JToken root;

            try
            {
                root = JToken.Parse(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (JsonException)
            {
                return Task.FromResult(set);
            }

            if (root is JObject obj && obj["accounts"] is JArray array)
            {
                set.CurrentKey = obj.Value<string>("current");

                foreach (JObject item in array.OfType<JObject>())
                {
                    Account? account = FromJson(item);

                    if (account != null)
                    {
                        account.LastUsed = ParseLastUsed(item);
                        set.Entries.Add(item);
                        set.Accounts.Add(account);
                    }
                }

                return Task.FromResult(set);
            }

            // 老格式：单账号对象
            if (root is JObject legacy)
            {
                Account? account = FromJson(legacy);

                if (account != null)
                {
                    set.Entries.Add(legacy);
                    set.Accounts.Add(account);
                    set.CurrentKey = account.Uuid;
                }
            }

            return Task.FromResult(set);
        }

        private void Write(List<JObject> entries, string? currentKey)
        {
            Directory.CreateDirectory(_paths.DataRoot);

            JObject root = new JObject
            {
                ["current"] = currentKey ?? string.Empty,
                ["accounts"] = new JArray(entries.Select(entry => entry.DeepClone()))
            };

            string temporary = _paths.AccountsFile + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                File.WriteAllText(temporary, root.ToString(Formatting.Indented), Encoding.UTF8);

                // 杀毒软件/另一个实例短暂占用时，直接覆盖可能失败；重试几次再放弃。
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Copy(temporary, _paths.AccountsFile, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < 2)
                    {
                        Thread.Sleep(40 * (attempt + 1));
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>删除历史版本留下的 .tmp 文件；失败不影响账号读取。</summary>
        private void CleanupTemporaryFiles()
        {
            try
            {
                string directory = Path.GetDirectoryName(_paths.AccountsFile) ?? _paths.DataRoot;
                string pattern = Path.GetFileName(_paths.AccountsFile) + ".tmp*";

                foreach (string file in Directory.EnumerateFiles(directory, pattern))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        private static DateTimeOffset? ParseLastUsed(JObject item)
        {
            string? text = item.Value<string>("lastUsed");

            return DateTimeOffset.TryParse(text, out DateTimeOffset value) ? value : null;
        }


        private static JObject ToJson(Account account)
        {
            return new JObject
            {
                ["name"] = account.Name,
                ["uuid"] = account.Uuid,
                ["type"] = account.Type.ToString(),
                ["accessToken"] = EncryptToken(account.AccessToken),
                ["refreshToken"] = EncryptToken(account.RefreshToken),
                ["authServer"] = account.AuthServer
            };
        }

        private static Account? FromJson(JObject root)
        {
            string? name = root.Value<string>("name");
            string? uuid = root.Value<string>("uuid");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uuid))
            {
                return null;
            }

            string typeText = root.Value<string>("type") ?? "Offline";
            AccountType type = string.Equals(typeText, "Microsoft", StringComparison.OrdinalIgnoreCase)
                ? AccountType.Microsoft
                : string.Equals(typeText, "External", StringComparison.OrdinalIgnoreCase)
                    ? AccountType.External
                    : AccountType.Offline;

            Account account = new Account(
                name!, uuid!, type,
                DecryptToken(root.Value<string>("accessToken")),
                DecryptToken(root.Value<string>("refreshToken")),
                root.Value<string>("authServer"));

            // 正版/外置账号必须有可用 accessToken 才算"登录着"：
            // DPAPI 换用户/换机器后解不开，之前会把这种账号当正版载入，进服被踢还没有提示。
            if ((type == AccountType.Microsoft || type == AccountType.External)
                && string.IsNullOrEmpty(account.AccessToken))
            {
                return null;
            }

            return account;
        }

        private static bool IsSame(JObject entry, Account account)
        {
            string? uuid = entry.Value<string>("uuid");

            return string.Equals(uuid, account.Uuid, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 用 DPAPI（当前用户作用域）加密令牌。
        ///
        /// 为什么必须加密：<c>accounts.json</c> 是明文文件，正版 accessToken 等同于账号的临时密码；
        /// 一旦被同机的其他程序读走，就能冒充用户。DPAPI 让密文只在**当前 Windows 用户**下可解。
        ///
        /// 失败时抛异常而不是写明文——宁可这次不存账号。
        /// </summary>
        private static string? EncryptToken(string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            try
            {
                byte[] cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token!),
                    null,
                    DataProtectionScope.CurrentUser);

                return "dpapi:" + Convert.ToBase64String(cipher);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("无法用 DPAPI 加密账号令牌，本次未保存账号。", ex);
            }
            catch (PlatformNotSupportedException ex)
            {
                throw new InvalidOperationException("当前系统不支持 DPAPI，本次未保存账号。", ex);
            }
        }

        /// <summary>解密令牌；老文件里的明文（没有 <c>dpapi:</c> 前缀）按原样返回。</summary>
        private static string? DecryptToken(string? stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            const string prefix = "dpapi:";

            if (!stored!.StartsWith(prefix, StringComparison.Ordinal))
            {
                return stored;
            }

            try
            {
                byte[] cipher = Convert.FromBase64String(stored.Substring(prefix.Length));
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLCL.Abstractions;

namespace NLCL.Core.Accounts
{
    /// <summary>
    /// 离线账号的 UUID 推导。
    ///
    /// 规则与 Minecraft 服务端一致：`md5("OfflinePlayer:" + 名字)`，
    /// 然后按 UUID v3 的格式写上版本位与变体位。
    /// 这样离线账号在正版服之外的世界里能拿到与服务器一致的 UUID。
    /// </summary>
    public static class OfflineAccountFactory
    {
        public static Account Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("用户名不能为空。", nameof(name));
            }

            string trimmed = name.Trim();
            return new Account(trimmed, ComputeUuid(trimmed), AccountType.Offline);
        }

        /// <summary>由用户名推导离线 UUID。</summary>
        public static string ComputeUuid(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            byte[] hash;
            using (MD5 md5 = MD5.Create())
            {
                hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
            }

            // UUID v3：高字节版本号 3，变体位 10xx
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            StringBuilder builder = new StringBuilder(36);
            for (int i = 0; i < 16; i++)
            {
                if (i == 4 || i == 6 || i == 8 || i == 10)
                {
                    builder.Append('-');
                }

                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }

    /// <summary>把账号存成 JSON 文件（<see cref="IAppPaths.AccountsFile"/>）。</summary>
    public sealed class JsonAccountStore : IAccountStore
    {
        private readonly IAppPaths _paths;

        public JsonAccountStore(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public System.Threading.Tasks.Task<Account?> LoadAsync(
            System.Threading.CancellationToken cancellationToken)
        {
            string file = _paths.AccountsFile;

            if (!File.Exists(file))
            {
                return System.Threading.Tasks.Task.FromResult<Account?>(null);
            }

            string json = File.ReadAllText(file, Encoding.UTF8);
            JObject root = JObject.Parse(json);

            string? name = root.Value<string>("name");
            string? uuid = root.Value<string>("uuid");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uuid))
            {
                return System.Threading.Tasks.Task.FromResult<Account?>(null);
            }

            string typeText = root.Value<string>("type") ?? "Offline";
            AccountType type = string.Equals(typeText, "Microsoft", StringComparison.OrdinalIgnoreCase)
                ? AccountType.Microsoft
                : AccountType.Offline;

            Account account = new Account(
                name!, uuid!, type,
                DecryptToken(root.Value<string>("accessToken")),
                DecryptToken(root.Value<string>("refreshToken")));
            return System.Threading.Tasks.Task.FromResult<Account?>(account);
        }

        public System.Threading.Tasks.Task SaveAsync(
            Account account,
            System.Threading.CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            Directory.CreateDirectory(_paths.DataRoot);

            JObject root = new JObject
            {
                ["name"] = account.Name,
                ["uuid"] = account.Uuid,
                ["type"] = account.Type.ToString(),
                ["accessToken"] = EncryptToken(account.AccessToken),
                ["refreshToken"] = EncryptToken(account.RefreshToken)
            };

            File.WriteAllText(
                _paths.AccountsFile,
                root.ToString(Formatting.Indented),
                Encoding.UTF8);

            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 用 DPAPI（当前用户作用域）加密令牌。
        ///
        /// 为什么必须加密：<c>accounts.json</c> 是明文文件，正版 accessToken 等同于账号的临时密码；
        /// 一旦被同机的其他程序读走，就能冒充用户。DPAPI 让密文只在**当前 Windows 用户**下可解，
        /// 拷到别的机器或别的用户下都是废数据。
        ///
        /// 失败时返回 null 而不是抛异常——加密不可用不该让整个登录流程失败，
        /// 但也不会退化成明文写入（宁可这次不存令牌）。
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
            catch (CryptographicException)
            {
                return null;
            }
            catch (PlatformNotSupportedException)
            {
                return null;
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
                // 换了 Windows 用户或换机器后解不开——当作未登录，而不是崩掉
                return null;
            }
        }

        public System.Threading.Tasks.Task ClearAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (File.Exists(_paths.AccountsFile))
            {
                File.Delete(_paths.AccountsFile);
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}

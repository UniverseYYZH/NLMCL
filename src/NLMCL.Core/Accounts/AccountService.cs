using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Accounts
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

}

using System;
using System.Collections.Generic;
using System.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Servers
{
    /// <summary>
    /// 从 Modrinth 的版本列表里挑出**和服务器一致**的 AutoModpack。
    ///
    /// 为什么不能直接取最新：AutoModpack v4 与 v5 是两套协议
    /// （v4 用 <c>modpack.mods.toml</c> + HTTP，v5 换成 <c>/head</c> + <c>/journal</c>）。
    /// 服务端跑 v4.0.6 时给客户端装 v5.0.0-rc，握手直接对不上，
    /// 表现就是"进服了但没同步 mod"。
    ///
    /// 选择顺序：
    ///   1. 档案里钉的版本（如 4.0.6）——精确匹配 version_number 前缀或文件名；
    ///   2. 同主版本里最新的（4.x）——小版本升级通常兼容；
    ///   3. 实在没有就取列表第一个（并让调用方提示"版本可能不匹配"）。
    /// </summary>
    public static class AutoModpackSelector
    {
        public static ResourceFile? Pick(IReadOnlyList<ResourceFile> files, string? wantedVersion)
        {
            if (files == null || files.Count == 0)
            {
                return null;
            }

            string wanted = (wantedVersion ?? string.Empty).Trim();

            if (wanted.Length > 0)
            {
                ResourceFile? exact = files.FirstOrDefault(file => Matches(file, wanted));

                if (exact != null)
                {
                    return exact;
                }

                int dot = wanted.IndexOf('.');

                if (dot > 0)
                {
                    string majorPrefix = wanted.Substring(0, dot + 1); // "4."

                    ResourceFile? sameMajor = files.FirstOrDefault(file =>
                        (file.VersionName ?? string.Empty).StartsWith(majorPrefix, StringComparison.OrdinalIgnoreCase)
                        || file.Name.StartsWith("automodpack-" + majorPrefix, StringComparison.OrdinalIgnoreCase));

                    if (sameMajor != null)
                    {
                        return sameMajor;
                    }
                }
            }

            return files[0];
        }

        private static bool Matches(ResourceFile file, string wanted)
        {
            string version = file.VersionName ?? string.Empty;

            return version.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || version.StartsWith(wanted + "-", StringComparison.OrdinalIgnoreCase)
                || version.StartsWith(wanted + ".", StringComparison.OrdinalIgnoreCase)
                || file.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

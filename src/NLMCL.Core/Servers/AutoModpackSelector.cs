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
    ///   3. 档案钉了版本但同主版本也没有——返回 null，由调用方中止启动；
    ///      未钉版本才取列表第一个。
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
                string majorPrefix = dot > 0
                    ? wanted.Substring(0, dot + 1) // "4."
                    : wanted + "."; // 只写了主版本 "4"

                ResourceFile? sameMajor = files.FirstOrDefault(file =>
                    (file.VersionName ?? string.Empty).StartsWith(majorPrefix, StringComparison.OrdinalIgnoreCase)
                    || file.Name.StartsWith("automodpack-" + majorPrefix, StringComparison.OrdinalIgnoreCase));

                if (sameMajor != null)
                {
                    return sameMajor;
                }

                // 档案钉了版本却找不到同主版本：不能退回最新版。
                // v4 服务端配 v5 客户端会握手失败，表现为“进服了但没同步 mod”，
                // 比直接报错更难排查，所以交给调用方中止启动。
                return null;
            }

            return files[0];
        }

        private static bool Matches(ResourceFile file, string wanted)
        {
            string version = file.VersionName ?? string.Empty;

            return version.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || version.StartsWith(wanted + "-", StringComparison.OrdinalIgnoreCase)
                || version.StartsWith(wanted + ".", StringComparison.OrdinalIgnoreCase)
                || MatchesVersion(file.Name, wanted);
        }

        /// <summary>
        /// 已落盘文件名是否满足档案钉的版本（用于“已就位”检查）。
        /// 只按文件名判断；版本号缺失时无法确认，返回 false 让调用方提示重装。
        /// </summary>
        public static bool MatchesVersion(string? fileName, string? wantedVersion)
        {
            string file = (fileName ?? string.Empty).Trim();
            string wanted = (wantedVersion ?? string.Empty).Trim();

            if (file.Length == 0 || wanted.Length == 0)
            {
                return false;
            }

            int index = file.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                int end = index + wanted.Length;
                if (IsVersionBoundaryBefore(file, index) && IsVersionBoundaryAfter(file, end))
                {
                    return true;
                }

                index = file.IndexOf(wanted, index + 1, StringComparison.OrdinalIgnoreCase);
            }

            int dot = wanted.IndexOf('.');
            if (dot > 0)
            {
                // 精确版本（4.0.6）不能靠主版本前缀命中 4.0.7。
                return false;
            }

            string majorPrefix = wanted + ".";

            return file.StartsWith("automodpack-" + majorPrefix, StringComparison.OrdinalIgnoreCase)
                || file.IndexOf("-" + majorPrefix, StringComparison.OrdinalIgnoreCase) >= 0
                || file.IndexOf("_" + majorPrefix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>版本号前一位不能紧贴数字或另一个版本段。</summary>
        private static bool IsVersionBoundaryBefore(string file, int index)
        {
            if (index == 0)
            {
                return true;
            }

            char previous = file[index - 1];
            if (char.IsDigit(previous))
            {
                return false;
            }

            return previous != '.' || index < 2 || !char.IsDigit(file[index - 2]);
        }

        /// <summary>
        /// 版本号后一位不能紧贴数字（4.0.60 不是 4.0.6），
        /// 也不能紧贴下一段版本（4.0.6.1 不是 4.0.6）；`.jar`、`-beta` 仍可匹配。
        /// </summary>
        private static bool IsVersionBoundaryAfter(string file, int end)
        {
            if (end >= file.Length)
            {
                return true;
            }

            char next = file[end];
            if (char.IsDigit(next))
            {
                return false;
            }

            return next != '.' || end + 1 >= file.Length || !char.IsDigit(file[end + 1]);
        }
    }
}

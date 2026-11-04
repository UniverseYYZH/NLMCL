using System;

namespace NLMCL.Core.Servers
{
    /// <summary>
    /// 判断某个版本目录是否对应服务器档案要求的实例。
    ///
    /// 为什么不能用 <c>Contains</c>：版本目录形如
    /// <c>fabric-loader-0.20.1-1.21.1</c> 与 <c>fabric-loader-0.16.9-1.21.10</c>，
    /// 用 <c>Contains("1.21.1")</c> 会把 1.21.10 也命中，导致进错实例。
    /// 版本号永远在目录名末尾，因此用"结尾精确匹配"。
    /// </summary>
    public static class ProfileInstanceMatcher
    {
        public static bool Matches(string? folderName, string? minecraft, string? loader)
        {
            string folder = (folderName ?? string.Empty).ToLowerInvariant();
            string version = (minecraft ?? string.Empty).ToLowerInvariant();
            string kind = (loader ?? string.Empty).ToLowerInvariant();

            if (folder.Length == 0 || version.Length == 0)
            {
                return false;
            }

            bool versionMatches = folder.Equals(version, StringComparison.Ordinal)
                || folder.EndsWith("-" + version, StringComparison.Ordinal);

            if (!versionMatches)
            {
                return false;
            }

            return kind.Length == 0 || folder.Contains(kind);
        }
    }
}

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

            if (kind.Length > 0)
            {
                return folder.Contains(kind);
            }

            // 纯净档案（loader 为空）：必须排除"带加载器"的目录。
            // 否则 fabric-loader-0.20.1-1.21.1 也会以 "-1.21.1" 结尾而被命中，
            // 结果"进还原服"启动的是模组客户端 —— 服务器必然拒绝。
            return !HasLoaderToken(folder);
        }

        private static readonly string[] LoaderTokens =
        {
            "fabric", "forge", "neoforge", "quilt", "optifine", "liteloader"
        };

        /// <summary>目录名里是否带加载器标记。</summary>
        private static bool HasLoaderToken(string folder)
        {
            foreach (string token in LoaderTokens)
            {
                if (folder.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

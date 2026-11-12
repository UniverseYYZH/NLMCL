using System;

namespace NLMCL.Core.Servers
{
    /// <summary>
    /// 判断某个版本目录是否对应服务器档案要求的实例。
    ///
    /// 各加载器的实际目录约定并不相同：
    /// <list type="bullet">
    /// <item>Fabric / Quilt：<c>fabric-loader-0.20.1-1.21.1</c>，版本在末尾。</item>
    /// <item>Forge：<c>1.21.1-forge-47.4.28</c>，MC 在开头。</item>
    /// <item>NeoForge：<c>neoforge-20.4.1</c>，目录名里没有 MC 版本。</item>
    /// </list>
    /// 因此不能用 <c>Contains</c> 或单一的“版本在末尾”规则。
    /// </summary>
    public static class ProfileInstanceMatcher
    {
        public static bool Matches(
            string? folderName,
            string? minecraft,
            string? loader,
            string? loaderVersion = null)
        {
            string folder = (folderName ?? string.Empty).ToLowerInvariant();
            string version = (minecraft ?? string.Empty).ToLowerInvariant();
            string kind = (loader ?? string.Empty).ToLowerInvariant();
            string wantedLoader = (loaderVersion ?? string.Empty).Trim().ToLowerInvariant();

            if (folder.Length == 0 || version.Length == 0)
            {
                return false;
            }

            if (kind.Length == 0)
            {
                // 纯净档案：<MC>，不能命中任何一个加载器实例。
                return folder.Equals(version, StringComparison.Ordinal)
                    || (folder.EndsWith("-" + version, StringComparison.Ordinal)
                        && !HasLoaderToken(folder));
            }

            if (kind == "neoforge")
            {
                // NeoForge 安装器生成的 id 是 neoforge-<版本>，不含 MC；
                // MC 归属由 JSON 的 inheritsFrom 另行校验。
                string prefix = "neoforge-";
                if (!folder.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return false;
                }

                string installed = folder.Substring(prefix.Length);
                return wantedLoader.Length == 0
                    || installed.Equals(wantedLoader, StringComparison.Ordinal);
            }

            if (kind == "forge")
            {
                // Forge 安装器的目录是 <MC>-forge-<加载器版本>。
                string mcFirst = version + "-forge-";
                if (folder.StartsWith(mcFirst, StringComparison.Ordinal))
                {
                    string installed = folder.Substring(mcFirst.Length);
                    return wantedLoader.Length == 0
                        || installed.Equals(wantedLoader, StringComparison.Ordinal);
                }

                // 兼容 <forge>-<加载器版本>-<MC> 的旧目录。
                string loaderFirst = "forge-";
                string suffix = "-" + version;
                if (!folder.StartsWith(loaderFirst, StringComparison.Ordinal)
                    || !folder.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return false;
                }

                string middle = folder.Substring(
                    loaderFirst.Length, folder.Length - loaderFirst.Length - suffix.Length);
                return wantedLoader.Length == 0
                    || middle.Equals(wantedLoader, StringComparison.Ordinal);
            }

            // Fabric / Quilt / 其他“加载器在前、MC 在末尾”的目录。
            bool loaderMatches = folder.StartsWith(kind + "-", StringComparison.Ordinal)
                || folder.IndexOf("-" + kind + "-", StringComparison.Ordinal) >= 0;
            if (!loaderMatches
                || !folder.EndsWith("-" + version, StringComparison.Ordinal))
            {
                return false;
            }

            if (wantedLoader.Length == 0)
            {
                return true;
            }

            string versionSuffix = "-" + version;
            string beforeVersion = folder.Substring(0, folder.Length - versionSuffix.Length);
            return beforeVersion.EndsWith("-" + wantedLoader, StringComparison.Ordinal);
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

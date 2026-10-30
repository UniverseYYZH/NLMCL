using System;
using NLCL.Abstractions;

namespace NLCL.Core.Installing
{
    /// <summary>
    /// Mojang 下载地址 → BMCLAPI 镜像的改写规则。
    ///
    /// 只改写**已知可镜像**的域名，其余（Fabric meta、第三方仓库）原样保留：
    /// 镜像站点对未知路径只会返回 404，硬改反而变慢。
    /// 规则与 PCL 的镜像用法一致（BMCLAPI 按"同路径反向代理 + 少数前缀不同"工作）。
    /// </summary>
    internal static class MojangMirror
    {
        internal const string MirrorBase = "https://bmclapi2.bangbang93.com";

        /// <summary>返回镜像地址；无法确定时返回 null（调用方走官方）。</summary>
        internal static string? TryRewrite(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return null;
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.PathAndQuery;

            // 这几家：BMCLAPI 直接按相同路径代理
            if (host == "launchermeta.mojang.com"
                || host == "piston-meta.mojang.com"
                || host == "piston-data.mojang.com"
                || host == "launcher.mojang.com")
            {
                return MirrorBase + path;
            }

            // 资源对象：resources.download.minecraft.net/<前两位>/<hash> → /assets/<前两位>/<hash>
            if (host == "resources.download.minecraft.net")
            {
                return MirrorBase + "/assets" + path;
            }

            // Maven 库：libraries.minecraft.net/<路径> → /maven/<路径>
            if (host == "libraries.minecraft.net")
            {
                return MirrorBase + "/maven" + path;
            }

            return null;
        }
    }
}

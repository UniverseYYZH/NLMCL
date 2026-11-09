using System;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
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
            // 只镜像小体积元数据；二进制大文件（client.jar / 资源对象）走官方 CDN。
            // 实测 BMCLAPI 的二进制链路在当前网络下 20–30 秒/文件，反而更慢。
            if (host == "launchermeta.mojang.com"
                || host == "piston-meta.mojang.com")
            {
                return MirrorBase + path;
            }

            // 资源对象（resources.download.minecraft.net）不走镜像：
            // 实测 BMCLAPI 对大量几 KB 资源对象每个要 20–30 秒，官方 CDN 反而毫秒级。
            // 资源包/光影/声音文件数量多，继续镜像会把整个安装拖到不可用。
            if (host == "resources.download.minecraft.net")
            {
                return null;
            }

            // Maven 库：libraries.minecraft.net/<路径> → /maven/<路径>
            if (host == "libraries.minecraft.net")
            {
                return MirrorBase + "/maven" + path;
            }

            // Fabric 的 meta（加载器列表、profile JSON）：镜像按 /fabric-meta/<路径> 代理
            if (host == "meta.fabricmc.net")
            {
                return MirrorBase + "/fabric-meta" + path;
            }

            // Fabric 的 maven：镜像按 /maven/<路径> 代理。
            // 坑：它只认 PCL2 的 User-Agent（默认 UA 会 403）——因为底下的 ModNet 发的
            // 就是 PCL2/...，所以这条改写实际是通的（实测 200 / 643ms）。
            // 直连 maven.fabricmc.net 在国内经常超时，才是之前安装失败的根因。
            if (host == "maven.fabricmc.net")
            {
                return MirrorBase + "/maven" + path;
            }

            return null;
        }

        /// <summary>二进制文件：官方 CDN 失败时的后备镜像地址。</summary>
        internal static string? TryRewriteForFallback(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return null;
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.PathAndQuery;

            if (host == "launcher.mojang.com" || host == "piston-data.mojang.com")
            {
                return MirrorBase + path;
            }

            if (host == "resources.download.minecraft.net")
            {
                return MirrorBase + "/assets" + path;
            }

            return null;
        }

        internal static bool IsBinaryHost(string url)
            => TryRewriteForFallback(url) != null;
    }
}

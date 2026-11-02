using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// authlib-injector 的下载与定位。
    ///
    /// 外置登录（Yggdrasil / MUA 联合认证）启动时必须带上
    /// <c>-javaagent:authlib-injector.jar=&lt;apiRoot&gt;</c>，
    /// 让游戏客户端按该认证服务器校验会话、拉皮肤。
    ///
    /// 上游清单：https://authlib-injector.yushi.moe/artifact/latest.json
    /// （含 version / download_url / checksums.sha256）
    /// </summary>
    public sealed class AuthlibInjectorProvider : IAuthlibInjector
    {
        public const string LatestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";

        private readonly IDownloadService _download;
        private readonly IAppPaths _paths;

        public AuthlibInjectorProvider(IDownloadService download, IAppPaths paths)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        /// <summary>返回本地 jar 的完整路径；必要时先下载并校验 sha256。</summary>
        public async Task<string> EnsureAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(InjectorDirectory);

            string metaJson = await _download
                .GetStringAsync(LatestUrl, cancellationToken)
                .ConfigureAwait(false);

            JObject root = JObject.Parse(metaJson);

            string? version = root.Value<string>("version");
            string? url = root.Value<string>("download_url");
            string? sha256 = root.SelectToken("checksums.sha256")?.Value<string>();

            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(url))
            {
                throw new InvalidOperationException("authlib-injector 清单格式异常（缺少 version/download_url）。");
            }

            string target = Path.Combine(InjectorDirectory, "authlib-injector-" + version + ".jar");

            if (File.Exists(target) && (string.IsNullOrEmpty(sha256) || MatchesSha256(target, sha256!)))
            {
                return target;
            }

            string temp = target + ".part";
            await _download.DownloadFileAsync(url!, temp, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(sha256) && !MatchesSha256(temp, sha256!))
            {
                TryDelete(temp);
                throw new InvalidOperationException("authlib-injector 校验失败（sha256 不匹配），已丢弃。");
            }

            TryDelete(target);
            File.Move(temp, target);

            return target;
        }

        private string InjectorDirectory => Path.Combine(_paths.DataRoot, "authlib-injector");

        /// <summary>
        /// 生成 <c>-javaagent:&lt;jar&gt;=&lt;apiRoot&gt;</c> 参数。
        /// 路径可能含空格，必须加引号，否则 JVM 会把参数切断。
        /// </summary>
        public static string BuildJavaAgentArgument(string jarPath, string apiRoot)
        {
            if (string.IsNullOrWhiteSpace(jarPath))
            {
                throw new ArgumentException("authlib-injector 路径为空。", nameof(jarPath));
            }

            string root = apiRoot.TrimEnd('/');

            return "-javaagent:\"" + jarPath + "\"=" + root;
        }

        private static bool MatchesSha256(string file, string expected)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(file))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    string actual = BitConverter.ToString(hash).Replace("-", string.Empty);

                    return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
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
}

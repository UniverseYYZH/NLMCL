using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Accounts
{
    /// <summary>
    /// 外置登录的启动配套：下载（或命中缓存）authlib-injector，
    /// 生成 <c>-javaagent:jar=apiRoot</c> 参数。
    ///
    /// 单独抽出来是因为「主界面启动」和「控制台页启动」两条路径都要用，
    /// 逻辑必须完全一致，否则同账号两边行为不同。
    /// </summary>
    public static class ExternalLaunch
    {
        /// <summary>非外置账号或没有注入器时返回 null（调用方不加参数）。</summary>
        public static async Task<string?> BuildJvmArgumentsAsync(
            Account? account, IAuthlibInjector? injector, CancellationToken cancellationToken)
        {
            if (account == null
                || account.Type != AccountType.External
                || string.IsNullOrWhiteSpace(account.AuthServer)
                || injector == null)
            {
                return null;
            }

            string jarPath = await injector.EnsureAsync(cancellationToken).ConfigureAwait(false);

            return BuildJavaAgentArgument(jarPath, account.AuthServer!);
        }

        /// <summary>
        /// <c>-javaagent:&lt;jar&gt;=&lt;apiRoot&gt;</c>。路径可能含空格，必须带引号。
        /// </summary>
        public static string BuildJavaAgentArgument(string jarPath, string apiRoot)
        {
            if (string.IsNullOrWhiteSpace(jarPath))
            {
                throw new ArgumentException("authlib-injector 路径为空。", nameof(jarPath));
            }

            return "-javaagent:\"" + jarPath + "\"=" + apiRoot.TrimEnd('/');
        }
    }
}

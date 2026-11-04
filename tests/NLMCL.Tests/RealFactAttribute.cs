using System;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 需要"真机环境"的用例：没配环境变量时在**发现阶段**就标记为跳过。
    ///
    /// 为什么不用 <c>if (...) return;</c>：那样 xUnit 会把它算成**通过**，
    /// 汇总数字虚高（"384 全绿"里混着十来个根本没跑）。这里改成显式 Skip，
    /// 报告里会出现在 Skipped 一栏，任何人都能看出"哪些没真跑"。
    ///
    /// 需要环境变量：
    ///   NLMCL_TEST_MC_DIR  真实的 .minecraft 目录
    ///   NLMCL_TEST_VERSION 要测的版本目录名
    /// </summary>
    public sealed class RealFactAttribute : FactAttribute
    {
        public RealFactAttribute()
        {
            string? minecraft = Environment.GetEnvironmentVariable("NLMCL_TEST_MC_DIR");

            if (string.IsNullOrWhiteSpace(minecraft))
            {
                Skip = "需要真机环境：设置 NLMCL_TEST_MC_DIR（见 docs/ACCEPTANCE.md）";
            }
        }
    }

    /// <summary>只依赖"本机装了 Java"的用例。</summary>
    public sealed class LocalJavaFactAttribute : FactAttribute
    {
        public LocalJavaFactAttribute()
        {
            // 由用例自身判断；这里只做占位说明，避免误用 RealFact
        }
    }
}

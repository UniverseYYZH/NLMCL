using System;
using System.Collections.Generic;
using System.Linq;

namespace NLMCL.Core.Logging
{
    /// <summary>一次崩溃判定的结果。</summary>
    public sealed class CrashReport
    {
        public CrashReport(string reason, string? hint, int exitCode, string? evidenceLine)
        {
            Reason = reason;
            Hint = hint;
            ExitCode = exitCode;
            EvidenceLine = evidenceLine;
        }

        /// <summary>崩溃原因（给用户看的一句话）。</summary>
        public string Reason { get; }

        /// <summary>可操作的建议；没有就给 null。</summary>
        public string? Hint { get; }

        public int ExitCode { get; }

        /// <summary>触发判定的那一行日志，便于定位。</summary>
        public string? EvidenceLine { get; }
    }

    /// <summary>
    /// 崩溃判定：看退出码 + 最近日志。
    ///
    /// **不做完整崩溃分析**——PCL 的 `ModCrash.vb` 会解析崩溃报告、
    /// 定位到具体模组与堆栈，那是另一个量级的工作。这里只回答一个问题：
    /// "刚才那次退出，是正常关闭还是崩了？"
    /// </summary>
    public static class CrashDetector
    {
        private const string CrashReportMarker = "---- Minecraft Crash Report ----";

        private static readonly string[] FatalPatterns =
        {
            "java.lang.UnsupportedClassVersionError",
            "java.lang.OutOfMemoryError",
            "A fatal error has been detected by the Java Runtime Environment",
            "Exception in thread \"main\"",
            "Failed to start the game",
            "Could not find or load main class",
            "java.lang.VerifyError",
            "java.lang.NoSuchMethodError",
            "java.lang.IncompatibleClassChangeError",
        };

        /// <summary>
        /// 判定是否崩溃。返回 null 表示正常退出。
        /// </summary>
        /// <param name="exitCode">进程退出码。</param>
        /// <param name="recentLines">最近的日志行（建议传最后 200 行以内）。</param>
        public static CrashReport? Detect(int exitCode, IEnumerable<string>? recentLines)
        {
            List<string> lines = recentLines?.ToList() ?? new List<string>();

            // 1) 有崩溃报告标记 —— 最确定的信号
            string? crashLine = lines.FirstOrDefault(
                line => line != null && line.IndexOf(CrashReportMarker, StringComparison.Ordinal) >= 0);

            if (crashLine != null)
            {
                string? cause = FindJavaCause(lines);
                return new CrashReport(
                    "游戏崩溃（生成了崩溃报告）",
                    cause,
                    exitCode,
                    crashLine);
            }

            // 2) 致命异常特征
            foreach (string pattern in FatalPatterns)
            {
                string? hit = lines.FirstOrDefault(
                    line => line != null && line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);

                if (hit != null)
                {
                    return new CrashReport(
                        "游戏崩溃",
                        HintFor(pattern),
                        exitCode,
                        hit);
                }
            }

            // 3) 非零退出码但没有明显日志 —— 保守判为异常退出
            if (exitCode != 0)
            {
                return new CrashReport(
                    "游戏异常退出（退出码 " + exitCode + "）",
                    "没有找到明确的错误日志，建议查看 logs/latest.log",
                    exitCode,
                    null);
            }

            return null;
        }

        private static string? FindJavaCause(List<string> lines)
        {
            string? oom = lines.FirstOrDefault(
                line => line != null && line.IndexOf("OutOfMemoryError", StringComparison.OrdinalIgnoreCase) >= 0);

            if (oom != null)
            {
                return "内存不足：在实例设置里调大分配内存，或减少模组数量";
            }

            string? mod = lines.FirstOrDefault(
                line => line != null && line.IndexOf("Missing or unsupported mandatory dependencies",
                    StringComparison.OrdinalIgnoreCase) >= 0);

            if (mod != null)
            {
                return "模组依赖缺失：检查模组列表是否少装了前置";
            }

            return null;
        }

        private static string? HintFor(string pattern)
        {
            switch (pattern)
            {
                case "java.lang.UnsupportedClassVersionError":
                    return "Java 版本过低：请换用更高版本的 Java";

                case "java.lang.VerifyError":
                case "java.lang.NoSuchMethodError":
                case "java.lang.IncompatibleClassChangeError":
                    return "模组与当前游戏版本/加载器不匹配：这个 jar 不是给这个版本编译的（多版本模组常见选错变体），换对应版本或联系作者";

                case "java.lang.OutOfMemoryError":
                    return "内存不足：在实例设置里调大分配内存";

                case "Could not find or load main class":
                    return "版本文件损坏：尝试重新安装该版本";

                default:
                    return null;
            }
        }
    }
}

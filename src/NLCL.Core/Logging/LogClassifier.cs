using System;
using System.Text.RegularExpressions;

namespace NLCL.Core.Logging
{
    /// <summary>一行游戏日志的语义标签。</summary>
    public enum LogTag
    {
        Normal = 0,
        ModConflict = 1,
        JavaIncompatible = 2,
        ResourceFailure = 3
    }

    /// <summary>
    /// 按关键词给日志行打标签，供启动控制台上色与筛选。
    ///
    /// 移植自 legacy 的 <c>log_tags.py</c>（自有业务，非 PCL 能力）。
    /// **判定顺序有意义**：Java 不兼容优先于模组冲突，模组冲突优先于资源失败——
    /// 因为一个 Java 版本错误往往会在后续刷出一堆模组加载失败，先标 Java 才不会误导用户。
    /// </summary>
    public static class LogClassifier
    {
        private static readonly Regex JavaPattern = BuildRegex(
            @"unsupportedclassversionerror",
            @"unsupported class version",
            @"requires java\s*\d+",
            @"需要\s*java\s*\d+",
            @"java version.*(?:incompatible|not supported)",
            @"java 版本.*(?:不兼容|不支持)",
            @"java\.lang\.unsupportedclassversion",
            @"could not find java",
            @"java executable.*not found",
            @"class file version \d+");

        private static readonly Regex ModConflictPattern = BuildRegex(
            @"mod resolution",
            @"incompatible mod",
            @"mod conflict",
            @"duplicate mod",
            @"模组冲突",
            @"缺少.*依赖",
            @"mixin.*(?:error|fail|exception)",
            @"missing or unsupported mandatory dependencies",
            @"loaderexception",
            @"modlauncher",
            @"forge.*(?:conflict|incompatible)",
            @"fabric.*(?:conflict|incompatible)",
            @"requires .* version .* of .*");

        private static readonly Regex ResourcePattern = BuildRegex(
            @"failed to load (?:texture|model|resource|sound)",
            @"unable to load (?:texture|model|resource|sound)",
            @"无法加载(?:资源|纹理|模型|声音)",
            @"missing texture",
            @"resource(?:pack)? not found",
            @"filenotfoundexception",
            @"couldn't load .*\.(?:png|json|ogg)",
            @"assets/.*\.(?:png|json|ogg).*not found",
            @"model .* not found");

        /// <summary>给一行日志打标签。空行视为正常信息。</summary>
        public static LogTag Classify(string? line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return LogTag.Normal;
            }

            if (JavaPattern.IsMatch(line))
            {
                return LogTag.JavaIncompatible;
            }

            if (ModConflictPattern.IsMatch(line))
            {
                return LogTag.ModConflict;
            }

            if (ResourcePattern.IsMatch(line))
            {
                return LogTag.ResourceFailure;
            }

            return LogTag.Normal;
        }

        /// <summary>中文标签（界面上显示）。</summary>
        public static string LabelOf(LogTag tag)
        {
            switch (tag)
            {
                case LogTag.JavaIncompatible:
                    return "Java 不兼容";
                case LogTag.ModConflict:
                    return "模组冲突";
                case LogTag.ResourceFailure:
                    return "资源加载失败";
                default:
                    return "正常信息";
            }
        }

        /// <summary>界面配色（与 legacy 一致）。</summary>
        public static string ColorOf(LogTag tag)
        {
            switch (tag)
            {
                case LogTag.JavaIncompatible:
                    return "#C79BFF";
                case LogTag.ModConflict:
                    return "#FF8A7A";
                case LogTag.ResourceFailure:
                    return "#F2C96D";
                default:
                    return "#C9D8CF";
            }
        }

        private static Regex BuildRegex(params string[] patterns)
        {
            return new Regex(
                string.Join("|", patterns),
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
    }
}

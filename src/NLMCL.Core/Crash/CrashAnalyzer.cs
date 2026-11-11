using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>一条崩溃特征匹配。</summary>
    public sealed class CrashMatch
    {
        public CrashMatch(string title, string detail, string advice, bool fatal)
        {
            Title = title;
            Detail = detail;
            Advice = advice;
            Fatal = fatal;
        }

        public string Title { get; }

        public string Detail { get; }

        public string Advice { get; }

        /// <summary>是否是导致无法启动的错误（否则只是警告）。</summary>
        public bool Fatal { get; }
    }

    /// <summary>崩溃分析结果。</summary>
    public sealed class CrashAnalysis
    {
        public CrashAnalysis(
            string source, IReadOnlyList<CrashMatch> matches, IReadOnlyList<string> suspects, string tail)
        {
            Source = source ?? string.Empty;
            Matches = matches ?? Array.Empty<CrashMatch>();
            Suspects = suspects ?? Array.Empty<string>();
            Tail = tail ?? string.Empty;
        }

        /// <summary>分析的文件（崩溃报告或 latest.log）。</summary>
        public string Source { get; }

        public IReadOnlyList<CrashMatch> Matches { get; }

        /// <summary>可疑模组（从日志里提到的 mod id / 文件名匹配出来）。</summary>
        public IReadOnlyList<string> Suspects { get; }

        /// <summary>日志尾部若干行（给用户看原文）。</summary>
        public string Tail { get; }

        public string Summary
        {
            get
            {
                if (Matches.Count == 0)
                {
                    return Source.Length == 0
                        ? "没找到崩溃报告或 latest.log。先启动一次游戏产生日志。"
                        : "没有识别出已知崩溃特征，把日志尾部发我看看。";
                }

                string first = Matches[0].Title;

                return Matches.Count == 1
                    ? "疑似：" + first
                    : "疑似：" + first + " 等 " + Matches.Count + " 个问题";
            }
        }
    }

    /// <summary>崩溃分析：读崩溃报告 / latest.log，匹配常见错误并给出建议。</summary>
    public interface ICrashAnalyzer
    {
        Task<CrashAnalysis> AnalyzeAsync(
            string minecraftDirectory, string versionFolder, CancellationToken cancellationToken);
    }
}

namespace NLMCL.Core.Crash
{
    using NLMCL.Abstractions;

    /// <summary>
    /// 崩溃分析。
    ///
    /// 输入优先级：实例的 <c>crash-reports/crash-*.txt</c>（最新一份）→ <c>logs/latest.log</c>
    /// → 全局 <c>crash-reports</c>。
    ///
    /// 只做**特征匹配 + 可操作建议**，不猜具体模组版本冲突（那需要完整依赖图）。
    /// </summary>
    public sealed class CrashAnalyzer : ICrashAnalyzer
    {
        private const int MaxBytes = 512 * 1024;

        private static readonly (string Pattern, string Title, string Advice, bool Fatal)[] Rules =
        {
            (@"Missing or unsupported mandatory dependencies",
                "缺少前置模组", "按日志里列出的 Mod ID 去安装对应前置（Fabric API / Architectury 等）。", true),

            (@"requires\s+.*?which is missing",
                "缺少前置模组", "日志里写了谁 requires 谁，补上前置再启动。", true),

            (@"Incompatible mod set",
                "模组不兼容", "同一功能装了两套（如两个优化模组），保留一个。", true),

            (@"Duplicate mods found",
                "模组重复", "mods 目录里有同一模组的多个版本，删掉旧的。", true),

            (@"UnsupportedClassVersionError",
                "Java 版本过低", "这个版本需要更高的 Java（1.20.5+ 用 21，1.17+ 用 17）。到设置里换 Java。", true),

            (@"OutOfMemoryError",
                "内存不足", "到「本地版本 → 内存」调大分配（4–8 GB），或减少模组数量。", true),

            (@"Pixel format not accelerated|Failed to create window|GLFW error|WGL.*failed|OpenGL.*(error|not supported)",
                "显卡/驱动问题", "更新显卡驱动；笔记本确认游戏跑在独显上；远程桌面/虚拟机常见此错。", true),

            (@"Mixin apply failed|Mixin.*failed|mixin.*injection.*failed",
                "Mixin 注入失败", "通常是模组与当前加载器/游戏版本不匹配，先禁用最近装的模组。", true),

            (@"zip END header not found|Invalid or corrupt jarfile|error in opening zip file",
                "jar 文件损坏", "某个模组或客户端 jar 下载不完整。用「版本修复」补全，或重下该模组。", true),

            (@"NoSuchMethodError|NoClassDefFoundError|AbstractMethodError",
                "模组版本不匹配", "某个模组是给别的游戏/加载器版本编译的；按日志里的类名找出对应模组并换版本。", true),

            (@"Could not find or load main class",
                "主类缺失", "客户端 jar 或加载器库缺失，用「版本修复」补全。", true),

            (@"java\.lang\.ExceptionInInitializerError",
                "模组初始化失败", "看 Caused by 里的第一个模组包名，禁用对应模组。", true),

            (@"Unsupported major\.minor version",
                "Java 版本不匹配", "换用与游戏版本匹配的 Java。", true),

            (@"AccessDeniedException|Permission denied",
                "文件权限问题", "游戏目录在受保护位置（Program Files / 受控沙箱）时会出现；换到普通目录。", true),
        };

        public async Task<CrashAnalysis> AnalyzeAsync(
            string minecraftDirectory, string versionFolder, CancellationToken cancellationToken)
        {
            string? source = FindNewestCrashReport(minecraftDirectory, versionFolder)
                ?? FindLatestLog(minecraftDirectory, versionFolder);

            if (source == null)
            {
                return new CrashAnalysis(string.Empty, Array.Empty<CrashMatch>(), Array.Empty<string>(), string.Empty);
            }

            string text = await ReadTailAsync(source, cancellationToken).ConfigureAwait(false);

            List<CrashMatch> matches = new List<CrashMatch>();

            foreach (var rule in Rules)
            {
                Match hit = Regex.Match(text, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

                if (!hit.Success)
                {
                    continue;
                }

                string line = LineOf(text, hit.Index);

                matches.Add(new CrashMatch(
                    rule.Title,
                    Trim(line, 200),
                    rule.Advice,
                    rule.Fatal));
            }

            return new CrashAnalysis(source, matches, FindSuspects(text, minecraftDirectory, versionFolder), Tail(text, 25));
        }

        // ---------- 文件定位 ----------

        internal static string? FindNewestCrashReport(string minecraftDirectory, string versionFolder)
        {
            // 实例自己的崩溃报告优先：全局 crash-reports 里可能是另一个实例的旧崩溃，
            // 直接按时间混选会把别人的错误算到当前实例头上。
            string instanceRoot = Path.Combine(
                minecraftDirectory, "versions", versionFolder, "crash-reports");

            string? own = NewestCrashReportIn(instanceRoot);
            if (own != null)
            {
                return own;
            }

            return NewestCrashReportIn(Path.Combine(minecraftDirectory, "crash-reports"));
        }

        private static string? NewestCrashReportIn(string root)
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.GetFiles(root, "crash-*.txt")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        internal static string? FindLatestLog(string minecraftDirectory, string versionFolder)
        {
            string[] candidates =
            {
                Path.Combine(minecraftDirectory, "versions", versionFolder, "logs", "latest.log"),
                Path.Combine(minecraftDirectory, "logs", "latest.log")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static async Task<string> ReadTailAsync(string path, CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0, stream.Length - MaxBytes);
                stream.Seek(start, SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
        }

        // ---------- 可疑模组 ----------

        /// <summary>从日志里找提到的 mod id / 文件名，和实际 mods 目录比对。</summary>
        internal static IReadOnlyList<string> FindSuspects(
            string text, string minecraftDirectory, string versionFolder)
        {
            List<string> suspects = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 日志里被引号括起来的 mod id：Mod ID: 'xxx'
            foreach (Match match in Regex.Matches(text, @"Mod ID:\s*'([\w\-\.]+)'", RegexOptions.IgnoreCase))
            {
                Add(match.Groups[1].Value);
            }

            // 2) requires 句子里出现的 id
            foreach (Match match in Regex.Matches(text, @"Mod\s+'?([\w\-\.]{2,})'?\s+requires", RegexOptions.IgnoreCase))
            {
                Add(match.Groups[1].Value);
            }

            // 3) mods 目录里的文件名出现在日志里
            string[] modsRoots =
            {
                Path.Combine(minecraftDirectory, "versions", versionFolder, "mods"),
                Path.Combine(minecraftDirectory, "mods")
            };

            foreach (string root in modsRoots.Where(Directory.Exists))
            {
                foreach (string file in Directory.GetFiles(root, "*.jar*"))
                {
                    string name = Path.GetFileName(file);
                    string stem = name.Split('-')[0];

                    if (stem.Length >= 3 && text.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Add(name);
                    }
                }
            }

            return suspects.Take(8).ToList();

            void Add(string value)
            {
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                {
                    suspects.Add(value);
                }
            }
        }

        // ---------- 小工具 ----------

        private static string LineOf(string text, int index)
        {
            int start = text.LastIndexOf('\n', Math.Max(0, Math.Min(index, text.Length - 1)));
            int end = text.IndexOf('\n', Math.Max(0, index));

            if (start < 0)
            {
                start = 0;
            }

            if (end < 0)
            {
                end = text.Length;
            }

            return text.Substring(start, end - start).Trim();
        }

        private static string Trim(string value, int max)
        {
            string text = (value ?? string.Empty).Trim();

            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static string Tail(string text, int lines)
        {
            string[] all = text.Replace("\r", string.Empty).Split('\n');
            int start = Math.Max(0, all.Length - lines);

            return string.Join("\n", all, start, all.Length - start).Trim();
        }
    }
}

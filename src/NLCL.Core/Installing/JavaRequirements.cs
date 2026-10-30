using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NLCL.Abstractions;

namespace NLCL.Core.Installing
{
    /// <summary>
    /// 从版本 JSON 里读出该版本要求的 Java 主版本。
    ///
    /// 为什么不"挑最新的 Java 就行"：Minecraft 各版本对 Java 有硬性范围。
    /// 1.20.1 要求 Java 17，用 Java 25 起不来（Mixin/ASM 会拒绝过新的 class 文件）；
    /// 反过来 1.21 要求 21，用 17 也起不来。PCL 用 <c>javaVersion.majorVersion</c> + 内置范围表判断。
    /// </summary>
    public static class JavaRequirements
    {
        /// <summary>沿 <c>inheritsFrom</c> 最多往上找这么多层（整合包可能套两层）。</summary>
        private const int MaxInheritDepth = 4;

        /// <summary>
        /// 读取所需 Java 主版本；版本 JSON 没写就返回 null（调用方按"随便挑"处理）。
        /// </summary>
        public static int? ReadRequiredMajor(string minecraftDirectory, string versionFolderName)
        {
            if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionFolderName))
            {
                return null;
            }

            string current = versionFolderName;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int depth = 0; depth < MaxInheritDepth; depth++)
            {
                if (!visited.Add(current))
                {
                    return null; // 继承成环，放弃
                }

                JObject? root = TryRead(Path.Combine(minecraftDirectory, "versions", current, current + ".json"));
                if (root == null)
                {
                    return null;
                }

                int? major = root.SelectToken("javaVersion.majorVersion")?.Value<int?>();
                if (major.HasValue && major.Value > 0)
                {
                    return major;
                }

                string? parent = root.Value<string>("inheritsFrom");
                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }

                current = parent!;
            }

            return null;
        }

        /// <summary>
        /// 读取版本声明的 Java 运行时组件名（例如 <c>java-runtime-gamma</c>）；没有就返回 null。
        /// 与 <see cref="ReadRequiredMajor"/> 一样沿 inheritsFrom 往上找。
        /// </summary>
        public static string? ReadComponent(string minecraftDirectory, string versionFolderName)
        {
            if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionFolderName))
            {
                return null;
            }

            string current = versionFolderName;
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int depth = 0; depth < MaxInheritDepth; depth++)
            {
                if (!visited.Add(current))
                {
                    return null;
                }

                JObject? root = TryRead(Path.Combine(minecraftDirectory, "versions", current, current + ".json"));
                if (root == null)
                {
                    return null;
                }

                string? component = root.SelectToken("javaVersion.component")?.Value<string>();
                if (!string.IsNullOrEmpty(component))
                {
                    return component;
                }

                string? parent = root.Value<string>("inheritsFrom");
                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }

                current = parent!;
            }

            return null;
        }

        private static JObject? TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// 从已安装的 Java 里挑一个能用的。
        ///
        /// 规则：优先**主版本等于要求**的（最稳）；没有时只接受"比要求高不超过
        /// <see cref="MaxNewerOffset"/> 个主版本"的，再高就返回 null 让调用方提示用户装 Java。
        ///
        /// 为什么要有上界：Minecraft 1.20.1 要 Java 17，但机器上只有 Java 25 时，
        /// 旧版 LWJGL（3.3.1）会在 Java 25 上触发
        /// "EXCEPTION_ACCESS_VIOLATION ... jni_fast_GetLongField" 这种 native 崩溃。
        /// 以前这里会静默挑"不低于要求的最小者"，等于把用户直接送上崩溃。
        /// </summary>
        public static JavaRuntime? Pick(IEnumerable<JavaRuntime> installed, int? requiredMajor)
        {
            List<JavaRuntime> all = installed?.ToList() ?? new List<JavaRuntime>();

            if (all.Count == 0)
            {
                return null;
            }

            // 先剔掉可执行文件不存在的幽灵条目（PCL 的 Java 列表可能残留已删除的运行时）
            List<JavaRuntime> list = all.Where(java => java.Exists).ToList();

            if (list.Count == 0)
            {
                return null;
            }

            if (!requiredMajor.HasValue)
            {
                // 没有要求时取最高版本（老行为）
                return list.OrderByDescending(java => java.Major).First();
            }

            JavaRuntime? exact = list
                .Where(java => java.Major == requiredMajor.Value)
                .OrderByDescending(java => java.Version)
                .FirstOrDefault();

            if (exact != null)
            {
                return exact;
            }

            // 只接受精确匹配。
            //
            // 曾经允许"略新"（required..required+4）作为折中，但那是没有自动下载能力时的权宜：
            // 实测 Java 25 跑 1.20.1 / 1.21 都会在 LWJGL 的 JNI 快速路径上 native 崩溃，
            // 而 17~21 之间的"差不多"也并不可靠。现在上层会按版本声明的 component
            // 自动下载 Mojang 运行时（见 IJavaRuntimeInstaller），所以这里宁可返回 null。
            return null;
        }

        /// <summary>
        /// 允许比要求高出多少个主版本。保留为 0：只接受精确匹配，
        /// 其余情况由 <c>IJavaRuntimeInstaller</c> 下载版本声明的运行时。
        /// </summary>
        public const int MaxNewerOffset = 0;

        /// <summary>把探测到的 Java 主版本拼成给用户看的一句话："Java 21 / Java 25"。</summary>
        public static string Describe(IEnumerable<JavaRuntime> installed)
        {
            List<int> majors = (installed ?? Enumerable.Empty<JavaRuntime>())
                .Select(java => java.Major)
                .Where(major => major > 0)
                .Distinct()
                .OrderBy(major => major)
                .ToList();

            return majors.Count == 0
                ? "（未探测到任何 Java）"
                : string.Join(" / ", majors.Select(major => "Java " + major));
        }
    }
}

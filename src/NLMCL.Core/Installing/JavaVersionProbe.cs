using System;
using System.Globalization;
using System.IO;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 从 Java 安装目录的 <c>release</c> 文件读出真实版本。
    ///
    /// 为什么需要：用户在设置里手填 <c>javaw.exe</c> 路径时，我们没有"探测"这一步，
    /// 版本号会留空，PCL 的参数管线就按默认的 Java 17 生成 JVM 参数——
    /// 给真实的 Java 8 塞 <c>-XX:+UseZGC</c>，JVM 直接拒绝启动，而且窗口一闪而过没有任何提示。
    ///
    /// <c>release</c> 文件是 JDK/JRE 自带的元数据（JDK 9+ 必有；8u 也有），
    /// 读它比跑一次 <c>java -version</c> 快得多，也不会弹控制台窗口。
    /// </summary>
    public static class JavaVersionProbe
    {
        /// <summary>读取失败（没有 release、格式不认识、无权限）时返回 null。</summary>
        public static Version? TryRead(string javaExecutable)
        {
            if (string.IsNullOrWhiteSpace(javaExecutable))
            {
                return null;
            }

            string? home = DeriveHome(javaExecutable);
            if (home == null)
            {
                return null;
            }

            string releaseFile = Path.Combine(home, "release");
            if (!File.Exists(releaseFile))
            {
                return null;
            }

            try
            {
                foreach (string line in File.ReadLines(releaseFile))
                {
                    if (line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal))
                    {
                        return Parse(line.Substring("JAVA_VERSION=".Length));
                    }
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            return null;
        }

        /// <summary>从可执行文件路径推 Java 主目录：<c>...\bin\javaw.exe</c> → <c>...</c>。</summary>
        public static string? DeriveHome(string javaExecutable)
        {
            string? directory = Path.GetDirectoryName(javaExecutable);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            if (string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Path.GetDirectoryName(directory);
                return string.IsNullOrEmpty(parent) ? null : parent;
            }

            return directory;
        }

        /// <summary>
        /// 解析 <c>release</c> 里的版本串：
        /// <c>"1.8.0_411"</c> → 8.0、<c>"17.0.9"</c> → 17.0.9、<c>"21-ea"</c> → 21.0。
        /// </summary>
        public static Version? Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string text = (raw ?? string.Empty).Trim().Trim('"').Trim();

            // 去掉 "+9"、"-ea"、"-beta" 这类后缀
            int cut = text.IndexOfAny(new[] { '+', '-', ' ' });
            if (cut > 0)
            {
                text = text.Substring(0, cut);
            }

            string[] parts = text.Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            int[] numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return null;
                }
            }

            // 1.8.0_411 这种老格式：主版本在第二段
            if (numbers[0] == 1 && numbers.Length >= 2)
            {
                return new Version(numbers[1], 0);
            }

            if (numbers.Length == 1)
            {
                return new Version(numbers[0], 0);
            }

            if (numbers.Length == 2)
            {
                return new Version(numbers[0], numbers[1]);
            }

            return new Version(numbers[0], numbers[1], numbers[2]);
        }
    }
}

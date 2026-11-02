using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>本机安装的一个 Java 运行时。</summary>
    public sealed class JavaRuntime
    {
        public JavaRuntime(string home, Version version)
        {
            Home = home ?? throw new ArgumentNullException(nameof(home));
            Version = version;
        }

        /// <summary>Java 主目录（以反斜杠结尾）。</summary>
        public string Home { get; }

        /// <summary>版本号；无法确定时为 null。</summary>
        public Version? Version { get; }

        /// <summary>主版本，例如 17、21。</summary>
        public int Major => Version?.Major ?? 0;

        /// <summary>
        /// javaw.exe 的完整路径（Windows 下无控制台窗口）。
        ///
        /// 不直接拼 `bin\javaw.exe`：PCL 记录的主目录有时本身就是 `...\bin\`，
        /// 再拼一层会得到 `bin\bin\javaw.exe`。这里按常见布局探测，
        /// 找到第一个真实存在的；都不存在时仍返回标准布局（让上层报错更可读）。
        /// </summary>
        public string ExecutablePath => ResolveExecutable();

        /// <summary>该 Java 是否真的可用（可执行文件存在）。</summary>
        public bool Exists => File.Exists(ResolveExecutable());

        private string ResolveExecutable()
        {
            string[] candidates =
            {
                Path.Combine(Home, "bin", "javaw.exe"),
                Path.Combine(Home, "bin", "java.exe"),
                Path.Combine(Home, "javaw.exe"),
                Path.Combine(Home, "java.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        public override string ToString() => Home + " (Java " + Major + ")";
    }

    /// <summary>
    /// 查找本机已安装的 Java。实现由 PCL 的 <c>JavaUtils</c> 提供。
    /// </summary>
    public interface IJavaLocator
    {
        Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 按 Mojang 的 Java 运行时清单下载某个组件（例如 <c>java-runtime-gamma</c>）。
    ///
    /// 存在的理由：不同 Minecraft 版本要求不同 Java，用户机器上往往只有其中一个。
    /// 没有时不该让用户自己去装 JDK，而是按版本 JSON 里声明的
    /// <c>javaVersion.component</c> 把 Mojang 的运行时拉下来。
    /// </summary>
    public interface IJavaRuntimeInstaller
    {
        /// <summary>
        /// 下载/修复指定组件；返回装好的运行时（失败抛异常）。
        /// 已存在的文件按 sha1 跳过，因此重复调用是增量修复。
        /// </summary>
        Task<JavaRuntime> InstallAsync(
            string component,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken);
    }
}

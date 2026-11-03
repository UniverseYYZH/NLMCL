using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>某个 Minecraft 版本可用的加载器构建（Forge / NeoForge）。</summary>
    public sealed class LoaderBuild
    {
        public LoaderBuild(string version, string minecraftVersion, DateTimeOffset? releasedAt)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            MinecraftVersion = minecraftVersion ?? string.Empty;
            ReleasedAt = releasedAt;
        }

        /// <summary>加载器版本，例如 47.4.27。</summary>
        public string Version { get; }

        public string MinecraftVersion { get; }

        public DateTimeOffset? ReleasedAt { get; }
    }

    /// <summary>外部进程执行端口（Forge 安装器是 jar，要跑 java）。</summary>
    public interface IProcessRunner
    {
        Task<ProcessResult> RunAsync(
            string executable,
            string arguments,
            string workingDirectory,
            Action<string>? onOutput,
            CancellationToken cancellationToken);
    }

    public sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
        }

        public int ExitCode { get; }

        public string Output { get; }
    }

    /// <summary>Forge 构建列表。</summary>
    public interface IForgeCatalog
    {
        Task<IReadOnlyList<LoaderBuild>> GetBuildsAsync(string minecraftVersion, CancellationToken cancellationToken);
    }

    /// <summary>NeoForge 构建列表（版本号形如 21.1.80，前两段编码了 Minecraft 版本）。</summary>
    public interface INeoForgeCatalog
    {
        Task<IReadOnlyList<LoaderBuild>> GetBuildsAsync(string minecraftVersion, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Forge / NeoForge 安装。
    ///
    /// 它们不像 Fabric 那样只要三个 JSON 请求：官方安装器是一个 jar，
    /// 内部要下载库、跑 processors（二进制补丁），必须真正执行这个 jar。
    /// 侦察结论：<c>java -jar installer.jar --installClient &lt;目录&gt;</c> 可用，
    /// 唯一前置是目标目录里存在 <c>launcher_profiles.json</c>。
    /// </summary>
    public interface ILoaderInstaller
    {
        /// <summary>返回安装后版本目录的名称（例如 1.20.1-forge-47.4.27）。</summary>
        Task<string> InstallForgeAsync(
            string minecraftVersion,
            string loaderVersion,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken);

        /// <summary>NeoForge 安装；安装器与 Forge 同源，只是 maven 与版本命名不同。</summary>
        Task<string> InstallNeoForgeAsync(
            string minecraftVersion,
            string loaderVersion,
            string minecraftDirectory,
            string javaExecutable,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 一次诊断包导出所需的显式上下文。
    /// 不让 Core 猜“当前实例”，避免多开/切换实例时把 A 的日志塞进 B 的包。
    /// </summary>
    public sealed class DiagnosticPackageRequest
    {
        public DiagnosticPackageRequest(
            string minecraftDirectory,
            string versionFolder,
            string gameDirectory,
            string modsDirectory,
            string instanceSettingsFile)
        {
            MinecraftDirectory = minecraftDirectory ?? throw new ArgumentNullException(nameof(minecraftDirectory));
            VersionFolder = versionFolder ?? throw new ArgumentNullException(nameof(versionFolder));
            GameDirectory = gameDirectory ?? throw new ArgumentNullException(nameof(gameDirectory));
            ModsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
            InstanceSettingsFile = instanceSettingsFile ?? throw new ArgumentNullException(nameof(instanceSettingsFile));
        }

        public string MinecraftDirectory { get; }

        public string VersionFolder { get; }

        /// <summary>实例实际使用的游戏目录（隔离时为 versions\&lt;id&gt;，否则为 .minecraft 根）。</summary>
        public string GameDirectory { get; }

        public string ModsDirectory { get; }

        public string InstanceSettingsFile { get; }

        public string AppVersion { get; set; } = string.Empty;

        public IReadOnlyList<string> JavaHomes { get; set; } = Array.Empty<string>();
    }

    public sealed class DiagnosticPackageResult
    {
        public DiagnosticPackageResult(string zipPath, IReadOnlyList<string> missing)
        {
            ZipPath = zipPath ?? throw new ArgumentNullException(nameof(zipPath));
            Missing = missing ?? throw new ArgumentNullException(nameof(missing));
        }

        public string ZipPath { get; }

        public IReadOnlyList<string> Missing { get; }
    }

    /// <summary>把排障材料打成一个可发送的 zip；绝不读取账号文件。</summary>
    public interface IDiagnosticPackageService
    {
        Task<DiagnosticPackageResult> CreateAsync(
            DiagnosticPackageRequest request,
            string destinationZip,
            CancellationToken cancellationToken);
    }
}

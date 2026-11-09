using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>全盘扫描进度。</summary>
    public sealed class GameDirectoryScanProgress
    {
        public GameDirectoryScanProgress(
            string stage,
            string currentDirectory,
            int scannedDirectories,
            int foundCount,
            string? foundDirectory = null)
        {
            Stage = stage;
            CurrentDirectory = currentDirectory;
            ScannedDirectories = scannedDirectories;
            FoundCount = foundCount;
            FoundDirectory = foundDirectory;
        }

        public string Stage { get; }

        public string CurrentDirectory { get; }

        public int ScannedDirectories { get; }

        public int FoundCount { get; }

        /// <summary>本次进度新发现的目录；没有新发现时为 null。</summary>
        public string? FoundDirectory { get; }
    }

    /// <summary>扫描整台电脑里的 .minecraft 目录。</summary>
    public interface IGameDirectoryScanner
    {
        Task<IReadOnlyList<string>> ScanAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<string>> ScanAsync(
            IProgress<GameDirectoryScanProgress>? progress,
            CancellationToken cancellationToken);
    }
}

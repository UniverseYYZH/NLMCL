using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>安全模式里被临时禁用的一个 Mod 文件。</summary>
    public sealed class SafeModeEntry
    {
        public SafeModeEntry(string originalPath, string disabledPath)
        {
            OriginalPath = originalPath ?? throw new ArgumentNullException(nameof(originalPath));
            DisabledPath = disabledPath ?? throw new ArgumentNullException(nameof(disabledPath));
        }

        public string OriginalPath { get; }

        public string DisabledPath { get; }
    }

    /// <summary>
    /// 一次安全模式启动的租约。租约写入磁盘，进程异常退出后仍能恢复。
    /// </summary>
    public sealed class SafeModeSession
    {
        public SafeModeSession(
            string modsDirectory,
            string journalPath,
            IReadOnlyList<SafeModeEntry> entries)
        {
            ModsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
            JournalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public string ModsDirectory { get; }

        public string JournalPath { get; }

        public IReadOnlyList<SafeModeEntry> Entries { get; }

        public int DisabledCount => Entries.Count;
    }

    /// <summary>恢复安全模式文件的结果。</summary>
    public sealed class SafeModeRestoreResult
    {
        public SafeModeRestoreResult(
            int restored,
            int alreadyRestored,
            IReadOnlyList<string> warnings)
        {
            Restored = restored;
            AlreadyRestored = alreadyRestored;
            Warnings = warnings ?? Array.Empty<string>();
        }

        public int Restored { get; }

        public int AlreadyRestored { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>启动时补恢复遗留安全模式会话的结果。</summary>
    public sealed class SafeModeRecoveryResult
    {
        public SafeModeRecoveryResult(
            int journals,
            int restored,
            IReadOnlyList<string> warnings)
        {
            Journals = journals;
            Restored = restored;
            Warnings = warnings ?? Array.Empty<string>();
        }

        public int Journals { get; }

        public int Restored { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// 安全模式：本次启动临时禁用 mods 目录中的所有 jar，退出/崩溃后恢复。
    /// 只负责文件租约，不负责启动游戏。
    /// </summary>
    public interface ISafeModeService
    {
        Task<SafeModeSession> BeginAsync(string modsDirectory, CancellationToken cancellationToken);

        Task<SafeModeRestoreResult> RestoreAsync(
            SafeModeSession session, CancellationToken cancellationToken);

        /// <summary>应用启动时调用，恢复上次异常退出留下的会话。</summary>
        Task<SafeModeRecoveryResult> RecoverPendingAsync(CancellationToken cancellationToken);
    }
}

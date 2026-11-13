using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLMCL.Abstractions;

namespace NLMCL.Core.SafeMode
{
    /// <summary>
    /// 文件级安全模式：把 mods 下所有启用的 jar 改名为 .jar.disabled，
    /// 并把映射写入 DataRoot\safe-mode 下的日志。正常退出或下一次启动时恢复。
    /// </summary>
    public sealed class SafeModeService : ISafeModeService
    {
        private const string DisabledSuffix = ".disabled";
        private readonly string _journalDirectory;

        public SafeModeService(string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                throw new ArgumentException("数据目录不能为空", nameof(dataRoot));
            }

            _journalDirectory = Path.Combine(Path.GetFullPath(dataRoot), "safe-mode");
        }

        public Task<SafeModeSession> BeginAsync(
            string modsDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directory = NormalizeDirectory(modsDirectory);

            if (!Directory.Exists(directory))
            {
                return Task.FromResult(new SafeModeSession(
                    directory, string.Empty, Array.Empty<SafeModeEntry>()));
            }

            string journalPath = GetJournalPath(directory);

            // 上一次异常退出来不及恢复：先恢复，避免在旧租约上再叠一层。
            if (File.Exists(journalPath))
            {
                SafeModeRestoreResult previous = RestoreJournal(journalPath, cancellationToken);
                if (previous.Warnings.Count > 0)
                {
                    throw new InvalidOperationException(
                        "上一次安全模式还有文件未能恢复：" + string.Join("；", previous.Warnings));
                }
            }

            string[] jars = Directory.GetFiles(directory, "*.jar", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (jars.Length == 0)
            {
                return Task.FromResult(new SafeModeSession(
                    directory, string.Empty, Array.Empty<SafeModeEntry>()));
            }

            List<SafeModeEntry> entries = new List<SafeModeEntry>();
            foreach (string jar in jars)
            {
                string disabled = jar + DisabledSuffix;
                if (File.Exists(disabled))
                {
                    throw new InvalidOperationException(
                        "无法进入安全模式：" + Path.GetFileName(disabled)
                        + " 已存在，请先在 Mod 管理里处理同名禁用文件。");
                }

                entries.Add(new SafeModeEntry(jar, disabled));
            }

            Directory.CreateDirectory(_journalDirectory);
            WriteJournal(journalPath, directory, entries);

            List<SafeModeEntry> moved = new List<SafeModeEntry>();
            try
            {
                foreach (SafeModeEntry entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(entry.OriginalPath, entry.DisabledPath);
                    moved.Add(entry);
                }
            }
            catch
            {
                // 移动到一半失败：把已移动的恢复，不留下半截安全模式。
                bool rolledBack = true;
                foreach (SafeModeEntry entry in moved)
                {
                    rolledBack &= TryMoveBack(entry);
                }

                // 回滚不完整时必须保留 journal，否则下次启动再也找不到这些
                // 被改成 .disabled 的文件。
                if (rolledBack)
                {
                    TryDelete(journalPath);
                }

                throw;
            }

            return Task.FromResult(new SafeModeSession(directory, journalPath, entries));
        }

        public Task<SafeModeRestoreResult> RestoreAsync(
            SafeModeSession session, CancellationToken cancellationToken)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (string.IsNullOrWhiteSpace(session.JournalPath))
            {
                return Task.FromResult(new SafeModeRestoreResult(
                    0, 0, Array.Empty<string>()));
            }

            return Task.FromResult(RestoreJournal(session.JournalPath, cancellationToken));
        }

        public Task<SafeModeRecoveryResult> RecoverPendingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(_journalDirectory))
            {
                return Task.FromResult(new SafeModeRecoveryResult(
                    0, 0, Array.Empty<string>()));
            }

            int journals = 0;
            int restored = 0;
            List<string> warnings = new List<string>();

            foreach (string journal in Directory.GetFiles(
                _journalDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                journals++;

                SafeModeRestoreResult result = RestoreJournal(journal, cancellationToken);
                restored += result.Restored;
                warnings.AddRange(result.Warnings);
            }

            return Task.FromResult(new SafeModeRecoveryResult(
                journals, restored, warnings));
        }

        private static SafeModeRestoreResult RestoreJournal(
            string journalPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JournalDto? journal;
            try
            {
                string json = File.ReadAllText(journalPath);
                journal = JsonConvert.DeserializeObject<JournalDto>(json);
            }
            catch (Exception ex)
            {
                TryDelete(journalPath);
                return new SafeModeRestoreResult(
                    0, 0, new[] { "安全模式日志损坏，已跳过：" + ex.Message });
            }

            if (journal == null || string.IsNullOrWhiteSpace(journal.ModsDirectory))
            {
                TryDelete(journalPath);
                return new SafeModeRestoreResult(
                    0, 0, new[] { "安全模式日志为空，已跳过。" });
            }

            string directory;
            try
            {
                directory = NormalizeDirectory(journal.ModsDirectory);
            }
            catch (Exception ex)
            {
                TryDelete(journalPath);
                return new SafeModeRestoreResult(
                    0, 0, new[] { "安全模式目录无效，已跳过：" + ex.Message });
            }

            int restored = 0;
            int already = 0;
            // 两边都存在时不能替用户猜哪一个是“对的”，保留 journal，下次还能再恢复。
            bool unresolved = false;
            List<string> warnings = new List<string>();

            foreach (EntryDto entry in journal.Entries ?? new List<EntryDto>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsInside(directory, entry.OriginalPath)
                    || !IsInside(directory, entry.DisabledPath))
                {
                    warnings.Add("安全模式记录越界，已跳过：" + entry.OriginalPath);
                    continue;
                }

                bool hasOriginal = File.Exists(entry.OriginalPath);
                bool hasDisabled = File.Exists(entry.DisabledPath);

                if (hasOriginal && hasDisabled)
                {
                    unresolved = true;
                    warnings.Add("原文件和禁用文件同时存在，未覆盖：" + Path.GetFileName(entry.OriginalPath));
                    continue;
                }

                if (!hasDisabled)
                {
                    if (hasOriginal)
                    {
                        already++;
                    }
                    else
                    {
                        warnings.Add("找不到安全模式文件：" + Path.GetFileName(entry.OriginalPath));
                    }

                    continue;
                }

                try
                {
                    File.Move(entry.DisabledPath, entry.OriginalPath);
                    restored++;
                }
                catch (Exception ex)
                {
                    unresolved = true;
                    warnings.Add("恢复失败：" + Path.GetFileName(entry.OriginalPath) + " " + ex.Message);
                }
            }

            if (!unresolved)
            {
                TryDelete(journalPath);
            }

            return new SafeModeRestoreResult(restored, already, warnings);
        }

        private static void WriteJournal(
            string journalPath, string directory, IReadOnlyList<SafeModeEntry> entries)
        {
            JournalDto dto = new JournalDto
            {
                ModsDirectory = directory,
                StartedAt = DateTimeOffset.Now,
                Entries = entries
                    .Select(entry => new EntryDto
                    {
                        OriginalPath = entry.OriginalPath,
                        DisabledPath = entry.DisabledPath
                    })
                    .ToList()
            };

            string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
            string temporary = journalPath + ".tmp";
            File.WriteAllText(temporary, json, Encoding.UTF8);

            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }

            File.Move(temporary, journalPath);
        }

        private string GetJournalPath(string directory)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(directory.ToLowerInvariant()));
                string key = BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16);
                return Path.Combine(_journalDirectory, "safe-mode-" + key + ".json");
            }
        }

        private static bool IsInside(string directory, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string full = Path.GetFullPath(path);
                string prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("目录不能为空", nameof(path));
            }

            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool TryMoveBack(SafeModeEntry entry)
        {
            try
            {
                if (File.Exists(entry.DisabledPath) && !File.Exists(entry.OriginalPath))
                {
                    File.Move(entry.DisabledPath, entry.OriginalPath);
                }

                return true;
            }
            catch
            {
                // 回滚失败时保留 journal，下一次启动还能再试。
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 删除失败不影响游戏；下一次启动再处理。
            }
        }

        private sealed class JournalDto
        {
            public string ModsDirectory { get; set; } = string.Empty;

            public DateTimeOffset StartedAt { get; set; }

            public List<EntryDto> Entries { get; set; } = new List<EntryDto>();
        }

        private sealed class EntryDto
        {
            public string OriginalPath { get; set; } = string.Empty;

            public string DisabledPath { get; set; } = string.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Maintenance
{
    /// <summary>
    /// 全盘查找 .minecraft 目录。
    ///
    /// 只用于用户主动点击「扫描全盘」：
    /// - 遍历所有就绪磁盘 + 用户目录
    /// - 限制深度/目录数，跳过系统目录、回收站、node_modules 等
    /// - 不进入 .minecraft 内部继续找（避免无意义深搜）
    /// </summary>
    public sealed class GameDirectoryScanner : IGameDirectoryScanner
    {
        private const string FolderName = ".minecraft";

        private static readonly HashSet<string> SkippedNames = new HashSet<string>(
            new[]
            {
                "windows",
                "program files",
                "program files (x86)",
                "programdata",
                "recovery",
                "perflogs",
                "$recycle.bin",
                "system volume information",
                "temp",
                "node_modules",
                ".git",
                ".vs",
                "packages",
                "nuget",
                "obj",
                "bin",
                "__pycache__"
            },
            StringComparer.OrdinalIgnoreCase);

        private readonly IReadOnlyList<string>? _roots;
        private readonly int _maxDepth;
        private readonly int _maxDirectories;

        public GameDirectoryScanner(
            IReadOnlyList<string>? roots = null,
            int maxDepth = 6,
            int maxDirectories = 40000)
        {
            _roots = roots;
            _maxDepth = Math.Max(1, maxDepth);
            _maxDirectories = Math.Max(500, maxDirectories);
        }

        public Task<IReadOnlyList<string>> ScanAsync(CancellationToken cancellationToken)
            => ScanAsync(null, cancellationToken);

        public Task<IReadOnlyList<string>> ScanAsync(
            IProgress<GameDirectoryScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<string>>(() =>
            {
                List<string> found = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int scanned = 0;

                List<string> roots;

                if (_roots != null)
                {
                    roots = NormalizeRoots(_roots);
                }
                else
                {
                    // 先直接定位标准位置；命中后不再扫 C 盘其余部分。
                    string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string primary = NormalizeDirectory(Path.Combine(roaming, FolderName));

                    if (Directory.Exists(primary) && seen.Add(primary))
                    {
                        found.Add(primary);
                        progress?.Report(new GameDirectoryScanProgress(
                            "定位默认目录", primary, 0, 1, primary));
                    }

                    roots = GetOtherDriveRoots();

                    if (!Directory.Exists(primary))
                    {
                        // 标准位置不存在时才退回用户目录，仍不扫整个 C 盘。
                        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                        if (!string.IsNullOrWhiteSpace(profile))
                        {
                            roots.Add(profile);
                        }
                    }
                }

                roots = NormalizeRoots(roots);

                foreach (string root in roots)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new GameDirectoryScanProgress(
                        "扫描 " + root, root, scanned, found.Count));

                    ScanRoot(
                        root,
                        _maxDepth,
                        _maxDirectories,
                        found,
                        seen,
                        cancellationToken,
                        current =>
                        {
                            scanned++;
                            progress?.Report(new GameDirectoryScanProgress(
                                "扫描中", current, scanned, found.Count));
                        },
                        discovered =>
                        {
                            progress?.Report(new GameDirectoryScanProgress(
                                "发现目录", discovered, scanned, found.Count, discovered));
                        });
                }

                return found;
            }, cancellationToken);
        }

        private static List<string> NormalizeRoots(IEnumerable<string> roots)
        {
            return roots
                .Select(NormalizeDirectory)
                .Where(item => item.Length > 0 && Directory.Exists(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>扫描一个根目录；公开出来是为了单测不依赖真实磁盘布局。</summary>
        public static void ScanRoot(
            string root,
            int maxDepth,
            int maxDirectories,
            ICollection<string> found,
            ISet<string> seen,
            CancellationToken cancellationToken,
            Action<string>? onDirectory = null,
            Action<string>? onFound = null)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            Queue<(string Path, int Depth)> queue = new Queue<(string, int)>();
            queue.Enqueue((NormalizeDirectory(root), 0));
            int visited = 0;

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                (string current, int depth) = queue.Dequeue();

                if (++visited > maxDirectories)
                {
                    return;
                }

                onDirectory?.Invoke(current);

                string name = Path.GetFileName(
                    current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                if (string.Equals(name, FolderName, StringComparison.OrdinalIgnoreCase))
                {
                    if (seen.Add(current))
                    {
                        found.Add(current);
                        onFound?.Invoke(current);
                    }

                    continue;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                IEnumerable<string> children;

                try
                {
                    children = Directory.GetDirectories(current);
                }
                catch (Exception ex) when (
                    ex is UnauthorizedAccessException
                    || ex is IOException
                    || ex is DirectoryNotFoundException)
                {
                    continue;
                }

                foreach (string child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string childName = Path.GetFileName(
                        child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                    if (SkippedNames.Contains(childName))
                    {
                        continue;
                    }

                    try
                    {
                        FileAttributes attributes = File.GetAttributes(child);

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch (Exception ex) when (
                        ex is UnauthorizedAccessException
                        || ex is IOException
                        || ex is DirectoryNotFoundException)
                    {
                        continue;
                    }

                    queue.Enqueue((NormalizeDirectory(child), depth + 1));
                }
            }
        }

        private static List<string> GetOtherDriveRoots()
        {
            List<string> roots = new List<string>();

            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    if (drive.DriveType != DriveType.Fixed
                        && drive.DriveType != DriveType.Removable)
                    {
                        continue;
                    }

                    string root = drive.RootDirectory.FullName;

                    // 标准 .minecraft 已经优先定位过；C 盘其余部分不再扫。
                    if (string.Equals(
                            drive.Name.TrimEnd('\\'), "C:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    roots.Add(root);
                }
            }
            catch (Exception)
            {
                // 枚举失败时返回空列表
            }

            return roots;
        }

        private static string NormalizeDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            try
            {
                string full = Path.GetFullPath(directory!);
                string root = Path.GetPathRoot(full) ?? string.Empty;

                return full.Length > root.Length
                    ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : full;
            }
            catch (Exception)
            {
                return directory!.Trim();
            }
        }
    }
}

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
    /// 清理启动器自己的缓存。只处理：
    ///   - 当前游戏目录下超过 6 小时的 *.parts 下载分块目录
    ///   - %TEMP%\nlmcl-diag-* 诊断临时目录
    ///   - %TEMP%\nlmcl-skin-*.png / nlmcl-mua-*.png / nlmcl-avatar-*.png 等旧皮肤临时图
    ///   - %TEMP%\nlmcl-download.log / nlmcl-startup.log
    ///   - %APPDATA%\NLMCL\logs 下 14 天前的日志
    ///   - settings.json.broken
    ///
    /// 刻意不碰 assets / libraries / versions / saves / mods / config：
    /// 那些不是缓存，删了会破坏实例。
    /// </summary>
    public sealed class CacheCleaner : ICacheCleaner
    {
        public const string DownloadPartsId = "download-parts";
        public const string TempDiagnosticsId = "temp-diagnostics";
        public const string TempLogsId = "temp-logs";
        public const string SkinTempId = "skin-temp";
        public const string OldLogsId = "old-logs";
        public const string BrokenSettingsId = "broken-settings";

        private static readonly TimeSpan DownloadPartsMinAge = TimeSpan.FromHours(6);
        private static readonly TimeSpan OldLogMinAge = TimeSpan.FromDays(14);

        private readonly IAppPaths _paths;
        private readonly string _tempRoot;

        public CacheCleaner(IAppPaths paths)
            : this(paths, Path.GetTempPath())
        {
        }

        /// <summary>测试可注入临时目录，避免扫到真实 %TEMP%。</summary>
        public CacheCleaner(IAppPaths paths, string tempRoot)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _tempRoot = string.IsNullOrWhiteSpace(tempRoot) ? Path.GetTempPath() : tempRoot;
        }

        public Task<IReadOnlyList<CacheCategory>> ScanAsync(
            string gameDirectory, CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<CacheCategory>>(
                () => Scan(gameDirectory, cancellationToken)
                    .Where(item => item.Entries.Count > 0)
                    .Select(item => item.ToCategory())
                    .ToList(),
                cancellationToken);
        }

        public Task<CacheCleanupResult> CleanAsync(
            string gameDirectory,
            IReadOnlyCollection<string> categoryIds,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () => Clean(gameDirectory, categoryIds, cancellationToken),
                cancellationToken);
        }

        private CacheCleanupResult Clean(
            string gameDirectory,
            IReadOnlyCollection<string> categoryIds,
            CancellationToken cancellationToken)
        {
            HashSet<string> selected = new HashSet<string>(
                categoryIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            long freed = 0;
            int deleted = 0;
            List<string> errors = new List<string>();

            foreach (CategoryScan category in Scan(gameDirectory, cancellationToken))
            {
                if (!selected.Contains(category.Id))
                {
                    continue;
                }

                foreach (CacheEntry entry in category.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsPattern)
                    {
                        foreach (string file in EnumerateFilesSafe(entry.Root, entry.Path))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (!IsUnder(file, entry.Root))
                            {
                                errors.Add("拒绝删除根目录之外的路径：" + file);
                                continue;
                            }

                            try
                            {
                                if (!File.Exists(file))
                                {
                                    continue;
                                }

                                long size = FileSize(file);
                                File.Delete(file);
                                freed += size;
                                deleted++;
                            }
                            catch (Exception ex) when (
                                ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                            {
                                errors.Add(Path.GetFileName(file) + "：" + ex.Message);
                            }
                        }

                        continue;
                    }

                    if (!IsUnder(entry.Path, entry.Root))
                    {
                        errors.Add("拒绝删除根目录之外的路径：" + entry.Path);
                        continue;
                    }

                    try
                    {
                        if (entry.IsDirectory)
                        {
                            if (Directory.Exists(entry.Path))
                            {
                                Directory.Delete(entry.Path, recursive: true);
                                freed += entry.SizeBytes;
                                deleted++;
                            }
                        }
                        else if (File.Exists(entry.Path))
                        {
                            File.Delete(entry.Path);
                            freed += entry.SizeBytes;
                            deleted++;
                        }
                    }
                    catch (Exception ex) when (
                        ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                    {
                        errors.Add(Path.GetFileName(entry.Path) + "：" + ex.Message);
                    }
                }
            }

            return new CacheCleanupResult(freed, deleted, errors);
        }

        private List<CategoryScan> Scan(string gameDirectory, CancellationToken cancellationToken)
        {
            List<CategoryScan> result = new List<CategoryScan>();

            result.Add(ScanDownloadParts(gameDirectory, cancellationToken));
            result.Add(ScanTempDiagnostics(cancellationToken));
            result.Add(ScanSkinTemp(cancellationToken));
            result.Add(ScanTempLogs(cancellationToken));
            result.Add(ScanOldLogs(cancellationToken));
            result.Add(ScanBrokenSettings(cancellationToken));

            return result;
        }

        private CategoryScan ScanDownloadParts(string gameDirectory, CancellationToken cancellationToken)
        {
            CategoryScan category = new CategoryScan(
                DownloadPartsId,
                "下载分块缓存",
                "未完成的下载分块（超过 6 小时）；删除后对应下载会从头开始。");

            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            {
                return category;
            }

            DateTime cutoff = DateTime.UtcNow - DownloadPartsMinAge;

            foreach (string directory in EnumerateDirectoriesSafe(
                gameDirectory, "*.parts", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                    {
                        continue;
                    }

                    category.Entries.Add(new CacheEntry(
                        directory, true, DirectorySize(directory), gameDirectory));
                }
                catch (Exception ex) when (
                    ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 单个目录不可读不影响其他缓存。
                }
            }

            return category;
        }

        private CategoryScan ScanTempDiagnostics(CancellationToken cancellationToken)
        {
            CategoryScan category = new CategoryScan(
                TempDiagnosticsId,
                "诊断临时目录",
                "生成诊断包失败后可能留下的 nlmcl-diag-* 临时目录。");

            foreach (string directory in EnumerateDirectoriesSafe(
                _tempRoot, "nlmcl-diag-*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    category.Entries.Add(new CacheEntry(
                        directory, true, DirectorySize(directory), _tempRoot));
                }
                catch (Exception ex) when (
                    ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }

            return category;
        }

        private CategoryScan ScanSkinTemp(CancellationToken cancellationToken)
        {
            CategoryScan category = new CategoryScan(
                SkinTempId,
                "皮肤临时文件",
                "旧版本留下的皮肤预览、头像、查询与默认皮肤 PNG；删除后会自动重新生成。");

            string[] patterns =
            {
                "nlmcl-skin-*.png",
                "nlmcl-mua-*.png",
                "nlmcl-avatar-*.png",
                "nlmcl-lookup-*.png",
                "nlmcl-default-skin*.png"
            };

            foreach (string pattern in patterns)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long size = 0;
                int count = 0;

                foreach (string file in EnumerateFilesSafe(_tempRoot, pattern))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    size += FileSize(file);
                    count++;
                }

                if (count > 0)
                {
                    // 用通配符作为一条聚合记录：历史遗留可能有几十万张，
                    // 不能为每个文件建一条 CacheEntry，否则设置页会先被内存拖垮。
                    category.Entries.Add(new CacheEntry(
                        pattern, false, size, _tempRoot, count, isPattern: true));
                }
            }

            return category;
        }

        private CategoryScan ScanTempLogs(CancellationToken cancellationToken)
        {
            CategoryScan category = new CategoryScan(
                TempLogsId,
                "临时日志",
                "%TEMP% 下的下载/启动排查日志；删除不影响游戏。");

            foreach (string name in new[] { "nlmcl-download.log", "nlmcl-startup.log" })
            {
                cancellationToken.ThrowIfCancellationRequested();

                string path = Path.Combine(_tempRoot, name);

                if (File.Exists(path))
                {
                    category.Entries.Add(new CacheEntry(path, false, FileSize(path), _tempRoot));
                }
            }

            return category;
        }

        private CategoryScan ScanOldLogs(CancellationToken cancellationToken)
        {
            CategoryScan category = new CategoryScan(
                OldLogsId,
                "旧日志",
                "14 天前的崩溃/控制台日志；保留最近日志用于排查。");

            string logs = Path.Combine(_paths.DataRoot, "logs");

            if (!Directory.Exists(logs))
            {
                return category;
            }

            DateTime cutoff = DateTime.UtcNow - OldLogMinAge;

            foreach (string file in EnumerateFilesSafe(logs, "*.log"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        category.Entries.Add(new CacheEntry(file, false, FileSize(file), logs));
                    }
                }
                catch (Exception ex) when (
                    ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }

            return category;
        }

        private CategoryScan ScanBrokenSettings(CancellationToken cancellationToken)
        {
            CategoryScan category = new CategoryScan(
                BrokenSettingsId,
                "损坏设置备份",
                "settings.json 解析失败时留下的 .broken 文件。");

            string broken = _paths.SettingsFile + ".broken";

            if (File.Exists(broken))
            {
                category.Entries.Add(new CacheEntry(
                    broken, false, FileSize(broken), _paths.DataRoot));
            }

            return category;
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(
            string root, string pattern, SearchOption searchOption)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.EnumerateDirectories(root, pattern, searchOption).ToList();
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                return Array.Empty<string>();
            }
        }

        private static long DirectorySize(string directory)
        {
            try
            {
                return Directory
                    .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(file => new FileInfo(file))
                    .Where(file => file.Exists)
                    .Sum(file => file.Length);
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                return 0;
            }
        }

        private static long FileSize(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return 0;
            }
        }

        private static bool IsUnder(string path, string root)
        {
            try
            {
                string fullPath = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return fullPath.StartsWith(
                    fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (
                ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        private sealed class CategoryScan
        {
            public CategoryScan(string id, string title, string description)
            {
                Id = id;
                Title = title;
                Description = description;
            }

            public string Id { get; }

            public string Title { get; }

            public string Description { get; }

            public List<CacheEntry> Entries { get; } = new List<CacheEntry>();

            public CacheCategory ToCategory()
            {
                return new CacheCategory(
                    Id, Title, Description,
                    Entries.Sum(item => item.SizeBytes),
                    Entries.Sum(item => item.FileCount));
            }
        }

        private sealed class CacheEntry
        {
            public CacheEntry(
                string path,
                bool isDirectory,
                long sizeBytes,
                string root,
                int fileCount = 1,
                bool isPattern = false)
            {
                Path = path;
                IsDirectory = isDirectory;
                SizeBytes = sizeBytes;
                Root = root;
                FileCount = Math.Max(1, fileCount);
                IsPattern = isPattern;
            }

            public string Path { get; }

            public bool IsDirectory { get; }

            public long SizeBytes { get; }

            public string Root { get; }

            public int FileCount { get; }

            public bool IsPattern { get; }
        }
    }
}

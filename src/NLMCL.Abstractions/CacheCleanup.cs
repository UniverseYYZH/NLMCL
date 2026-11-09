using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>一类可清理的缓存。</summary>
    public sealed class CacheCategory
    {
        public CacheCategory(string id, string title, string description, long sizeBytes, int itemCount)
        {
            Id = id;
            Title = title;
            Description = description;
            SizeBytes = sizeBytes;
            ItemCount = itemCount;
        }

        /// <summary>稳定 id，清理时用它选择类别。</summary>
        public string Id { get; }

        public string Title { get; }

        public string Description { get; }

        public long SizeBytes { get; }

        public int ItemCount { get; }
    }

    /// <summary>一次清理的结果。</summary>
    public sealed class CacheCleanupResult
    {
        public CacheCleanupResult(long freedBytes, int deletedCount, IReadOnlyList<string> errors)
        {
            FreedBytes = freedBytes;
            DeletedCount = deletedCount;
            Errors = errors;
        }

        public long FreedBytes { get; }

        public int DeletedCount { get; }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>缓存扫描与清理。只处理启动器自己的临时文件，不碰存档、mods、版本。</summary>
    public interface ICacheCleaner
    {
        Task<IReadOnlyList<CacheCategory>> ScanAsync(string gameDirectory, CancellationToken cancellationToken);

        Task<CacheCleanupResult> CleanAsync(
            string gameDirectory,
            IReadOnlyCollection<string> categoryIds,
            CancellationToken cancellationToken);
    }
}

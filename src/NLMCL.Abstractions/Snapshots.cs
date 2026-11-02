using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 一个实例快照：某次备份下来的 存档 + 配置 + 选项。
    ///
    /// 不是整包备份（不含 mods / 资源包 / 库），所以通常只有几十 MB，
    /// 目的是"折腾坏了能退回去"，不是灾难恢复。
    /// </summary>
    public sealed class SnapshotInfo
    {
        public SnapshotInfo(
            string id,
            string versionId,
            string folderName,
            string note,
            DateTimeOffset createdAt,
            long sizeBytes,
            int fileCount,
            IReadOnlyList<string> items)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            VersionId = versionId ?? string.Empty;
            FolderName = folderName ?? string.Empty;
            Note = note ?? string.Empty;
            CreatedAt = createdAt;
            SizeBytes = sizeBytes;
            FileCount = fileCount;
            Items = items ?? Array.Empty<string>();
        }

        /// <summary>快照目录名，也是唯一标识。</summary>
        public string Id { get; }

        public string VersionId { get; }

        public string FolderName { get; }

        public string Note { get; }

        public DateTimeOffset CreatedAt { get; }

        public long SizeBytes { get; }

        public int FileCount { get; }

        /// <summary>实际备份了哪些项（saves / config / options.txt）。</summary>
        public IReadOnlyList<string> Items { get; }

        /// <summary>界面上显示的一行标题。</summary>
        public string Title => string.IsNullOrWhiteSpace(Note) ? "未命名快照" : Note;

        public string SizeText
        {
            get
            {
                double mb = SizeBytes / 1024.0 / 1024.0;
                return mb >= 1 ? mb.ToString("0.0") + " MB" : (SizeBytes / 1024.0).ToString("0") + " KB";
            }
        }

        /// <summary>「2026-11-02 22:10 · 12.3 MB · 42 个文件」。</summary>
        public string Summary => CreatedAt.ToString("yyyy-MM-dd HH:mm") + " · " + SizeText
            + " · " + FileCount + " 个文件";
    }

    /// <summary>快照的创建、列出、回滚与删除。</summary>
    public interface ISnapshotService
    {
        Task<IReadOnlyList<SnapshotInfo>> ListAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 备份 <paramref name="versionFolder"/> 这个实例的 saves / config / options.txt。
        /// 隔离实例的数据在 versions\&lt;实例&gt;\，非隔离在 .minecraft 根，实现要自己判断。
        /// </summary>
        Task<SnapshotInfo> CreateAsync(
            string minecraftDirectory, string versionFolder, string note, CancellationToken cancellationToken);

        /// <summary>回滚。实现必须先把当前状态存成保护快照，再覆盖。</summary>
        Task RestoreAsync(SnapshotInfo snapshot, string minecraftDirectory, CancellationToken cancellationToken);

        Task DeleteAsync(string snapshotId, CancellationToken cancellationToken);
    }
}

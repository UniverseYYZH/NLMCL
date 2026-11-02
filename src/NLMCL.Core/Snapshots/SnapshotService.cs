using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Snapshots
{
    /// <summary>
    /// 实例快照：备份 / 回滚 <c>saves</c>、<c>config</c>、<c>options.txt</c>。
    ///
    /// 数据位置要按"版本隔离"判断——这是 v1.0.0 的 bug 所在：
    /// PCL 的隔离实例把存档放在 <c>versions\&lt;实例&gt;\</c>，
    /// 老实现只在 <c>.minecraft</c> 根找，于是一备份就报"没有可备份的内容"。
    /// 这里两个位置都看，优先隔离目录。
    /// </summary>
    public sealed class SnapshotService : ISnapshotService
    {
        internal static readonly string[] Items = { "saves", "config", "options.txt" };

        public const string MetadataFileName = "snapshot.json";

        private readonly IAppPaths _paths;

        public SnapshotService(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public Task<IReadOnlyList<SnapshotInfo>> ListAsync(CancellationToken cancellationToken)
        {
            List<SnapshotInfo> result = new List<SnapshotInfo>();

            string root = _paths.SnapshotDirectory;
            if (!Directory.Exists(root))
            {
                return Task.FromResult<IReadOnlyList<SnapshotInfo>>(result);
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                SnapshotInfo? info = ReadMetadata(directory);
                if (info != null)
                {
                    result.Add(info);
                }
            }

            return Task.FromResult<IReadOnlyList<SnapshotInfo>>(
                result.OrderByDescending(item => item.CreatedAt).ToList());
        }

        public Task<SnapshotInfo> CreateAsync(
            string minecraftDirectory, string versionFolder, string note, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(minecraftDirectory))
            {
                throw new ArgumentException("游戏目录为空。", nameof(minecraftDirectory));
            }

            string dataDirectory = ResolveDataDirectory(minecraftDirectory, versionFolder);

            List<string> present = Items
                .Where(item => Exists(Path.Combine(dataDirectory, item)))
                .ToList();

            if (present.Count == 0)
            {
                throw new InvalidOperationException(
                    "没有可备份的内容：在 " + dataDirectory + " 下没有找到 saves、config 或 options.txt。"
                    + "先进入游戏保存一次，或在实例里玩过再备份。");
            }

            string folder = string.IsNullOrWhiteSpace(versionFolder) ? "unknown" : versionFolder!;
            string id = BuildId(folder);
            string target = Path.Combine(_paths.SnapshotDirectory, id);

            Directory.CreateDirectory(target);

            try
            {
                foreach (string item in present)
                {
                    CopyItem(Path.Combine(dataDirectory, item), Path.Combine(target, item));
                }

                (long size, int files) = Measure(target);

                JObject metadata = new JObject
                {
                    ["id"] = id,
                    ["versionId"] = folder,
                    ["folderName"] = folder,
                    ["note"] = note ?? string.Empty,
                    ["createdAt"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                    ["sizeBytes"] = size,
                    ["fileCount"] = files,
                    ["items"] = new JArray(present)
                };

                File.WriteAllText(Path.Combine(target, MetadataFileName), metadata.ToString());

                return Task.FromResult(new SnapshotInfo(
                    id, folder, folder, note ?? string.Empty, DateTimeOffset.Now, size, files, present));
            }
            catch
            {
                // 半截快照没有意义，不能留在列表里
                TryDeleteDirectory(target);
                throw;
            }
        }

        public Task RestoreAsync(
            SnapshotInfo snapshot, string minecraftDirectory, CancellationToken cancellationToken)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            string source = Path.Combine(_paths.SnapshotDirectory, snapshot.Id);
            if (!Directory.Exists(source))
            {
                throw new InvalidOperationException("快照目录不存在：" + source);
            }

            string dataDirectory = ResolveDataDirectory(minecraftDirectory, snapshot.FolderName);

            // 回滚前先存一份当前状态：否则"回滚错了"就再也回不来
            try
            {
                CreateAsync(minecraftDirectory, snapshot.FolderName,
                    "回滚前自动备份（" + snapshot.Title + "）", cancellationToken).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // 当前目录可能是空的，这不影响回滚
            }

            Directory.CreateDirectory(dataDirectory);

            foreach (string item in snapshot.Items.Count > 0 ? snapshot.Items : Items)
            {
                string from = Path.Combine(source, item);
                if (!Exists(from))
                {
                    continue;
                }

                string to = Path.Combine(dataDirectory, item);
                DeleteIfExists(to);
                CopyItem(from, to);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string snapshotId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                throw new ArgumentException("快照 id 为空。", nameof(snapshotId));
            }

            string target = Path.Combine(_paths.SnapshotDirectory, snapshotId);
            TryDeleteDirectory(target);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 隔离实例的数据在 <c>versions\&lt;实例&gt;\</c>，非隔离在 <c>.minecraft</c> 根。
        /// 两边都看，哪边真的有东西就用哪边（隔离目录优先）。
        /// </summary>
        internal static string ResolveDataDirectory(string minecraftDirectory, string versionFolder)
        {
            if (!string.IsNullOrWhiteSpace(versionFolder))
            {
                string isolated = Path.Combine(minecraftDirectory, "versions", versionFolder!);
                if (Items.Any(item => Exists(Path.Combine(isolated, item))))
                {
                    return isolated;
                }
            }

            return minecraftDirectory;
        }

        private string BuildId(string folder)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string safe = SafeName(folder);
            string root = _paths.SnapshotDirectory;

            string id = stamp + "-" + safe;
            int suffix = 1;
            while (Directory.Exists(Path.Combine(root, id)))
            {
                id = stamp + "-" + safe + "-" + suffix;
                suffix++;
            }

            return id;
        }

        private SnapshotInfo? ReadMetadata(string directory)
        {
            string file = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(file))
            {
                return null;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(file));

                DateTimeOffset created = DateTimeOffset.TryParse(
                    root.Value<string>("createdAt"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed)
                    ? parsed
                    : new DateTimeOffset(Directory.GetCreationTime(directory));

                string[] items = root["items"] == null
                    ? Items
                    : root["items"]!.Select(token => token.Value<string>() ?? string.Empty)
                        .Where(text => text.Length > 0)
                        .ToArray();

                return new SnapshotInfo(
                    Path.GetFileName(directory),
                    root.Value<string>("versionId") ?? string.Empty,
                    root.Value<string>("folderName") ?? string.Empty,
                    root.Value<string>("note") ?? string.Empty,
                    created,
                    root.Value<long?>("sizeBytes") ?? 0,
                    root.Value<int?>("fileCount") ?? 0,
                    items);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

        private static void CopyItem(string source, string target)
        {
            if (Directory.Exists(source))
            {
                CopyDirectory(source, target);
                return;
            }

            string? parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent!);
            }

            File.Copy(source, target, overwrite: true);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static (long Size, int Files) Measure(string directory)
        {
            long size = 0;
            int files = 0;

            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    files++;
                }
                catch (IOException)
                {
                }
            }

            return (size, files);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

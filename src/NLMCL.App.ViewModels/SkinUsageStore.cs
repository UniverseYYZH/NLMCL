using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NLMCL.App.ViewModels
{
    /// <summary>本地皮肤的使用记录。只存路径/最后使用/累计秒数，不复制皮肤。</summary>
    internal sealed class SkinUsageRecord
    {
        public DateTimeOffset LastUsedAt { get; set; }

        public long TotalSeconds { get; set; }

        public int UseCount { get; set; }
    }

    /// <summary>
    /// 皮肤库的使用时长记录。用 TSV 保存，避免为了一个字段引入新的序列化依赖。
    /// 文件放在当前数据根目录，C 盘不可写时会自动跟随 AppPaths 回退。
    /// </summary>
    internal sealed class SkinUsageStore
    {
        private readonly Dictionary<string, SkinUsageRecord> _records;
        private readonly string? _file;
        private readonly object _gate = new object();

        public SkinUsageStore(string? dataRoot)
        {
            _records = new Dictionary<string, SkinUsageRecord>(StringComparer.OrdinalIgnoreCase);
            _file = string.IsNullOrWhiteSpace(dataRoot)
                ? null
                : Path.Combine(dataRoot, "skin-usage.tsv");
            Load();
        }

        public SkinUsageRecord? Get(string path)
        {
            string key = Normalize(path);

            lock (_gate)
            {
                return _records.TryGetValue(key, out SkinUsageRecord? record)
                    ? new SkinUsageRecord
                    {
                        LastUsedAt = record.LastUsedAt,
                        TotalSeconds = record.TotalSeconds,
                        UseCount = record.UseCount
                    }
                    : null;
            }
        }

        public void MarkUsed(string path, DateTimeOffset at)
        {
            string key = Normalize(path);

            lock (_gate)
            {
                if (!_records.TryGetValue(key, out SkinUsageRecord? record))
                {
                    record = new SkinUsageRecord();
                    _records[key] = record;
                }

                record.LastUsedAt = at;
                record.UseCount++;
                SaveLocked();
            }
        }

        public void AddDuration(string path, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            string key = Normalize(path);

            lock (_gate)
            {
                if (!_records.TryGetValue(key, out SkinUsageRecord? record))
                {
                    record = new SkinUsageRecord();
                    _records[key] = record;
                }

                record.TotalSeconds += Math.Max(1, (long)duration.TotalSeconds);
                SaveLocked();
            }
        }

        private void Load()
        {
            if (_file == null || !File.Exists(_file))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(_file, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');

                    if (parts.Length < 4
                        || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
                        || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                    {
                        continue;
                    }

                    string path;

                    try
                    {
                        path = Uri.UnescapeDataString(parts[0]);
                    }
                    catch (UriFormatException)
                    {
                        continue;
                    }

                    DateTimeOffset last;

                    if (!DateTimeOffset.TryParse(
                        parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last))
                    {
                        last = DateTimeOffset.MinValue;
                    }

                    _records[Normalize(path)] = new SkinUsageRecord
                    {
                        LastUsedAt = last,
                        TotalSeconds = Math.Max(0, seconds),
                        UseCount = Math.Max(0, count)
                    };
                }
            }
            catch (IOException)
            {
                // 使用记录只是辅助信息；读不到就从空记录开始。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void SaveLocked()
        {
            if (_file == null)
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(_file);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                StringBuilder text = new StringBuilder();

                foreach (KeyValuePair<string, SkinUsageRecord> pair in _records)
                {
                    text.Append(Uri.EscapeDataString(pair.Key)).Append('\t')
                        .Append(pair.Value.LastUsedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(pair.Value.TotalSeconds).Append('\t')
                        .Append(pair.Value.UseCount).Append('\n');
                }

                string temporary = _file + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));

                if (File.Exists(_file))
                {
                    try
                    {
                        File.Replace(temporary, _file, null);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporary, _file, true);
                        File.Delete(temporary);
                    }
                }
                else
                {
                    File.Move(temporary, _file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}

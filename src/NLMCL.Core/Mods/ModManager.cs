using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Mods
{
    /// <summary>
    /// 模组文件管理。
    ///
    /// 启用/禁用靠**改后缀**（`.jar` ↔ `.jar.disabled`），和 PCL 的做法一致：
    /// 加载器只认 `.jar`，所以禁用就等于不被加载，且不移动文件、随时可恢复。
    ///
    /// 元数据从 jar 里读：`fabric.mod.json` / `META-INF/mods.toml` / `mcmod.info`。
    /// </summary>
    public sealed class ModManager : IModManager
    {
        private const string DisabledSuffix = ".jar.disabled";

        public Task<IReadOnlyList<ModEntry>> ListAsync(
            string modsDirectory, CancellationToken cancellationToken)
        {
            List<ModEntry> entries = new List<ModEntry>();

            if (!Directory.Exists(modsDirectory))
            {
                return Task.FromResult<IReadOnlyList<ModEntry>>(entries);
            }

            foreach (string file in Directory.GetFiles(modsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string name = Path.GetFileName(file);

                bool enabled = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                bool disabled = name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

                if (!enabled && !disabled)
                {
                    continue;
                }

                ModEntry entry = new ModEntry(file, name, new FileInfo(file).Length)
                {
                    Enabled = enabled,
                    Name = Path.GetFileNameWithoutExtension(
                        disabled ? name.Substring(0, name.Length - ".disabled".Length) : name)
                };

                ReadMetadata(entry);
                entries.Add(entry);
            }

            return Task.FromResult<IReadOnlyList<ModEntry>>(
                entries.OrderByDescending(item => item.Enabled).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        public Task DisableAsync(ModEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (!entry.Enabled)
            {
                return Task.CompletedTask;
            }

            string target = entry.FilePath + ".disabled";

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(entry.FilePath, target);
            entry.FilePath = target;
            entry.FileName = Path.GetFileName(target);
            entry.Enabled = false;

            return Task.CompletedTask;
        }

        public Task EnableAsync(ModEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (entry.Enabled)
            {
                return Task.CompletedTask;
            }

            string target = entry.FilePath;

            if (!target.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                && target.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                target = target.Substring(0, target.Length - ".disabled".Length);
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(entry.FilePath, target);
            entry.FilePath = target;
            entry.FileName = Path.GetFileName(target);
            entry.Enabled = true;

            return Task.CompletedTask;
        }

        public Task DeleteAsync(ModEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (File.Exists(entry.FilePath))
            {
                File.Delete(entry.FilePath);
            }

            return Task.CompletedTask;
        }

        /// <summary>从 jar 里读名字 / 版本 / 加载器；读不到就保持文件名。</summary>
        internal static void ReadMetadata(ModEntry entry)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(entry.FilePath))
                {
                    if (TryReadJson(archive, "fabric.mod.json", entry, "fabric"))
                    {
                        return;
                    }

                    if (TryReadToml(archive, "META-INF/mods.toml", entry, "forge"))
                    {
                        return;
                    }

                    TryReadMcmodInfo(archive, entry);
                }
            }
            catch (Exception)
            {
                // 坏 jar / 非 jar：保持文件名，不影响列表
            }
        }

        private static bool TryReadJson(ZipArchive archive, string name, ModEntry entry, string loader)
        {
            ZipArchiveEntry? file = archive.Entries.FirstOrDefault(
                item => string.Equals(item.FullName, name, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                return false;
            }

            using (Stream stream = file.Open())
            using (StreamReader reader = new StreamReader(stream))
            {
                string text = reader.ReadToEnd();

                entry.Loader = loader;

                string? display = Match(text, "\"name\"\\s*:\\s*\"([^\"]+)\"")
                    ?? Match(text, "\"id\"\\s*:\\s*\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(display))
                {
                    entry.Name = display!;
                }

                string? version = Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(version))
                {
                    entry.Version = version!;
                }

                return true;
            }
        }

        private static bool TryReadToml(ZipArchive archive, string name, ModEntry entry, string loader)
        {
            ZipArchiveEntry? file = archive.Entries.FirstOrDefault(
                item => string.Equals(item.FullName, name, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                return false;
            }

            using (Stream stream = file.Open())
            using (StreamReader reader = new StreamReader(stream))
            {
                string text = reader.ReadToEnd();

                entry.Loader = loader;

                string? display = Match(text, "displayName\\s*=\\s*\"([^\"]+)\"")
                    ?? Match(text, "modId\\s*=\\s*\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(display))
                {
                    entry.Name = display!;
                }

                string? version = Match(text, "version\\s*=\\s*\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(version) && version!.IndexOf("${", StringComparison.Ordinal) < 0)
                {
                    entry.Version = version!;
                }

                return true;
            }
        }

        private static void TryReadMcmodInfo(ZipArchive archive, ModEntry entry)
        {
            ZipArchiveEntry? file = archive.Entries.FirstOrDefault(
                item => string.Equals(item.FullName, "mcmod.info", StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                return;
            }

            using (Stream stream = file.Open())
            using (StreamReader reader = new StreamReader(stream))
            {
                string text = reader.ReadToEnd();

                entry.Loader = "forge";

                string? display = Match(text, "\"name\"\\s*:\\s*\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(display))
                {
                    entry.Name = display!;
                }

                string? version = Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(version))
                {
                    entry.Version = version!;
                }
            }
        }

        private static string? Match(string text, string pattern)
        {
            Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

            // 第二遍：算每个启用模组缺哪些前置（禁用/缺失都不算已安装）
            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ModEntry item in entries)
            {
                if (item.Enabled && !string.IsNullOrWhiteSpace(item.ModId))
                {
                    installed.Add(item.ModId);
                }
            }

            foreach (ModEntry item in entries)
            {
                item.MissingDependencies.Clear();

                foreach (string dependency in ReadDeclaredDependencies(item))
                {
                    if (IsIgnorableDependency(dependency) || installed.Contains(dependency))
                    {
                        continue;
                    }

                    item.MissingDependencies.Add(dependency);
                }
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
        /// <summary>这些 id 由加载器/游戏本身提供，不该算"缺失前置"。</summary>
        private static readonly string[] IgnorableDependencies =
        {
            "minecraft", "java", "fabricloader", "fabric-loader", "forge", "neoforge", "quilt_loader"
        };

        private static bool IsIgnorableDependency(string id)
            => IgnorableDependencies.Contains(id, StringComparer.OrdinalIgnoreCase);

        /// <summary>读取模组声明的前置 id（目前覆盖 Fabric 的 depends）。</summary>
        internal static IReadOnlyList<string> ReadDeclaredDependencies(ModEntry entry)
        {
            List<string> result = new List<string>();

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(entry.FilePath))
                {
                    ZipArchiveEntry? file = archive.Entries.FirstOrDefault(
                        item => string.Equals(item.FullName, "fabric.mod.json", StringComparison.OrdinalIgnoreCase));

                    if (file == null)
                    {
                        return result;
                    }

                    using (Stream stream = file.Open())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        JObject root = JObject.Parse(reader.ReadToEnd());

                        if (root["depends"] is JObject depends)
                        {
                            foreach (JProperty property in depends.Properties())
                            {
                                result.Add(property.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 坏 jar 不影响列表，只是没有依赖信息
            }

            return result;
        }

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

                string? id = Match(text, "\"id\"\\s*:\\s*\"([^\"]+)\"");

            if (!string.IsNullOrWhiteSpace(id))
            {
                entry.ModId = id!;
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

                string? id = Match(text, "\"id\"\\s*:\\s*\"([^\"]+)\"");

            if (!string.IsNullOrWhiteSpace(id))
            {
                entry.ModId = id!;
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

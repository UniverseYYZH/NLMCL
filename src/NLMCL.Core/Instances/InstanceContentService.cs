using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Instances
{
    /// <summary>
    /// 实例内容管理：存档按 zip 解压为世界目录，资源包/光影包直接复制 zip。
    /// 只写当前 .minecraft 下的 saves/resourcepacks/shaderpacks。
    /// </summary>
    public sealed class InstanceContentService : IInstanceContentService
    {
        public string GetDirectory(string gameDirectory, InstanceContentKind kind)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                throw new ArgumentException("游戏目录为空。", nameof(gameDirectory));
            }

            return Path.Combine(gameDirectory, FolderName(kind));
        }

        public int Count(string gameDirectory, InstanceContentKind kind)
        {
            string directory = GetDirectory(gameDirectory, kind);

            if (!Directory.Exists(directory))
            {
                return 0;
            }

            if (kind == InstanceContentKind.Saves)
            {
                return Directory.EnumerateDirectories(directory)
                    .Count(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal));
            }

            return Directory.EnumerateFileSystemEntries(directory)
                .Count(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal));
        }

        public Task<string> ImportAsync(
            string gameDirectory,
            InstanceContentKind kind,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("找不到要导入的文件。", sourcePath);
            }

            if (!string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("只支持导入 .zip 文件。");
            }

            string directory = GetDirectory(gameDirectory, kind);
            Directory.CreateDirectory(directory);

            return Task.Run(
                () => kind == InstanceContentKind.Saves
                    ? ImportSave(sourcePath, directory, cancellationToken)
                    : ImportPack(sourcePath, directory, cancellationToken),
                cancellationToken);
        }

        private static string ImportSave(string sourcePath, string savesDirectory, CancellationToken cancellationToken)
        {
            string baseName = SafeName(Path.GetFileNameWithoutExtension(sourcePath));
            string target = UniqueDirectory(savesDirectory, baseName);
            string temporary = Path.Combine(savesDirectory, ".import-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(temporary);

                using (ZipArchive archive = ZipFile.OpenRead(sourcePath))
                {
                    List<ZipArchiveEntry> entries = archive.Entries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.FullName))
                        .Where(entry => !entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                        .Where(entry => !string.Equals(entry.FullName, ".DS_Store", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    string? rootPrefix = SharedRootPrefix(entries);

                    foreach (ZipArchiveEntry entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string relative = NormalizeEntryName(entry.FullName);

                        // 先按原始相对路径校验，避免 "../x" 被 SharedRootPrefix 当成顶层目录剥掉。
                        SafeDestination(temporary, relative);

                        if (rootPrefix != null)
                        {
                            if (relative.Length == rootPrefix.Length)
                            {
                                continue;
                            }

                            relative = relative.Substring(rootPrefix.Length + 1);
                        }

                        if (relative.Length == 0)
                        {
                            continue;
                        }

                        string destination = SafeDestination(temporary, relative);

                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                            || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(destination);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                        using (Stream input = entry.Open())
                        using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                        {
                            input.CopyTo(output);
                        }
                    }
                }

                Directory.Move(temporary, target);
                return target;
            }
            catch
            {
                TryDeleteDirectory(temporary);
                TryDeleteDirectory(target);
                throw;
            }
        }

        private static string ImportPack(string sourcePath, string directory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(sourcePath);
            string stem = SafeName(Path.GetFileNameWithoutExtension(fileName));
            string extension = Path.GetExtension(fileName);
            string target = UniqueFile(directory, stem, extension);
            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    input.CopyTo(output);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, target);
                return target;
            }
            catch
            {
                TryDeleteFile(temporary);
                throw;
            }
        }

        /// <summary>如果 zip 里所有文件都在同一个顶层目录下，返回该目录名，导入时去掉这一层。</summary>
        private static string? SharedRootPrefix(IReadOnlyList<ZipArchiveEntry> entries)
        {
            string? root = null;

            foreach (ZipArchiveEntry entry in entries)
            {
                string name = NormalizeEntryName(entry.FullName);

                if (name.Length == 0)
                {
                    continue;
                }

                int slash = name.IndexOf('/');

                if (slash < 0)
                {
                    return null;
                }

                string top = name.Substring(0, slash);

                if (root == null)
                {
                    root = top;
                }
                else if (!string.Equals(root, top, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return root;
        }

        private static string NormalizeEntryName(string name) =>
            name.Replace('\\', '/').TrimStart('/');

        private static string SafeDestination(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("存档包含越界路径：" + relative);
            }

            return destination;
        }

        private static string SafeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "导入内容" : safe;
        }

        private static string UniqueDirectory(string directory, string baseName)
        {
            string candidate = Path.Combine(directory, baseName);
            int suffix = 2;

            while (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidate = Path.Combine(directory, baseName + " (" + suffix + ")");
                suffix++;
            }

            return candidate;
        }

        private static string UniqueFile(string directory, string stem, string extension)
        {
            string candidate = Path.Combine(directory, stem + extension);
            int suffix = 2;

            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = Path.Combine(directory, stem + " (" + suffix + ")" + extension);
                suffix++;
            }

            return candidate;
        }

        private static string FolderName(InstanceContentKind kind)
        {
            switch (kind)
            {
                case InstanceContentKind.Saves:
                    return "saves";
                case InstanceContentKind.ResourcePacks:
                    return "resourcepacks";
                case InstanceContentKind.ShaderPacks:
                    return "shaderpacks";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
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

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
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

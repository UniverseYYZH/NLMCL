using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Instances
{
    /// <summary>
    /// 实例级 zip 包。包内保留实例目录结构和版本 JSON，导入时会重建目录并同步 JSON 的 id。
    /// 不打包 logs / crash-reports，避免把无关日志和隐私内容带给别人。
    /// </summary>
    public sealed class InstancePackageService : IInstancePackageService
    {
        private const string ManifestName = "nlmcl-package.json";
        private const int FormatVersion = 1;

        public Task<string> ExportAsync(string instancePath, string destinationZip, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Export(instancePath, destinationZip, cancellationToken), cancellationToken);
        }

        public Task<string> ImportAsync(string packageZip, string versionsDirectory, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Import(packageZip, versionsDirectory, cancellationToken), cancellationToken);
        }

        public Task RestoreAsync(string packageZip, string instanceDirectory, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Restore(packageZip, instanceDirectory, cancellationToken), cancellationToken);
        }

        private static string Export(string instancePath, string destinationZip, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath))
            {
                throw new DirectoryNotFoundException("实例目录不存在：" + instancePath);
            }

            string source = Path.GetFullPath(instancePath);
            string destination = Path.GetFullPath(destinationZip);
            string? parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new ArgumentException("导出路径无效：" + destinationZip);
            }

            Directory.CreateDirectory(parent);
            string temp = Path.Combine(parent, "." + Path.GetFileName(destination) + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string folderName = new DirectoryInfo(source).Name;
            var manifest = new PackageManifest
            {
                Format = FormatVersion,
                Name = folderName,
                SourceFolder = folderName,
                ExportedAt = DateTimeOffset.UtcNow
            };

            try
            {
                using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));
                    }

                    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string full = Path.GetFullPath(file);
                        if (string.Equals(full, destination, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(full, temp, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string relative = full.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                        if (IsExcluded(relative))
                        {
                            continue;
                        }

                        string entryName = relative.Replace(Path.DirectorySeparatorChar, '/');
                        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        using (Stream input = File.OpenRead(full))
                        using (Stream output = entry.Open())
                        {
                            input.CopyTo(output);
                        }
                    }
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(temp, destination);
                return destination;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static string Import(string packageZip, string versionsDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageZip) || !File.Exists(packageZip))
            {
                throw new FileNotFoundException("实例包不存在：" + packageZip);
            }

            string versions = Path.GetFullPath(versionsDirectory);
            Directory.CreateDirectory(versions);

            using (var archive = ZipFile.OpenRead(packageZip))
            {
                PackageManifest manifest = ReadManifest(archive, packageZip);
                string sourceFolder = SanitizeName(string.IsNullOrWhiteSpace(manifest.SourceFolder) ? manifest.Name : manifest.SourceFolder);
                string targetName = SanitizeName(string.IsNullOrWhiteSpace(manifest.Name) ? sourceFolder : manifest.Name);
                string target = MakeUniqueTarget(versions, targetName);
                string targetFull = Path.GetFullPath(target);

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.Equals(entry.FullName, ManifestName, StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(targetFull, relative));
                    if (!IsInside(full, targetFull))
                    {
                        throw new InvalidDataException("实例包包含非法路径：" + entry.FullName);
                    }

                    string? directory = Path.GetDirectoryName(full);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    entry.ExtractToFile(full, overwrite: true);
                }

                RenameVersionJson(targetFull, sourceFolder, Path.GetFileName(targetFull));
                return targetFull;
            }
        }

        private static void Restore(string packageZip, string instanceDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageZip) || !File.Exists(packageZip))
            {
                throw new FileNotFoundException("实例备份不存在：" + packageZip);
            }

            if (string.IsNullOrWhiteSpace(instanceDirectory) || !Directory.Exists(instanceDirectory))
            {
                throw new DirectoryNotFoundException("实例目录不存在：" + instanceDirectory);
            }

            string target = Path.GetFullPath(instanceDirectory);
            string staging = target + ".nlmcl-restore-" + Guid.NewGuid().ToString("N");
            string old = target + ".nlmcl-rollback-" + Guid.NewGuid().ToString("N");

            try
            {
                using (var archive = ZipFile.OpenRead(packageZip))
                {
                    PackageManifest manifest = ReadManifest(archive, packageZip);
                    Directory.CreateDirectory(staging);

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.Equals(entry.FullName, ManifestName, StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrEmpty(entry.Name))
                        {
                            continue;
                        }

                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar)
                            .Replace('\\', Path.DirectorySeparatorChar);
                        string full = Path.GetFullPath(Path.Combine(staging, relative));
                        if (!IsInside(full, staging))
                        {
                            throw new InvalidDataException("实例备份包含非法路径：" + entry.FullName);
                        }

                        string? directory = Path.GetDirectoryName(full);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        entry.ExtractToFile(full, overwrite: true);
                    }

                    string sourceFolder = SanitizeName(
                        string.IsNullOrWhiteSpace(manifest.SourceFolder) ? manifest.Name : manifest.SourceFolder);
                    RenameVersionJson(staging, sourceFolder, Path.GetFileName(target));
                }

                Directory.Move(target, old);
                try
                {
                    Directory.Move(staging, target);
                }
                catch
                {
                    Directory.Move(old, target);
                    throw;
                }

                TryDeleteDirectory(old);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        private static PackageManifest ReadManifest(ZipArchive archive, string packageZip)
        {
            ZipArchiveEntry? entry = archive.Entries
                .FirstOrDefault(item => string.Equals(item.FullName, ManifestName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                string name = Path.GetFileNameWithoutExtension(packageZip);
                return new PackageManifest { Format = FormatVersion, Name = name, SourceFolder = name };
            }

            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                PackageManifest? manifest = JsonConvert.DeserializeObject<PackageManifest>(reader.ReadToEnd());
                if (manifest == null || manifest.Format > FormatVersion)
                {
                    throw new InvalidDataException("不支持的实例包版本。");
                }

                return manifest;
            }
        }

        private static void RenameVersionJson(string target, string sourceFolder, string targetName)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                return;
            }

            string oldJson = Path.Combine(target, sourceFolder + ".json");
            if (!File.Exists(oldJson))
            {
                return;
            }

            JObject root = JObject.Parse(File.ReadAllText(oldJson));
            root["id"] = targetName;
            string newJson = Path.Combine(target, targetName + ".json");
            File.WriteAllText(newJson, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(oldJson);
            }
        }

        private static string MakeUniqueTarget(string versionsDirectory, string name)
        {
            string candidate = Path.Combine(versionsDirectory, name);
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            for (int i = 2; i < 1000; i++)
            {
                candidate = Path.Combine(versionsDirectory, name + "-imported" + (i == 2 ? string.Empty : "-" + i));
                if (!Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("无法为导入实例生成唯一目录名。");
        }

        private static string SanitizeName(string? name)
        {
            string value = (name ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return "imported-instance";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                builder.Append(invalid.Contains(c) ? '_' : c);
            }

            string result = builder.ToString().Trim().TrimEnd('.');
            return result.Length == 0 ? "imported-instance" : result;
        }

        private static bool IsInside(string full, string root)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcluded(string relative)
        {
            string first = relative.Split(Path.DirectorySeparatorChar, '/')[0];
            return string.Equals(first, "logs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(first, "crash-reports", StringComparison.OrdinalIgnoreCase);
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
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

        private sealed class PackageManifest
        {
            public int Format { get; set; }
            public string Name { get; set; } = string.Empty;
            public string SourceFolder { get; set; } = string.Empty;
            public DateTimeOffset ExportedAt { get; set; }
        }
    }
}

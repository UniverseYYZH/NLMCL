using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Versions
{
    /// <summary>
    /// 实例管理：重命名 / 复制 / 删除。
    ///
    /// 一个实例 = <c>versions/&lt;名字&gt;/</c> + 同名 <c>.json</c>。
    /// 改名时必须同时改 JSON 里的 <c>id</c>，否则 PCL 扫描出来的名字会和目录不一致。
    /// </summary>
    public sealed class VersionManager : IVersionManager
    {
        private static readonly char[] Forbidden = Path.GetInvalidFileNameChars();

        public Task RenameAsync(
            string minecraftDirectory, string folder, string newName, CancellationToken cancellationToken)
        {
            Validate(minecraftDirectory, folder, newName, ignoreFolder: folder);

            string versions = Path.Combine(minecraftDirectory, "versions");
            string source = Path.Combine(versions, folder);
            string target = Path.Combine(versions, newName);

            Directory.Move(source, target);
            RenameJson(target, folder, newName);

            return Task.CompletedTask;
        }

        public Task<string> CopyAsync(
            string minecraftDirectory, string folder, string newName, CancellationToken cancellationToken)
        {
            Validate(minecraftDirectory, folder, newName, ignoreFolder: folder);

            string versions = Path.Combine(minecraftDirectory, "versions");
            string source = Path.Combine(versions, folder);
            string target = Path.Combine(versions, newName);

            CopyDirectory(source, target);
            RenameJson(target, folder, newName);

            return Task.FromResult(newName);
        }

        public Task DeleteAsync(string minecraftDirectory, string folder, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("实例名不能为空。", nameof(folder));
            }

            string target = Path.Combine(minecraftDirectory, "versions", folder);

            if (!Directory.Exists(target))
            {
                throw new DirectoryNotFoundException("实例不存在：" + folder);
            }

            // 防呆：确认真的在 versions 下（而不是被传了个相对路径）
            string full = Path.GetFullPath(target);
            string root = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions"));

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("拒绝删除 versions 目录之外的路径：" + full);
            }

            Directory.Delete(target, recursive: true);

            return Task.CompletedTask;
        }

        public bool IsNameAvailable(string minecraftDirectory, string name, string? ignoreFolder = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string trimmed = name.Trim();

            if (trimmed.IndexOfAny(Forbidden) >= 0 || trimmed.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                return false;
            }

            if (trimmed == "." || trimmed == "..")
            {
                return false;
            }

            if (ignoreFolder != null && string.Equals(trimmed, ignoreFolder, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string versions = Path.Combine(minecraftDirectory, "versions");

            if (!Directory.Exists(versions))
            {
                return true;
            }

            return !Directory.EnumerateDirectories(versions)
                .Any(path => string.Equals(Path.GetFileName(path), trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private static void Validate(
            string minecraftDirectory, string folder, string newName, string? ignoreFolder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("旧实例名不能为空。", nameof(folder));
            }

            string trimmed = (newName ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                throw new ArgumentException("新名字不能为空。", nameof(newName));
            }

            if (trimmed.IndexOfAny(Forbidden) >= 0)
            {
                throw new ArgumentException("新名字包含非法字符。", nameof(newName));
            }

            string target = Path.Combine(minecraftDirectory, "versions", trimmed);

            if (Directory.Exists(target))
            {
                throw new IOException("已经有同名实例：" + trimmed);
            }

            string source = Path.Combine(minecraftDirectory, "versions", folder);

            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("实例不存在：" + folder);
            }
        }

        /// <summary>把目录里的 <c>&lt;old&gt;.json</c> 改名，并把 id 改成新名字。</summary>
        private static void RenameJson(string instanceDirectory, string oldName, string newName)
        {
            string oldJson = Path.Combine(instanceDirectory, oldName + ".json");
            string newJson = Path.Combine(instanceDirectory, newName + ".json");

            if (!File.Exists(oldJson))
            {
                return;
            }

            JObject json;

            try
            {
                json = JObject.Parse(File.ReadAllText(oldJson));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("版本 JSON 无法解析，已中止改名：" + oldJson, ex);
            }

            json["id"] = newName;

            File.WriteAllText(newJson, json.ToString(), new UTF8Encoding(false));

            if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(oldJson);
            }
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
    }
}

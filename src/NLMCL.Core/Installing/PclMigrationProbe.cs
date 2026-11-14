using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLMCL.Core.Runtime;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 只读探测一个 PCL 的 .minecraft。不会复制、移动或删除文件。
    /// </summary>
    public sealed class PclMigrationProbe
    {
        private readonly Func<string, bool> _canWrite;

        public PclMigrationProbe(Func<string, bool>? canWrite = null)
        {
            _canWrite = canWrite ?? EnvironmentProbe.CanWriteDirectory;
        }

        public string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");

        public bool CanWrite(string? directory)
        {
            return _canWrite(directory ?? string.Empty);
        }

        public PclDirectoryInfo Inspect(string? path)
        {
            string normalized = Normalize(path);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return PclDirectoryInfo.Invalid("请选择 PCL 的 .minecraft 目录。");
            }

            try
            {
                if (!Directory.Exists(normalized))
                {
                    return PclDirectoryInfo.Invalid("目录不存在：" + normalized);
                }

                string versionsDirectory = Path.Combine(normalized, "versions");
                if (!Directory.Exists(versionsDirectory))
                {
                    return PclDirectoryInfo.Invalid(
                        "未找到 versions 文件夹，这可能不是 PCL 的 .minecraft 目录。");
                }

                int versionCount = Directory.GetDirectories(versionsDirectory).Length;
                bool hasMods = HasEntries(Path.Combine(normalized, "mods"));
                bool hasSaves = HasEntries(Path.Combine(normalized, "saves"));
                bool canWrite = CanWrite(normalized);

                return new PclDirectoryInfo(
                    normalized,
                    versionCount,
                    hasMods,
                    hasSaves,
                    canWrite,
                    string.Empty);
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is ArgumentException
                || ex is NotSupportedException)
            {
                return PclDirectoryInfo.Invalid("读取目录失败：" + ex.Message);
            }
        }

        private static bool HasEntries(string directory)
        {
            try
            {
                return Directory.Exists(directory)
                    && Directory.EnumerateFileSystemEntries(directory).Any();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string full = Path.GetFullPath(path!);
                string root = Path.GetPathRoot(full) ?? string.Empty;
                return full.Length > root.Length
                    ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : full;
            }
            catch (Exception ex) when (ex is ArgumentException
                || ex is NotSupportedException
                || ex is PathTooLongException)
            {
                return path!.Trim();
            }
        }
    }

    public sealed class PclDirectoryInfo
    {
        internal PclDirectoryInfo(
            string path,
            int versionCount,
            bool hasMods,
            bool hasSaves,
            bool canWrite,
            string error)
        {
            Path = path;
            VersionCount = versionCount;
            HasMods = hasMods;
            HasSaves = hasSaves;
            CanWrite = canWrite;
            Error = error;
        }

        public string Path { get; }

        public int VersionCount { get; }

        public bool HasMods { get; }

        public bool HasSaves { get; }

        public bool CanWrite { get; }

        public string Error { get; }

        public bool IsValid => string.IsNullOrEmpty(Error);

        internal static PclDirectoryInfo Invalid(string error)
        {
            return new PclDirectoryInfo(string.Empty, 0, false, false, false, error);
        }
    }
}

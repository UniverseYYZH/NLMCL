using System;
using System.Collections.Generic;
using System.IO;
using NLMCL.Abstractions;

namespace NLMCL.Core
{
    /// <summary>
    /// <see cref="IAppPaths"/> 的默认实现，只依赖 .NET Framework 基础类库。
    /// 普通模式优先使用 %APPDATA%\NLMCL；如果该位置不可写，回退到 exe 目录下的
    /// NLMCL-data、%LOCALAPPDATA%\NLMCL 或 %TEMP%\NLMCL，避免受限进程把整个界面卡死。
    /// 便携模式仍使用 exe\data。
    /// </summary>
    public sealed class AppPaths : IAppPaths
    {
        private const string AppFolderName = "NLMCL";
        private const string FallbackFolderName = "NLMCL-data";

        /// <summary>放在 NLMCL.exe 同目录即可启用便携模式。</summary>
        public const string PortableFlagName = "portable.flag";

        /// <summary>回退数据目录里的标记文件，用于后续启动复用同一份数据。</summary>
        public const string FallbackMarkerName = ".fallback";

        public AppPaths()
        {
            bool portable = DefaultPortable();
            IsPortable = portable;

            if (portable)
            {
                RoamingRoot = AppContext.BaseDirectory;
                DataRoot = Path.Combine(RoamingRoot, "data");
                IsDataRootFallback = false;
                return;
            }

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            RoamingRoot = roaming;

            string preferred = Path.Combine(roaming, AppFolderName);
            string fallback = Path.Combine(AppContext.BaseDirectory, FallbackFolderName);
            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
            string temp = Path.Combine(Path.GetTempPath(), AppFolderName);

            DataRoot = SelectDataRoot(preferred, fallback, local, temp, out bool isFallback);
            IsDataRootFallback = isFallback;
        }

        private static bool DefaultPortable()
        {
            return HasPortableFlag(AppContext.BaseDirectory);
        }

        /// <summary>测试用：指定 Roaming AppData 根目录，普通模式。</summary>
        public AppPaths(string roamingAppDataDirectory)
            : this(roamingAppDataDirectory, false)
        {
        }

        /// <summary>测试/显式指定便携模式。</summary>
        public AppPaths(string roamingAppDataDirectory, bool portable)
        {
            if (string.IsNullOrWhiteSpace(roamingAppDataDirectory))
            {
                throw new ArgumentException("Roaming AppData 路径不能为空。", nameof(roamingAppDataDirectory));
            }

            IsPortable = portable;
            RoamingRoot = roamingAppDataDirectory;
            DataRoot = portable
                ? Path.Combine(roamingAppDataDirectory, "data")
                : Path.Combine(roamingAppDataDirectory, AppFolderName);
            IsDataRootFallback = false;
        }

        public bool IsPortable { get; }

        public bool IsDataRootFallback { get; }

        public string RoamingRoot { get; }

        public string DataRoot { get; }

        public string SettingsFile => Path.Combine(DataRoot, "settings.json");

        public string AccountsFile => Path.Combine(DataRoot, "accounts.json");

        public string InstancesDirectory => Path.Combine(DataRoot, "instances");

        public string SkinDirectory => Path.Combine(DataRoot, "skins");

        public string SnapshotDirectory => Path.Combine(DataRoot, "snapshots");

        public string DefaultMinecraftDirectory => Path.Combine(RoamingRoot, ".minecraft");

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(InstancesDirectory);
            Directory.CreateDirectory(SkinDirectory);
            Directory.CreateDirectory(SnapshotDirectory);
        }

        /// <summary>
        /// 选择实际数据根目录。优先使用已有的回退标记，其次使用可写的 AppData；
        /// AppData 不可写时依次尝试 exe 目录、LocalAppData、Temp。
        /// 标记存在时不再探测 AppData，避免同一次安装的数据在多个目录之间分裂。
        /// </summary>
        internal static string SelectDataRoot(
            string preferred,
            string fallback,
            string local,
            string temp,
            out bool isFallback)
        {
            isFallback = false;
            if (string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            string[] candidates = { fallback, local, temp };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || PathsEqual(candidate, preferred))
                {
                    continue;
                }

                if (HasFallbackMarker(candidate) && IsWritable(candidate))
                {
                    isFallback = true;
                    return candidate;
                }
            }

            if (IsWritable(preferred))
            {
                return preferred;
            }

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || PathsEqual(candidate, preferred))
                {
                    continue;
                }

                if (!IsWritable(candidate))
                {
                    continue;
                }

                TryMigrateLegacyData(preferred, candidate);
                TryWriteFallbackMarker(candidate);
                isFallback = true;
                return candidate;
            }

            return preferred;
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsWritable(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
                using (FileStream stream = new FileStream(
                    probe,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose))
                {
                    stream.WriteByte(0);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasFallbackMarker(string directory)
        {
            try
            {
                return File.Exists(Path.Combine(directory, FallbackMarkerName));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryWriteFallbackMarker(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, FallbackMarkerName),
                    "Created " + DateTimeOffset.Now.ToString("o") + Environment.NewLine);
            }
            catch (Exception)
            {
                // 标记写不了也不影响本次运行；下次会重新探测。
            }
        }

        /// <summary>
        /// 第一次回退时尽量把旧 AppData 里的账号、设置、皮肤和实例配置复制过去。
        /// 只补缺失文件，绝不覆盖回退目录里已有的内容；缓存和日志不复制。
        /// </summary>
        private static void TryMigrateLegacyData(string sourceRoot, string targetRoot)
        {
            if (!Directory.Exists(sourceRoot))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file);
                    if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                        || name.IndexOf(".tmp-", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(name, "startup.log", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, FallbackMarkerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    CopyFileIfMissing(file, Path.Combine(targetRoot, name));
                }
            }
            catch (Exception)
            {
            }

            CopyDirectoryIfMissing(
                Path.Combine(sourceRoot, "skins"),
                Path.Combine(targetRoot, "skins"),
                skipCache: true);
            CopyDirectoryIfMissing(
                Path.Combine(sourceRoot, "instances"),
                Path.Combine(targetRoot, "instances"),
                skipCache: false);
            CopyDirectoryIfMissing(
                Path.Combine(sourceRoot, "snapshots"),
                Path.Combine(targetRoot, "snapshots"),
                skipCache: false);
            CopyDirectoryIfMissing(
                Path.Combine(sourceRoot, "authlib-injector"),
                Path.Combine(targetRoot, "authlib-injector"),
                skipCache: false);
        }

        private static void CopyDirectoryIfMissing(string source, string target, bool skipCache)
        {
            if (!Directory.Exists(source))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(target);
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
                {
                    CopyFileIfMissing(file, Path.Combine(target, Path.GetFileName(file)));
                }

                foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(directory);
                    if (skipCache && string.Equals(name, "cache", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    CopyDirectoryIfMissing(directory, Path.Combine(target, name), skipCache: false);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void CopyFileIfMissing(string source, string target)
        {
            try
            {
                if (File.Exists(target))
                {
                    return;
                }

                string? folder = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder!);
                }

                File.Copy(source, target, overwrite: false);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>检查指定目录是否有便携标记。</summary>
        public static bool HasPortableFlag(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            return File.Exists(Path.Combine(directory, PortableFlagName));
        }
    }
}

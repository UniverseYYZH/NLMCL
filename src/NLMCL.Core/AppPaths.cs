using System;
using System.IO;
using NLMCL.Abstractions;

namespace NLMCL.Core
{
    /// <summary>
    /// <see cref="IAppPaths"/> 的默认实现，只依赖 .NET Framework 基础类库。
    /// 普通模式使用 %APPDATA%\NLMCL；便携模式使用 exe\data。
    /// </summary>
    public sealed class AppPaths : IAppPaths
    {
        private const string AppFolderName = "NLMCL";

        /// <summary>放在 NLMCL.exe 同目录即可启用便携模式。</summary>
        public const string PortableFlagName = "portable.flag";

        public AppPaths()
            : this(DefaultRoot(), DefaultPortable())
        {
        }

        private static string DefaultRoot()
        {
            return DefaultPortable()
                ? AppContext.BaseDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
        }

        public bool IsPortable { get; }

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

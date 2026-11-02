using System;
using System.IO;
using NLMCL.Abstractions;

namespace NLMCL.Core
{
    /// <summary>
    /// <see cref="IAppPaths"/> 的默认实现，只依赖 .NET Framework 基础类库。
    /// </summary>
    public sealed class AppPaths : IAppPaths
    {
        private const string AppFolderName = "NLMCL";

        public AppPaths()
            : this(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
        {
        }

        /// <summary>测试与便携模式用：把根目录指到别处。</summary>
        public AppPaths(string roamingAppDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(roamingAppDataDirectory))
            {
                throw new ArgumentException("Roaming AppData 路径不能为空。", nameof(roamingAppDataDirectory));
            }

            RoamingRoot = roamingAppDataDirectory;
        }

        public string RoamingRoot { get; }

        public string DataRoot => Path.Combine(RoamingRoot, AppFolderName);

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
    }
}

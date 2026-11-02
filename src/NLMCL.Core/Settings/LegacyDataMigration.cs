using System;
using System.IO;

namespace NLMCL.Core.Settings
{
    /// <summary>
    /// 改名迁移：老版本（NLCL）的数据在 <c>%APPDATA%\NLCL</c>，
    /// 新名字第一次启动时把它整体复制到 <c>%APPDATA%\NLMCL</c>。
    ///
    /// 只在新目录**还不存在**时执行——否则会把用户新数据覆盖掉。
    /// 迁移是尽力而为：失败不抛异常，最坏情况就是重新配置一次。
    /// </summary>
    public static class LegacyDataMigration
    {
        public const string LegacyFolderName = "NLCL";

        /// <summary>返回是否真的发生了迁移。</summary>
        public static bool TryMigrate(string dataRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dataRoot) || Directory.Exists(dataRoot))
                {
                    return false;
                }

                string trimmed = dataRoot.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string? parent = Path.GetDirectoryName(trimmed);
                if (string.IsNullOrEmpty(parent))
                {
                    return false;
                }

                string legacy = Path.Combine(parent!, LegacyFolderName);
                if (!Directory.Exists(legacy))
                {
                    return false;
                }

                CopyDirectory(legacy, dataRoot);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }
    }
}

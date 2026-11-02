using System;
using System.IO;

namespace NLMCL.Core.Settings
{
    /// <summary>
    /// 改名迁移：老版本（NLMCL 之前的名字 NLCL）的数据在 <c>%APPDATA%\NLCL</c>，
    /// 新名字启动时把**缺失**的文件/目录补齐到 <c>%APPDATA%\NLMCL</c>。
    ///
    /// 逐项合并而不是"整个目录搬一次"：新目录可能已经被写过（比如测试或先跑过新版本的
    /// pcl_settings.json），若只在"新目录不存在"时迁移，老配置就永远搬不过来了。
    /// 已存在的文件一律不覆盖。
    /// </summary>
    public static class LegacyDataMigration
    {
        public const string LegacyFolderName = "NLCL";

        /// <summary>返回是否真的复制了东西。</summary>
        public static bool TryMigrate(string dataRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dataRoot))
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

                return CopyMissing(legacy, dataRoot);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>只复制目标里不存在的文件/目录；返回是否有任何复制。</summary>
        private static bool CopyMissing(string source, string target)
        {
            bool copied = false;

            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source))
            {
                string destination = Path.Combine(target, Path.GetFileName(file));
                if (File.Exists(destination))
                {
                    continue;
                }

                File.Copy(file, destination, overwrite: false);
                copied = true;
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                if (CopyMissing(directory, Path.Combine(target, Path.GetFileName(directory))))
                {
                    copied = true;
                }
            }

            return copied;
        }
    }
}

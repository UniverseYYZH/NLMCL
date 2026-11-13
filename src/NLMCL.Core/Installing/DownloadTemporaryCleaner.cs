using System;
using System.IO;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 清理一次安装留下的分块/半截文件。
    ///
    /// 暂停时必须保留这些文件用于续传；只有用户明确取消或忽略任务时才调用这里。
    /// 队列保证同一时间只有一个安装任务，因此可以安全地扫描整个游戏目录。
    /// </summary>
    public static class DownloadTemporaryCleaner
    {
        /// <summary>返回删除的文件/目录数量。目录不存在时返回 0。</summary>
        public static int Clean(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return 0;
            }

            int removed = 0;

            try
            {
                foreach (string parts in Directory.EnumerateDirectories(
                    root, "*.parts", SearchOption.AllDirectories))
                {
                    if (TryDeleteDirectory(parts))
                    {
                        removed++;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            try
            {
                foreach (string temp in Directory.EnumerateFiles(
                    root, "*.nlmcl-tmp", SearchOption.AllDirectories))
                {
                    if (TryDeleteFile(temp))
                    {
                        removed++;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return removed;
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return false;
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return false;
        }
    }
}

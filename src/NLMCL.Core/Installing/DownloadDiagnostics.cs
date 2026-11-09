using System;
using System.IO;
using System.Text;

namespace NLMCL.Core.Installing
{
    /// <summary>轻量下载诊断：只写临时目录，失败不影响安装；超过 2MB 轮转，保留 3 个备份。</summary>
    internal static class DownloadDiagnostics
    {
        private const long MaxBytes = 2L * 1024 * 1024;
        private const int MaxBackups = 3;
        private static readonly object Gate = new object();

        internal static string LogPath =>
            Path.Combine(Path.GetTempPath(), "nlmcl-download.log");

        public static void Write(string message)
            => WriteTo(LogPath, message);

        internal static void WriteTo(string path, string message)
        {
            try
            {
                lock (Gate)
                {
                    RotateIfNeeded(path);
                    File.AppendAllText(
                        path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes)
                {
                    return;
                }

                for (int index = MaxBackups - 1; index >= 1; index--)
                {
                    string from = path + "." + index;
                    string to = path + "." + (index + 1);

                    if (!File.Exists(from))
                    {
                        continue;
                    }

                    if (File.Exists(to))
                    {
                        File.Delete(to);
                    }

                    File.Move(from, to);
                }

                string first = path + ".1";

                if (File.Exists(first))
                {
                    File.Delete(first);
                }

                File.Move(path, first);
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

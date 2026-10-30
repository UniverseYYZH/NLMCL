using System;
using System.IO;
using System.Text;
using System.Windows;

namespace NLCL.App.Services
{
    /// <summary>
    /// 未处理异常的兜底：写一份可读的日志，并把路径告诉用户。
    ///
    /// 之前没有任何全局处理——后台线程上任何一次异常都会让 .NET Framework 直接结束进程，
    /// 用户只看到"启动器自己消失了"，连排查入口都没有。
    /// </summary>
    internal static class CrashReporter
    {
        internal static string Write(string source, Exception? exception)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NLCL",
                    "logs");
                Directory.CreateDirectory(folder);

                string path = Path.Combine(
                    folder,
                    "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

                StringBuilder builder = new StringBuilder();
                builder.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine("来源: " + source);
                builder.AppendLine("线程: " + System.Threading.Thread.CurrentThread.ManagedThreadId);
                builder.AppendLine();
                builder.AppendLine(exception == null ? "(没有异常对象)" : exception.ToString());

                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception)
            {
                // 连日志都写不了就只弹框。
                return string.Empty;
            }
        }

        internal static void ShowFatal(string source, Exception? exception, bool canContinue)
        {
            string path = Write(source, exception);

            string message = source + "：\n"
                + (exception == null ? "未知错误" : exception.GetType().Name + ": " + exception.Message)
                + "\n\n"
                + (canContinue
                    ? "本次异常已被拦下，程序仍在运行，但建议尽快保存工作并重启。"
                    : "程序无法继续运行。")
                + (string.IsNullOrEmpty(path) ? string.Empty : "\n\n日志：" + path);

            try
            {
                MessageBox.Show(message, "NLCL 遇到问题", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // 连 MessageBox 都弹不出来（例如没有桌面会话）时，至少日志已经落盘。
            }
        }
    }
}

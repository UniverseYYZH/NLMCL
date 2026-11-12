using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace NLMCL.App.Services
{
    /// <summary>
    /// 启动自检：把“这个进程到底能不能写盘、写到哪里”落盘。
    ///
    /// 背景：出现过“标题栏是新构建、但 %APPDATA%\NLMCL 下什么新文件都没有”的情况，
    /// 无法区分“代码没执行”和“进程被拦截写盘”。主日志写到数据目录的 logs，
    /// 失败时再尝试 exe 同级 startup.log，避免同时在 Temp/AppData/exe 三处留副本。
    /// </summary>
    internal static class StartupTrace
    {
        /// <summary>数据目录下的 logs；App 启动后设置。</summary>
        internal static string? LogDirectory { get; set; }

        internal static string BuildStamp()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                return File.Exists(location)
                    ? File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm:ss")
                    : "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        internal static void Write(Exception? failure)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("构建: " + BuildStamp());

            try
            {
                builder.AppendLine("exe: " + Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception ex)
            {
                builder.AppendLine("exe: 取不到路径 —— " + ex.Message);
            }

            builder.AppendLine("用户: " + Environment.UserDomainName + "\\" + Environment.UserName);
            builder.AppendLine("64位进程: " + Environment.Is64BitProcess);
            builder.AppendLine("当前目录: " + Environment.CurrentDirectory);
            builder.AppendLine("APPDATA: " + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            builder.AppendLine("TEMP: " + Path.GetTempPath());
            builder.AppendLine("启动异常: " + (failure == null ? "无" : failure.ToString()));
            builder.AppendLine();

            List<string> targets = new List<string>();

            if (!string.IsNullOrWhiteSpace(LogDirectory))
            {
                targets.Add(Path.Combine(LogDirectory!, "startup.log"));
            }
            else
            {
                // 还未确定数据目录（例如 AppPaths 构造前就失败）才用系统位置兜底。
                try
                {
                    targets.Add(Path.Combine(Path.GetTempPath(), "nlmcl-startup.log"));
                }
                catch (Exception)
                {
                }

                try
                {
                    targets.Add(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "NLMCL",
                        "startup.log"));
                }
                catch (Exception)
                {
                }
            }

            try
            {
                string? exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeFolder))
                {
                    string fallback = Path.Combine(exeFolder!, "startup.log");
                    if (!targets.Exists(target => string.Equals(
                        Path.GetFullPath(target), Path.GetFullPath(fallback), StringComparison.OrdinalIgnoreCase)))
                    {
                        targets.Add(fallback);
                    }
                }
            }
            catch (Exception)
            {
            }

            List<string> outcomes = new List<string>();

            foreach (string target in targets)
            {
                try
                {
                    string? folder = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder!);
                    }

                    File.WriteAllText(target, builder.ToString(), Encoding.UTF8);
                    outcomes.Add("OK    " + target);
                }
                catch (Exception ex)
                {
                    outcomes.Add("FAIL  " + target + " —— " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            // 把结果再追加到写成功的那些文件里（失败的就只能看别的文件）
            StringBuilder tail = new StringBuilder();
            tail.AppendLine();
            tail.AppendLine("写入结果：");
            foreach (string outcome in outcomes)
            {
                tail.AppendLine("  " + outcome);
            }

            foreach (string target in targets)
            {
                try
                {
                    if (File.Exists(target))
                    {
                        File.AppendAllText(target, tail.ToString(), Encoding.UTF8);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}

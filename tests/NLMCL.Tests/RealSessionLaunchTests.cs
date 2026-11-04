using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Pcl.Vb;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 可选的真实启动诊断：走**与应用完全相同**的会话路径（重定向 stdio、隐藏窗口、
    /// 工作目录 = .minecraft）把真实实例拉起来，收集 45 秒输出。
    ///
    /// 目的：区分"命令行不对"和"进程创建方式不对"。
    /// 需要 NLMCL_TEST_MC_DIR / NLMCL_TEST_VERSION；Java 可用 NLMCL_TEST_JAVA 覆盖。
    /// </summary>
    public class RealSessionLaunchTests
    {
        [RealFact]
        public async Task Launches_real_instance_through_the_session_path()
        {
            string? minecraft = Environment.GetEnvironmentVariable("NLMCL_TEST_MC_DIR");
            string? version = Environment.GetEnvironmentVariable("NLMCL_TEST_VERSION");
            if (string.IsNullOrWhiteSpace(minecraft) || string.IsNullOrWhiteSpace(version))
            {
                return; // 没配环境变量：跳过
            }

            string java = Environment.GetEnvironmentVariable("NLMCL_TEST_JAVA")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".minecraft", "runtime", "java-runtime-epsilon", "bin", "javaw.exe");

            Assert.True(File.Exists(java), "Java 不存在：" + java);

            string logFile = Path.Combine(minecraft, "logs", "latest.log");
            DateTime? before = File.Exists(logFile) ? new FileInfo(logFile).LastWriteTime : (DateTime?)null;

            PclGameSessionLauncher launcher = new PclGameSessionLauncher();
            LaunchRequest request = new LaunchRequest(minecraft, version)
            {
                JavaExecutable = java,
                JavaVersion = new Version(17, 0),
                PlayerName = "Player",
                AccessToken = "0",
                MaximumMemoryMb = 2048
            };

            IGameSession session = await launcher.StartAsync(request, CancellationToken.None);

            List<string> lines = new List<string>();
            TaskCompletionSource<int> exited = new TaskCompletionSource<int>();

            session.Output += (sender, line) =>
            {
                lock (lines)
                {
                    lines.Add(line.Text);
                }
            };
            session.Exited += (sender, code) => exited.TrySetResult(code);

            Task finished = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(45)));

            int? exitCode = exited.Task.IsCompleted ? exited.Task.Result : (int?)null;

            // 还活着就主动结束，别给测试机上留一个游戏窗口
            if (!exitCode.HasValue)
            {
                try
                {
                    session.Stop();
                }
                catch (Exception)
                {
                }
            }

            session.Dispose();

            string reportPath = Path.Combine(Path.GetTempPath(), "nlmcl-real-session.log");
            StringBuilder report = new StringBuilder();
            report.AppendLine("Java: " + java);
            report.AppendLine("退出码: " + (exitCode.HasValue ? exitCode.Value.ToString() : "(45s 内未退出，视为存活)"));
            DateTime? after = File.Exists(logFile) ? new FileInfo(logFile).LastWriteTime : (DateTime?)null;
            report.AppendLine("latest.log 之前: " + (before.HasValue ? before.Value.ToString("HH:mm:ss") : "(无)"));
            report.AppendLine("latest.log 之后: " + (after.HasValue ? after.Value.ToString("HH:mm:ss") : "(无)"));
            report.AppendLine();

            lock (lines)
            {
                foreach (string line in lines)
                {
                    report.AppendLine(line);
                }
            }

            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            // 只关心一件事：会话路径下游戏能不能正常写文件/正常跑起来
            Assert.DoesNotContain("拒绝访问", report.ToString());
        }
    }
}

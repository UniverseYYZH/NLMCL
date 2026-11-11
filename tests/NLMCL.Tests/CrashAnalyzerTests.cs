using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Crash;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>崩溃分析：特征匹配、可疑模组提取、无日志时的提示。</summary>
    public class CrashAnalyzerTests
    {
        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-crash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            return root;
        }

        private static void WriteLog(string root, string text)
        {
            File.WriteAllText(Path.Combine(root, "logs", "latest.log"), text);
        }

        [Fact]
        public async Task Detects_missing_dependency_and_suspect()
        {
            string root = NewRoot();

            try
            {
                WriteLog(root, @"[12:00:00] [main/ERROR]: Missing or unsupported mandatory dependencies:
	Mod ID: 'architectury', Requested by: 'somemod', Expected range: '[9.0,)', Actual version: '[MISSING]'");

                CrashAnalysis analysis = await new CrashAnalyzer()
                    .AnalyzeAsync(root, "1.20.1", CancellationToken.None);

                Assert.Contains(analysis.Matches, match => match.Title.Contains("缺少前置"));
                Assert.Contains("architectury", analysis.Suspects);
                Assert.EndsWith("latest.log", analysis.Source);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Detects_out_of_memory()
        {
            string root = NewRoot();

            try
            {
                WriteLog(root, "[12:00:00] [main/ERROR]: java.lang.OutOfMemoryError: Java heap space");

                CrashAnalysis analysis = await new CrashAnalyzer()
                    .AnalyzeAsync(root, "1.20.1", CancellationToken.None);

                Assert.Contains(analysis.Matches, match => match.Title.Contains("内存不足"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Detects_corrupt_jar()
        {
            string root = NewRoot();

            try
            {
                WriteLog(root, "Error: Invalid or corrupt jarfile mods/broken.jar");

                CrashAnalysis analysis = await new CrashAnalyzer()
                    .AnalyzeAsync(root, "1.20.1", CancellationToken.None);

                Assert.Contains(analysis.Matches, match => match.Title.Contains("jar 文件损坏"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Reports_when_no_log_exists()
        {
            string root = NewRoot();

            try
            {
                CrashAnalysis analysis = await new CrashAnalyzer()
                    .AnalyzeAsync(root, "1.20.1", CancellationToken.None);

                Assert.Empty(analysis.Matches);
                Assert.Contains("没找到", analysis.Summary);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Instance_crash_report_wins_over_a_newer_global_one()
        {
            string root = NewRoot();

            try
            {
                string ownDir = Path.Combine(root, "versions", "1.20.1", "crash-reports");
                string globalDir = Path.Combine(root, "crash-reports");
                Directory.CreateDirectory(ownDir);
                Directory.CreateDirectory(globalDir);

                string own = Path.Combine(ownDir, "crash-own.txt");
                string global = Path.Combine(globalDir, "crash-global.txt");
                File.WriteAllText(own, "own");
                File.WriteAllText(global, "global");
                File.SetLastWriteTime(own, DateTime.Now.AddHours(-2));
                File.SetLastWriteTime(global, DateTime.Now);

                Assert.Equal(own, CrashAnalyzer.FindNewestCrashReport(root, "1.20.1"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Prefers_crash_report_over_latest_log()
        {
            string root = NewRoot();

            try
            {
                string crashDir = Path.Combine(root, "versions", "1.20.1", "crash-reports");
                Directory.CreateDirectory(crashDir);
                File.WriteAllText(
                    Path.Combine(crashDir, "crash-2026-11-04_12.00.00-client.txt"),
                    "Description: Rendering overlay\njava.lang.OutOfMemoryError: Java heap space");

                WriteLog(root, "nothing interesting");

                CrashAnalysis analysis = await new CrashAnalyzer()
                    .AnalyzeAsync(root, "1.20.1", CancellationToken.None);

                Assert.EndsWith(".txt", analysis.Source);
                Assert.Contains(analysis.Matches, match => match.Title.Contains("内存不足"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

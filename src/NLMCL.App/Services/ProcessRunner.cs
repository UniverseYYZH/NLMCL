using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>
    /// 启动外部进程并逐行回收输出（Forge 安装器用）。
    /// 只在 App 层实现：Core 只依赖 <see cref="IProcessRunner"/> 端口，方便单测。
    /// </summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string executable,
            string arguments,
            string workingDirectory,
            Action<string>? onOutput,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                StringBuilder output = new StringBuilder();

                ProcessStartInfo info = new ProcessStartInfo(executable, arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = new Process { StartInfo = info })
                {
                    void Append(string? line)
                    {
                        if (line == null)
                        {
                            return;
                        }

                        lock (output)
                        {
                            output.AppendLine(line);
                        }

                        onOutput?.Invoke(line);
                    }

                    process.OutputDataReceived += (_, e) => Append(e.Data);
                    process.ErrorDataReceived += (_, e) => Append(e.Data);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    while (!process.WaitForExit(500))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (InvalidOperationException)
                            {
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    // 让异步输出回调把剩余内容刷完
                    process.WaitForExit();

                    string text;
                    lock (output)
                    {
                        text = output.ToString();
                    }

                    return new ProcessResult(process.ExitCode, text);
                }
            }, cancellationToken);
        }
    }
}

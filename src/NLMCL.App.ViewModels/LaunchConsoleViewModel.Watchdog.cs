using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NLMCL.Abstractions;
using NLMCL.Core.Logging;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 「启动与日志」的"卡住"检测。
    ///
    /// 有些情况进程起来了但什么都不输出（JVM 参数错误、classpath 为空、显卡初始化卡死），
    /// 用户看到的是"运行中"+空白日志，完全无从判断。这里在 60 秒无输出时给出明确提示。
    /// </summary>
    public sealed partial class LaunchConsoleViewModel
    {
        private const int SilenceWarningSeconds = 60;

        private Timer? _watchdog;
        private DateTime _lastOutputUtc = DateTime.UtcNow;
        private bool _silenceWarned;

        /// <summary>每收到一行输出就调用，重置计时。</summary>
        internal void MarkOutput()
        {
            _lastOutputUtc = DateTime.UtcNow;
            _silenceWarned = false;
        }

        /// <summary>进程启动成功后开始盯。</summary>
        private void StartWatchdog()
        {
            _lastOutputUtc = DateTime.UtcNow;
            _silenceWarned = false;

            StopWatchdog();

            _watchdog = new Timer(_ => CheckSilence(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }

        private void StopWatchdog()
        {
            _watchdog?.Dispose();
            _watchdog = null;
        }

        /// <summary>测试用：把"最后一次输出"往前拨。</summary>
        internal void BackdateSilence(TimeSpan span)
        {
            _lastOutputUtc = DateTime.UtcNow - span;
        }

        internal void CheckSilence()
        {
            if (!IsRunning || _silenceWarned)
            {
                return;
            }

            TimeSpan silent = DateTime.UtcNow - _lastOutputUtc;

            if (silent.TotalSeconds < SilenceWarningSeconds)
            {
                return;
            }

            _silenceWarned = true;

            _dispatcher.Post(() =>
            {
                if (!IsRunning)
                {
                    return;
                }

                Lines.Add(new ConsoleLine(
                    "【提示】启动后 " + SilenceWarningSeconds
                    + " 秒没有任何输出。可能卡在 JVM 初始化 / 类路径 / 显卡驱动；"
                    + "可以先「停止」再检查：Java 版本是否匹配、实例是否缺文件（本地版本 → 修复）。",
                    LogTag.JavaIncompatible));

                StatusText = "可能卡在启动阶段（" + SilenceWarningSeconds + " 秒无输出）";
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>游戏进程输出的一行。</summary>
    public sealed class GameOutputLine
    {
        public GameOutputLine(string text, bool isError)
        {
            Text = text ?? string.Empty;
            IsError = isError;
        }

        public string Text { get; }

        /// <summary>是否来自 stderr。</summary>
        public bool IsError { get; }
    }

    /// <summary>
    /// 一个正在运行的游戏进程。控制台页面通过它拿输出与停止。
    /// </summary>
    public interface IGameSession : IDisposable
    {
        int ProcessId { get; }

        bool IsRunning { get; }

        /// <summary>
        /// 已观测到的退出码；进程还在运行时为 null。
        ///
        /// 存在的理由：<see cref="Exited"/> 是一次性事件。JVM 秒退（参数被拒、主类缺失）
        /// 时，进程可能在消费者订阅之前就退出，那次广播没人听见，界面会永远停在"运行中"。
        /// 消费者订阅后应立刻查一次这个属性补拿终态。
        /// </summary>
        int? ExitCode { get; }

        /// <summary>进程输出（stdout/stderr 均已合并顺序到达）。可能在后台线程触发。</summary>
        event EventHandler<GameOutputLine>? Output;

        /// <summary>进程退出。</summary>
        event EventHandler<int>? Exited;

        /// <summary>请求结束进程（先 Kill，不做优雅退出握手）。</summary>
        void Stop();
    }

    /// <summary>启动一个带输出捕获的游戏会话。</summary>
    public interface IGameSessionLauncher
    {
        Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken);
    }
}

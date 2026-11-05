using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class MultiLaunchTests
    {
        [Fact]
        public void Two_sessions_can_run_at_once()
        {
            CountingSessions sessions = new CountingSessions();
            LaunchConsoleViewModel console = TestSupport.CreateConsole(
                new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            FakeSession first = new FakeSession(1001);
            FakeSession second = new FakeSession(1002);

            console.Attach(first);
            Assert.True(console.IsRunning);

            // 多开：第二个会话接进来时，不能把第一个踢掉
            console.Attach(second);
            Assert.True(console.IsRunning);
            Assert.Contains("2", console.StatusText);

            // 第一个退出：还有第二个在跑，界面必须保持"运行中"
            first.Exit(0);
            Assert.True(console.IsRunning);

            // 最后一个退出才算结束
            second.Exit(0);
            Assert.False(console.IsRunning);
        }


        [Fact]
        public void Logs_are_kept_per_session_and_restored_on_switch()
        {
            LaunchConsoleViewModel console = TestSupport.CreateConsole(new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            FakeSession first = new FakeSession(2001);
            FakeSession second = new FakeSession(2002);

            console.Attach(first);
            first.Emit("第一个游戏的日志");

            console.Attach(second);
            second.Emit("第二个游戏的日志");

            // 界面上只显示当前选中的那个
            Assert.Contains(console.Lines, l => l.Text.Contains("第二个"));
            Assert.DoesNotContain(console.Lines, l => l.Text.Contains("第一个"));

            // 切回第一个：日志要能重建
            console.SelectSession(console.Sessions[0]);
            Assert.Contains(console.Lines, l => l.Text.Contains("第一个"));
        }

        [Fact]
        public void Stop_all_stops_every_running_game()
        {
            LaunchConsoleViewModel console = TestSupport.CreateConsole(new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            FakeSession first = new FakeSession(3001);
            FakeSession second = new FakeSession(3002);

            console.Attach(first);
            console.Attach(second);

            console.StopAll();

            Assert.Equal(1, first.StopCalls);
            Assert.Equal(1, second.StopCalls);
        }


        [Fact]
        public void Exited_sessions_are_hidden_from_the_bar_by_default()
        {
            LaunchConsoleViewModel console = TestSupport.CreateConsole(new NLMCL.Core.AppPaths(@"C:\Temp\roaming"));

            FakeSession running = new FakeSession(4001);
            FakeSession exited = new FakeSession(4002);

            console.Attach(running);
            console.Attach(exited);

            exited.Exit(0);

            // 历史记录不该一直占着会话栏
            Assert.Single(console.BarSessions);
            Assert.True(console.BarSessions[0].IsRunning);
            Assert.Contains("1", console.ExitedSessionsLabel);

            // 打开开关能看到它
            console.ToggleExitedSessions();
            Assert.Equal(2, console.BarSessions.Count);

            // 清理后彻底移除
            console.ClearExitedSessions();
            Assert.Single(console.Sessions);
            Assert.Single(console.BarSessions);
        }

        private sealed class CountingSessions : IGameSessionLauncher
        {
            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
                => Task.FromResult<IGameSession>(new FakeSession(1));
        }

        private sealed class FakeSession : IGameSession
        {
            public FakeSession(int pid)
            {
                ProcessId = pid;
            }

            public event EventHandler<GameOutputLine>? Output;

            public event EventHandler<int>? Exited;

            public int ProcessId { get; }

            public bool IsRunning { get; private set; } = true;

            public int? ExitCode { get; private set; }

            public int StopCalls { get; private set; }

            public void Stop()
            {
                StopCalls++;
                IsRunning = false;
            }

            public void Emit(string text)
            {
                Output?.Invoke(this, new GameOutputLine(text, false));
            }

            public void Exit(int code)
            {
                IsRunning = false;
                ExitCode = code;
                Exited?.Invoke(this, code);
            }

            public void Dispose()
            {
            }
        }
    }
}

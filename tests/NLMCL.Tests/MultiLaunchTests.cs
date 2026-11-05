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

            public void Stop() => IsRunning = false;

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
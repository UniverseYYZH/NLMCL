using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.App.ViewModels;
using NLCL.Core;
using NLCL.Core.Logging;
using Xunit;

namespace NLCL.Tests
{
    public class LaunchConsoleViewModelTests
    {
        private sealed class FakeSession : IGameSession
        {
            public event EventHandler<GameOutputLine>? Output;

            public event EventHandler<int>? Exited;

            public int ProcessId => 999;

            public bool IsRunning { get; private set; } = true;

            public int? ExitCode { get; set; }

            public int StopCalls { get; private set; }

            public void Stop()
            {
                StopCalls++;
                IsRunning = false;
            }

            public void Emit(string text, bool isError = false)
            {
                Output?.Invoke(this, new GameOutputLine(text, isError));
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

        private sealed class FakeSessionLauncher : IGameSessionLauncher
        {
            public FakeSession Session { get; } = new FakeSession();

            public LaunchRequest? LastRequest { get; private set; }

            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult<IGameSession>(Session);
            }
        }

        private sealed class FixedJavaLocator : IJavaLocator
        {
            private readonly IReadOnlyList<JavaRuntime> _javas;

            public FixedJavaLocator(params JavaRuntime[] javas)
            {
                _javas = javas;
            }

            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_javas);
            }
        }

        private static LaunchConsoleViewModel Create(
            FakeSessionLauncher launcher, IJavaLocator javas, IUiDispatcher? dispatcher = null)
        {
            return new LaunchConsoleViewModel(
                launcher, javas, new AppPaths(@"C:\Temp\roaming"), dispatcher ?? new ImmediateDispatcher());
        }

        [Fact]
        public async Task Start_without_version_asks_user()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                TestSupport.CreateJava(17)));

            await vm.StartAsync();

            Assert.Null(launcher.LastRequest);
            Assert.Contains("版本", vm.StatusText);
        }

        [Fact]
        public async Task Start_without_java_reports_and_stays_idle()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator());
            vm.VersionId = "1.21.1";

            await vm.StartAsync();

            Assert.Null(launcher.LastRequest);
            Assert.False(vm.IsRunning);
            Assert.Contains("Java", vm.StatusText);
        }

        [Fact]
        public async Task Output_lines_are_classified_and_counted()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                TestSupport.CreateJava(21)));
            vm.VersionId = "1.21.1";

            await vm.StartAsync();

            Assert.True(vm.IsRunning);
            Assert.Equal("1.21.1", launcher.LastRequest!.VersionId);
            Assert.EndsWith(@"bin\javaw.exe", launcher.LastRequest.JavaExecutable, StringComparison.OrdinalIgnoreCase);

            launcher.Session.Emit("[Render thread/INFO] Minecraft 1.21.1 started");
            launcher.Session.Emit("java.lang.UnsupportedClassVersionError: bad class file");
            launcher.Session.Emit("Failed to load texture minecraft:textures/block/stone.png");

            Assert.Equal(3, vm.Lines.Count);
            Assert.Equal(LogTag.Normal, vm.Lines[0].Tag);
            Assert.Equal(LogTag.JavaIncompatible, vm.Lines[1].Tag);
            Assert.Equal(LogTag.ResourceFailure, vm.Lines[2].Tag);

            // 只有非普通行计入异常数
            Assert.Equal(2, vm.ErrorCount);

            // 普通行不显示标签，否则整屏都是"正常信息"
            Assert.False(vm.Lines[0].ShowTag);
            Assert.True(vm.Lines[1].ShowTag);
        }

        [Fact]
        public async Task Lines_are_trimmed_to_the_limit()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                TestSupport.CreateJava(21)));
            vm.VersionId = "1.21.1";
            vm.MaxLines = 5;

            await vm.StartAsync();

            for (int i = 0; i < 12; i++)
            {
                launcher.Session.Emit("line " + i);
            }

            Assert.Equal(5, vm.Lines.Count);
            Assert.Equal("line 11", vm.Lines[4].Text);
        }

        [Fact]
        public async Task Exit_updates_state()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                TestSupport.CreateJava(21)));
            vm.VersionId = "1.21.1";

            await vm.StartAsync();
            launcher.Session.Exit(-1);

            Assert.False(vm.IsRunning);
            Assert.Contains("-1", vm.StatusText);
        }

        [Fact]
        public async Task Stop_and_clear_behave()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                TestSupport.CreateJava(21)));
            vm.VersionId = "1.21.1";

            await vm.StartAsync();
            launcher.Session.Emit("Mod Resolution encountered an error");
            Assert.Equal(1, vm.ErrorCount);

            vm.Clear();
            Assert.Empty(vm.Lines);
            Assert.Equal(0, vm.ErrorCount);

            vm.Stop();
            Assert.Equal(1, launcher.Session.StopCalls);
        }

        /// <summary>
        /// 回归：进程输出回调在后台线程，直接改绑定了界面的 ObservableCollection
        /// 会让 WPF 抛跨线程异常并把整个进程带走。必须封送到 UI 线程再改。
        /// </summary>
        [Fact]
        public async Task Output_from_background_thread_is_marshaled_to_the_ui_thread()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            QueuedDispatcher dispatcher = new QueuedDispatcher();
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                TestSupport.CreateJava(21)), dispatcher);
            vm.VersionId = "1.21.1";

            await vm.StartAsync();

            launcher.Session.Emit("[main/INFO] 一行日志");

            // 还没轮到 UI 线程：集合必须原封不动（这正是修复前会崩的那一步）
            Assert.Empty(vm.Lines);
            Assert.Equal(1, dispatcher.Pending);

            dispatcher.Drain();

            Assert.Single(vm.Lines);
            Assert.Contains("一行日志", vm.Lines[0].Text);
        }

        /// <summary>回归：控制台启动必须按版本要求挑 Java，不能无脑取最高版本。</summary>
        [Fact]
        public async Task Console_picks_the_java_the_version_asks_for()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlcl-console-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "versions", "1.12.2"));
            File.WriteAllText(
                Path.Combine(root, "versions", "1.12.2", "1.12.2.json"),
                "{\"id\":\"1.12.2\",\"javaVersion\":{\"majorVersion\":8}}");

            try
            {
                FakeSessionLauncher launcher = new FakeSessionLauncher();
                LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(
                    TestSupport.CreateJava(8), TestSupport.CreateJava(21)));
                vm.GameDirectory = root;
                vm.VersionId = "1.12.2";

                await vm.StartAsync();

                Assert.NotNull(launcher.LastRequest);
                Assert.Equal(8, launcher.LastRequest!.JavaVersion?.Major);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>回归：JVM 秒退时 Exited 在订阅前就广播完了，界面不能永远停在"运行中"。</summary>
        [Fact]
        public async Task Exit_that_happened_before_subscription_is_still_reported()
        {
            FakeSessionLauncher launcher = new FakeSessionLauncher();
            launcher.Session.ExitCode = 1;
            LaunchConsoleViewModel vm = Create(launcher, new FixedJavaLocator(TestSupport.CreateJava(21)));
            vm.VersionId = "1.21.1";

            await vm.StartAsync();

            // 修复前：会话已退出但事件没人听见，IsRunning 永远为 true、按钮从此点不动。
            Assert.False(vm.IsRunning);
            Assert.DoesNotContain("运行中", vm.StatusText);
        }
    }
}

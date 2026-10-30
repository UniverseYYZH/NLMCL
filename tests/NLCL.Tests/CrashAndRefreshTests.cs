using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.App.ViewModels;
using NLCL.Core;
using NLCL.Core.Accounts;
using NLCL.Core.Logging;
using Xunit;

namespace NLCL.Tests
{
    public class CrashDetectorTests
    {
        [Fact]
        public void Crash_report_marker_is_detected()
        {
            CrashReport? report = CrashDetector.Detect(0, new[]
            {
                "[Render thread/INFO] Stopping!",
                "---- Minecraft Crash Report ----",
                "Description: Unexpected error"
            });

            Assert.NotNull(report);
            Assert.Contains("崩溃报告", report!.Reason);
            Assert.Equal("---- Minecraft Crash Report ----", report.EvidenceLine);
        }

        [Fact]
        public void Out_of_memory_gives_actionable_hint()
        {
            CrashReport? report = CrashDetector.Detect(1, new[]
            {
                "java.lang.OutOfMemoryError: Java heap space"
            });

            Assert.NotNull(report);
            Assert.NotNull(report!.Hint);
            Assert.Contains("内存", report.Hint!);
        }

        [Fact]
        public void Old_java_is_named_as_the_cause()
        {
            CrashReport? report = CrashDetector.Detect(1, new[]
            {
                "java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main"
            });

            Assert.NotNull(report);
            Assert.Contains("Java", report!.Hint!);
        }

        [Fact]
        public void Clean_exit_with_zero_code_is_not_a_crash()
        {
            Assert.Null(CrashDetector.Detect(0, new[] { "[Render thread/INFO] Stopping!" }));
        }

        [Fact]
        public void Nonzero_exit_without_logs_still_reports()
        {
            // 进程被系统杀掉时往往没有日志——不能因为"日志干净"就当正常
            CrashReport? report = CrashDetector.Detect(-1073741819, Array.Empty<string>());

            Assert.NotNull(report);
            Assert.Contains("异常退出", report!.Reason);
        }

        [Fact]
        public void Null_lines_do_not_throw()
        {
            Assert.Null(CrashDetector.Detect(0, null));
        }
    }

    public class LaunchConsoleCrashTests
    {
        private sealed class FakeSession : IGameSession
        {
            public event EventHandler<GameOutputLine>? Output;

            public event EventHandler<int>? Exited;

            public int ProcessId => 1;

            public bool IsRunning { get; private set; } = true;

            public int? ExitCode { get; private set; }

            public void Stop() => IsRunning = false;

            public void Emit(string text) => Output?.Invoke(this, new GameOutputLine(text, false));

            public void Crash(int code)
            {
                IsRunning = false;
                ExitCode = code;
                Exited?.Invoke(this, code);
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeLauncher : IGameSessionLauncher
        {
            public FakeSession Session { get; } = new FakeSession();

            public Task<IGameSession> StartAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult<IGameSession>(Session);
            }
        }

        private sealed class Jdk : IJavaLocator
        {
            public Task<IReadOnlyList<JavaRuntime>> FindInstalledAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<JavaRuntime>>(
                    new[] { TestSupport.CreateJava(21) });
            }
        }

        [Fact]
        public async Task Crash_adds_explanation_lines_to_console()
        {
            FakeLauncher launcher = new FakeLauncher();
            LaunchConsoleViewModel vm = new LaunchConsoleViewModel(
                launcher, new Jdk(), new AppPaths(@"C:\Temp\roaming"), new ImmediateDispatcher());
            vm.VersionId = "1.21.1";

            await vm.StartAsync();
            launcher.Session.Emit("java.lang.OutOfMemoryError: Java heap space");
            launcher.Session.Crash(1);

            Assert.False(vm.IsRunning);
            Assert.Contains("崩溃", vm.StatusText);
            Assert.Contains(vm.Lines, line => line.Text.Contains("【崩溃】"));
            Assert.Contains(vm.Lines, line => line.Text.Contains("【建议】"));
        }

        [Fact]
        public async Task Normal_exit_does_not_add_crash_lines()
        {
            FakeLauncher launcher = new FakeLauncher();
            LaunchConsoleViewModel vm = new LaunchConsoleViewModel(
                launcher, new Jdk(), new AppPaths(@"C:\Temp\roaming"), new ImmediateDispatcher());
            vm.VersionId = "1.21.1";

            await vm.StartAsync();
            launcher.Session.Emit("[Render thread/INFO] Stopping!");
            launcher.Session.Crash(0);

            Assert.False(vm.IsRunning);
            Assert.DoesNotContain(vm.Lines, line => line.Text.Contains("【崩溃】"));
        }
    }

    public class AccountRefreshTests
    {
        private sealed class FakeAuth : IMicrosoftAuthenticator
        {
            public FakeAuth(bool succeed)
            {
                Succeed = succeed;
            }

            public bool Succeed { get; }

            public int RefreshCalls { get; private set; }

            public Task<DeviceCodeChallenge> StartAsync(CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<Account> WaitForAccountAsync(DeviceCodeChallenge challenge,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<Account> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
            {
                RefreshCalls++;

                if (!Succeed)
                {
                    throw new InvalidOperationException("refresh_token 已失效");
                }

                return Task.FromResult(new Account(
                    "Steve", "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                    AccountType.Microsoft, "new-token", "new-refresh"));
            }
        }

        private static MainViewModel Create(IMicrosoftAuthenticator auth, TestSupport.MemoryAccountStore store)
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            return new MainViewModel(
                new TestSupport.EmptyCatalog(), paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                new TestSupport.NoopLauncher(),
                new TestSupport.NoopJavaLocator(),
                store,
                auth,
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());
        }

        [Fact]
        public async Task Stored_microsoft_account_is_refreshed_on_load()
        {
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore
            {
                Current = new Account("Steve", "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                    AccountType.Microsoft, "old-token", "old-refresh")
            };

            FakeAuth auth = new FakeAuth(succeed: true);
            MainViewModel vm = Create(auth, store);

            await vm.LoadAccountAsync();

            Assert.Equal(1, auth.RefreshCalls);
            Assert.NotNull(vm.Account);
            Assert.Equal("new-token", vm.Account!.AccessToken);
            Assert.Equal("new-refresh", vm.Account.RefreshToken);
            Assert.Equal("new-token", store.Current!.AccessToken);
        }

        [Fact]
        public async Task Failed_refresh_keeps_the_old_account()
        {
            Account original = new Account("Steve", "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                AccountType.Microsoft, "old-token", "old-refresh");

            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore { Current = original };
            FakeAuth auth = new FakeAuth(succeed: false);
            MainViewModel vm = Create(auth, store);

            await vm.LoadAccountAsync();

            // 续期失败不能把人踢下线——旧令牌可能还没过期
            Assert.NotNull(vm.Account);
            Assert.Equal("old-token", vm.Account!.AccessToken);
            Assert.Contains("续期失败", vm.StatusText);
        }

        [Fact]
        public async Task Offline_account_never_tries_to_refresh()
        {
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore
            {
                Current = OfflineAccountFactory.Create("Alex")
            };

            FakeAuth auth = new FakeAuth(succeed: true);
            MainViewModel vm = Create(auth, store);

            await vm.LoadAccountAsync();

            Assert.Equal(0, auth.RefreshCalls);
            Assert.Equal("Alex", vm.Account!.Name);
        }
    }
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>首启引导：目录校验、步骤推进、完成后落盘、跳过也落盘。</summary>
    public class WizardTests
    {
        private sealed class FakePicker : IFolderPicker
        {
            private readonly string? _path;

            public FakePicker(string? path = null)
            {
                _path = path;
            }

            public string? PickFolder(string description, string? initialDirectory) => _path;
        }

        private sealed class EmptyJavaLocator : IJavaLocator
        {
            public Task<JavaRuntime> DownloadAsync(
                string component, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<System.Collections.Generic.IReadOnlyList<JavaRuntime>> FindInstalledAsync(
                CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<JavaRuntime>>(
                    new System.Collections.Generic.List<JavaRuntime>());
        }

        private sealed class MemoryAccounts : IAccountStore
        {
            public Account? Saved { get; private set; }

            public Task<Account?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

            public Task SaveAsync(Account account, CancellationToken cancellationToken)
            {
                Saved = account;
                return Task.CompletedTask;
            }

            public Task SignOutAsync(CancellationToken cancellationToken)
            {
                Saved = null;
                return Task.CompletedTask;
            }
            public Task ClearAsync(CancellationToken cancellationToken)
            {
                Saved = null;
                return Task.CompletedTask;
            }

            public Task<System.Collections.Generic.IReadOnlyList<Account>> LoadAllAsync(
                CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<Account>>(
                    Saved == null ? new System.Collections.Generic.List<Account>() : new System.Collections.Generic.List<Account> { Saved });

            public Task SwitchAsync(Account account, CancellationToken cancellationToken)
            {
                Saved = account;
                return Task.CompletedTask;
            }

            public Task RemoveAsync(Account account, CancellationToken cancellationToken)
            {
                Saved = null;
                return Task.CompletedTask;
            }
        }

        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-wizard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static WizardViewModel Create(string root, MemoryAccounts accounts, string? picked = null)
        {
            AppPaths paths = new AppPaths(root);
            return new WizardViewModel(
                paths, new JsonAppSettingsStore(paths), new FakePicker(picked), new EmptyJavaLocator(), accounts);
        }

        [Fact]
        public async Task Next_creates_missing_directory_and_advances()
        {
            string root = NewRoot();

            try
            {
                WizardViewModel vm = Create(root, new MemoryAccounts());
                string target = Path.Combine(root, "custom-mc");
                vm.GameDirectory = target;

                await vm.NextAsync();

                Assert.True(Directory.Exists(target));
                Assert.Equal(1, vm.Step); // 进到 Java 步骤
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Empty_directory_is_rejected()
        {
            string root = NewRoot();

            try
            {
                WizardViewModel vm = Create(root, new MemoryAccounts());
                vm.GameDirectory = "  ";

                await vm.NextAsync();

                Assert.Equal(0, vm.Step);
                Assert.Contains("请先选择", vm.StatusText);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Finish_saves_directory_account_and_flag()
        {
            string root = NewRoot();

            try
            {
                MemoryAccounts accounts = new MemoryAccounts();
                WizardViewModel vm = Create(root, accounts);

                string target = Path.Combine(root, "mc");
                Directory.CreateDirectory(target);
                vm.GameDirectory = target;
                vm.PlayerName = "Steve";

                bool completed = false;
                vm.Completed += () => completed = true;

                await vm.FinishAsync();

                Assert.True(completed);
                Assert.NotNull(accounts.Saved);
                Assert.Equal("Steve", accounts.Saved!.Name);

                AppPaths paths = new AppPaths(root);
                AppSettings settings = await new JsonAppSettingsStore(paths)
                    .LoadAsync(CancellationToken.None);

                Assert.True(settings.WizardCompleted);
                Assert.Equal(target, settings.GameDirectory);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Skip_marks_completed_without_account()
        {
            string root = NewRoot();

            try
            {
                MemoryAccounts accounts = new MemoryAccounts();
                WizardViewModel vm = Create(root, accounts);

                bool completed = false;
                vm.Completed += () => completed = true;

                await vm.SkipAsync();

                Assert.True(completed);
                Assert.Null(accounts.Saved);

                AppPaths paths = new AppPaths(root);
                AppSettings settings = await new JsonAppSettingsStore(paths)
                    .LoadAsync(CancellationToken.None);

                Assert.True(settings.WizardCompleted);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Accounts;
using Xunit;

namespace NLMCL.Tests
{
    public class OfflineAccountFactoryTests
    {
        [Fact]
        public void Uuid_is_deterministic_and_version3_shaped()
        {
            string first = OfflineAccountFactory.ComputeUuid("Steve");
            string second = OfflineAccountFactory.ComputeUuid("Steve");

            Assert.Equal(first, second);
            Assert.Matches(
                new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-3[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"),
                first);
        }

        [Fact]
        public void Different_names_give_different_uuids()
        {
            Assert.NotEqual(
                OfflineAccountFactory.ComputeUuid("Steve"),
                OfflineAccountFactory.ComputeUuid("Alex"));
        }

        [Fact]
        public void Name_is_trimmed_and_blank_is_rejected()
        {
            Account account = OfflineAccountFactory.Create("  Steve  ");
            Assert.Equal("Steve", account.Name);
            Assert.Equal(AccountType.Offline, account.Type);
            Assert.Equal(OfflineAccountFactory.ComputeUuid("Steve"), account.Uuid);

            // 离线账号没有令牌，启动时用占位值
            Assert.Null(account.AccessToken);
            Assert.Equal("0", account.EffectiveAccessToken);

            Assert.Throws<ArgumentException>(() => OfflineAccountFactory.Create("   "));
        }
    }

    public class JsonAccountStoreTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-acct-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Round_trips_through_disk()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAccountStore store = new JsonAccountStore(paths);

            try
            {
                Assert.Null(await store.LoadAsync(CancellationToken.None));

                Account saved = OfflineAccountFactory.Create("Steve");
                await store.SaveAsync(saved, CancellationToken.None);

                Assert.True(File.Exists(paths.AccountsFile));

                Account? restored = await store.LoadAsync(CancellationToken.None);
                Assert.NotNull(restored);
                Assert.Equal(saved.Name, restored!.Name);
                Assert.Equal(saved.Uuid, restored.Uuid);
                Assert.Equal(AccountType.Offline, restored.Type);

                await store.ClearAsync(CancellationToken.None);
                Assert.Null(await store.LoadAsync(CancellationToken.None));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Corrupt_file_is_treated_as_signed_out()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAccountStore store = new JsonAccountStore(paths);

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                File.WriteAllText(paths.AccountsFile, "{ not json");

                // 坏文件不该让启动器崩：直接当"未登录"，而不是抛异常中断启动流程
                Account? account = await store.LoadAsync(CancellationToken.None);

                Assert.Null(account);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public class AccountFlowTests
    {
        private sealed class RecordingLauncher : IGameLauncher
        {
            public LaunchRequest? LastRequest { get; private set; }

            public Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new LaunchResult(1, true, "ok"));
            }
        }

        private sealed class OneVersionCatalog : IVersionCatalog
        {
            public Task<System.Collections.Generic.IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<MinecraftVersion>>(
                    new[] { new MinecraftVersion("1.21.1", "1.21.1", VersionKind.Release, null) });
            }
        }

        [Fact]
        public async Task Offline_sign_in_persists_and_flows_into_launch()
        {
            AppPaths paths = new AppPaths(@"C:\Temp\roaming");
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();
            RecordingLauncher launcher = new RecordingLauncher();

            MainViewModel vm = new MainViewModel(
                new OneVersionCatalog(),
                paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                launcher,
                new PclJavaLocatorStub(),
                store,
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());

            vm.PlayerNameInput = "Alex";
            await vm.SignInOfflineAsync();

            Assert.NotNull(vm.Account);
            Assert.Equal("Alex", vm.Account!.Name);
            Assert.Equal("Alex · 离线", vm.AccountLabel);
            Assert.NotNull(store.Current);

            await vm.RefreshAsync();
            vm.SelectVersion(vm.Versions[0]);
            await vm.LaunchSelectedAsync();

            Assert.NotNull(launcher.LastRequest);
            Assert.Equal("Alex", launcher.LastRequest!.PlayerName);
            Assert.Equal(OfflineAccountFactory.ComputeUuid("Alex"), launcher.LastRequest.PlayerUuid);
            Assert.Equal("0", launcher.LastRequest.AccessToken);

            await vm.SignOutAsync();
            Assert.Null(vm.Account);
            Assert.Null(store.Current);
        }

        private sealed class PclJavaLocatorStub : IJavaLocator
        {
            public Task<System.Collections.Generic.IReadOnlyList<JavaRuntime>> FindInstalledAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult<System.Collections.Generic.IReadOnlyList<JavaRuntime>>(
                    new[] { TestSupport.CreateJava(21) });
            }
        }

        [Fact]
        public async Task Account_popup_closes_after_sign_in_and_sign_out()
        {
            AppPaths paths = new AppPaths(Path.Combine(Path.GetTempPath(), "nlmcl-acct-" + Guid.NewGuid().ToString("N")));
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();

            MainViewModel vm = new MainViewModel(
                new OneVersionCatalog(),
                paths,
                TestSupport.CreateDownload(paths),
                TestSupport.CreateConsole(paths),
                new RecordingLauncher(),
                new PclJavaLocatorStub(),
                store,
                new TestSupport.NoopAuthenticator(),
                new TestSupport.MemoryInstanceSettingsStore(),
                new TestSupport.FakeNetwork(),
                new TestSupport.FakeClipboard(),
                new TestSupport.FakeFolderPicker(), new TestSupport.FakeAppSettings());

            vm.ToggleAccountPopup();
            Assert.True(vm.IsAccountPopupOpen);

            vm.PlayerNameInput = "Steve";
            await vm.SignInOfflineAsync();

            // 登录完成后应自动收起，否则用户得自己点右上角叉
            Assert.False(vm.IsAccountPopupOpen);

            vm.ToggleAccountPopup();
            Assert.True(vm.IsAccountPopupOpen);

            await vm.SignOutAsync();
            Assert.False(vm.IsAccountPopupOpen);
        }

        [Fact]
        public async Task Sign_out_keeps_every_account_in_history()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"));

            NLMCL.Core.AppPaths paths = new NLMCL.Core.AppPaths(root);
            NLMCL.Core.Accounts.JsonAccountStore store = new NLMCL.Core.Accounts.JsonAccountStore(paths);

            await store.SaveAsync(new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline), CancellationToken.None);
            await store.SaveAsync(new NLMCL.Abstractions.Account(
                "Beta", "uuid-b", NLMCL.Abstractions.AccountType.Offline), CancellationToken.None);

            // 退出登录：只清当前，不删账号
            await store.SignOutAsync(CancellationToken.None);

            Assert.Null(await store.LoadAsync(CancellationToken.None));

            var all = await store.LoadAllAsync(CancellationToken.None);
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public async Task Sign_out_keeps_other_accounts_in_history()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"));

            NLMCL.Core.AppPaths paths = new NLMCL.Core.AppPaths(root);
            NLMCL.Core.Accounts.JsonAccountStore store = new NLMCL.Core.Accounts.JsonAccountStore(paths);

            NLMCL.Abstractions.Account first = new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline);
            NLMCL.Abstractions.Account second = new NLMCL.Abstractions.Account(
                "Beta", "uuid-b", NLMCL.Abstractions.AccountType.Offline);

            await store.SaveAsync(first, CancellationToken.None);
            await store.SaveAsync(second, CancellationToken.None);

            // 模拟"退出当前账号"：只移除一个
            await store.RemoveAsync(second, CancellationToken.None);

            var remaining = await store.LoadAllAsync(CancellationToken.None);

            Assert.Single(remaining);
            Assert.Equal("Alpha", remaining[0].Name);
        }

        [Fact]
        public async Task Account_history_records_last_used_time()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"));

            NLMCL.Core.AppPaths paths = new NLMCL.Core.AppPaths(root);
            NLMCL.Core.Accounts.JsonAccountStore store = new NLMCL.Core.Accounts.JsonAccountStore(paths);

            await store.SaveAsync(new NLMCL.Abstractions.Account(
                "Gamma", "uuid-g", NLMCL.Abstractions.AccountType.Offline), CancellationToken.None);

            var all = await store.LoadAllAsync(CancellationToken.None);

            Assert.Single(all);
            Assert.NotNull(all[0].LastUsed);
            Assert.NotEmpty(all[0].LastUsedLabel);
        }

        [Fact]
        public async Task Switching_works_right_after_sign_out()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"));

            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();

            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(root),
                accounts: store);

            NLMCL.Abstractions.Account alpha = new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline);

            store.All.Add(alpha);
            await vm.SignOutAsync();

            Assert.Null(vm.Account);

            // 退出登录后点「切换」必须能登上（以前被 Account == null 挡掉了）
            await vm.SwitchAccountAsync(alpha);

            Assert.NotNull(vm.Account);
            Assert.Equal("Alpha", vm.Account!.Name);
        }

        [Fact]
        public async Task Switching_applies_even_when_persisting_fails()
        {
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore
            {
                FailSwitch = true
            };

            NLMCL.Abstractions.Account alpha = new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline);
            NLMCL.Abstractions.Account beta = new NLMCL.Abstractions.Account(
                "Beta", "uuid-b", NLMCL.Abstractions.AccountType.Offline);

            store.Current = alpha;
            store.All.Add(alpha);
            store.All.Add(beta);

            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"))),
                accounts: store);

            vm.Account = alpha;
            vm.IsAccountPopupOpen = true;

            // 账号文件写入失败不能让用户卡在旧账号：内存里也要完成切换，并明确提示没有保存。
            await vm.SwitchAccountAsync(beta);

            Assert.Equal("Beta", vm.Account!.Name);
            Assert.False(vm.IsAccountPopupOpen);
            Assert.Contains("未能写入", vm.StatusText);
        }

        [Fact]
        public async Task Loading_current_account_also_fills_switch_list()
        {
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();

            NLMCL.Abstractions.Account alpha = new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline);
            NLMCL.Abstractions.Account beta = new NLMCL.Abstractions.Account(
                "Beta", "uuid-b", NLMCL.Abstractions.AccountType.Offline);

            store.Current = alpha;
            store.All.Add(alpha);
            store.All.Add(beta);

            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"))),
                accounts: store);

            await vm.LoadAccountAsync();

            Assert.Equal(2, vm.SavedAccounts.Count);
            Assert.Single(vm.SavedAccounts, item => item.IsCurrent);
            Assert.Contains(vm.SavedAccounts, item => item.Name == "Beta");
        }

        [Fact]
        public async Task Switching_account_updates_skin_page_immediately()
        {
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();

            NLMCL.Abstractions.Account alpha = new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline);
            NLMCL.Abstractions.Account beta = new NLMCL.Abstractions.Account(
                "Beta", "uuid-b", NLMCL.Abstractions.AccountType.Offline);

            store.Current = alpha;
            store.All.Add(alpha);
            store.All.Add(beta);

            NLMCL.App.ViewModels.SkinViewModel skin = new NLMCL.App.ViewModels.SkinViewModel(
                new TestSupport.NoopSkinService());

            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"))),
                accounts: store,
                skin: skin);

            await vm.SwitchAccountAsync(alpha);
            Assert.Contains("Alpha", skin.AccountText);

            await vm.SwitchAccountAsync(beta);
            Assert.Contains("Beta", skin.AccountText);
        }

        [Fact]
        public async Task Avatar_load_race_keeps_last_account()
        {
            TestSupport.MemoryAccountStore store = new TestSupport.MemoryAccountStore();

            NLMCL.Abstractions.Account alpha = new NLMCL.Abstractions.Account(
                "Alpha", "uuid-a", NLMCL.Abstractions.AccountType.Offline);
            NLMCL.Abstractions.Account beta = new NLMCL.Abstractions.Account(
                "Beta", "uuid-b", NLMCL.Abstractions.AccountType.Offline);

            store.Current = alpha;
            store.All.Add(alpha);
            store.All.Add(beta);

            TestSupport.NoopSkinService skins = new TestSupport.NoopSkinService
            {
                AvatarPngHandler = async account =>
                {
                    await Task.Delay(account.Name == "Alpha" ? 250 : 10);
                    return System.Text.Encoding.UTF8.GetBytes(account.Name);
                }
            };

            NLMCL.App.ViewModels.MainViewModel vm = TestSupport.CreateMain(
                new TestSupport.EmptyCatalog(),
                new NLMCL.Core.AppPaths(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "nlmcl-acct-" + System.Guid.NewGuid().ToString("N"))),
                accounts: store,
                skins: skins);

            await vm.SwitchAccountAsync(alpha);
            await vm.SwitchAccountAsync(beta);

            await Task.Delay(600);

            Assert.NotNull(vm.AvatarPng);
            Assert.Equal("Beta", System.Text.Encoding.UTF8.GetString(vm.AvatarPng!));
        }
    }
}

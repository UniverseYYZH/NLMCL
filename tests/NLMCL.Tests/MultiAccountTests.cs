using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Accounts;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>多账号存储：老格式迁移、保存/切换/删除、无效账号过滤。</summary>
    public class MultiAccountTests
    {
        private static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-multiacc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void WriteLegacyFile(AppPaths paths, string json)
        {
            Directory.CreateDirectory(paths.DataRoot);
            File.WriteAllText(paths.AccountsFile, json);
        }

        [Fact]
        public async Task Legacy_single_account_is_readable()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                WriteLegacyFile(paths, @"{
                    ""name"": ""ZW_Official"",
                    ""uuid"": ""99390821-0a33-36a3-97a5-79ff8bc9a9af"",
                    ""type"": ""Offline""
                }");

                JsonAccountStore store = new JsonAccountStore(paths);

                Account? current = await store.LoadAsync(CancellationToken.None);
                var all = await store.LoadAllAsync(CancellationToken.None);

                Assert.NotNull(current);
                Assert.Equal("ZW_Official", current!.Name);
                Assert.Single(all);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Saving_second_account_keeps_both_and_switches_current()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                JsonAccountStore store = new JsonAccountStore(paths);

                Account first = OfflineAccountFactory.Create("Steve");
                Account second = OfflineAccountFactory.Create("Alex");

                await store.SaveAsync(first, CancellationToken.None);
                await store.SaveAsync(second, CancellationToken.None);

                var all = await store.LoadAllAsync(CancellationToken.None);

                Assert.Equal(2, all.Count);
                Assert.Equal("Alex", all[0].Name); // 当前账号排第一

                Account? current = await store.LoadAsync(CancellationToken.None);
                Assert.Equal("Alex", current!.Name);

                // 文件已是新格式
                Assert.Contains("\"accounts\"", File.ReadAllText(paths.AccountsFile));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Switching_account_changes_current_without_relogin()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                JsonAccountStore store = new JsonAccountStore(paths);

                Account first = OfflineAccountFactory.Create("Steve");
                Account second = OfflineAccountFactory.Create("Alex");

                await store.SaveAsync(first, CancellationToken.None);
                await store.SaveAsync(second, CancellationToken.None);

                await store.SwitchAsync(first, CancellationToken.None);

                Account? current = await store.LoadAsync(CancellationToken.None);

                Assert.Equal("Steve", current!.Name);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Removing_current_account_falls_back_to_remaining()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                JsonAccountStore store = new JsonAccountStore(paths);

                Account first = OfflineAccountFactory.Create("Steve");
                Account second = OfflineAccountFactory.Create("Alex");

                await store.SaveAsync(first, CancellationToken.None);
                await store.SaveAsync(second, CancellationToken.None);

                await store.RemoveAsync(second, CancellationToken.None);

                var all = await store.LoadAllAsync(CancellationToken.None);
                Account? current = await store.LoadAsync(CancellationToken.None);

                Assert.Single(all);
                Assert.Equal("Steve", current!.Name);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task External_account_without_token_is_dropped()
        {
            string root = NewRoot();

            try
            {
                AppPaths paths = new AppPaths(root);
                WriteLegacyFile(paths, @"{
                    ""name"": ""Broken"",
                    ""uuid"": ""11111111-1111-1111-1111-111111111111"",
                    ""type"": ""External""
                }");

                JsonAccountStore store = new JsonAccountStore(paths);

                Assert.Null(await store.LoadAsync(CancellationToken.None));
                Assert.Empty(await store.LoadAllAsync(CancellationToken.None));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

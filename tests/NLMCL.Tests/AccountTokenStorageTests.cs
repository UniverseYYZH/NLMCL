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
    /// <summary>令牌落盘的加密行为。</summary>
    public class AccountTokenStorageTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-token-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Token_is_not_written_in_plaintext()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAccountStore store = new JsonAccountStore(paths);

            try
            {
                Account account = new Account(
                    "Steve", "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                    AccountType.Microsoft, "super-secret-token-value");

                await store.SaveAsync(account, CancellationToken.None);

                string raw = File.ReadAllText(paths.AccountsFile);

                // 明文绝不能出现在文件里
                Assert.DoesNotContain("super-secret-token-value", raw);

                // 读回来必须还原
                Account? restored = await store.LoadAsync(CancellationToken.None);
                Assert.NotNull(restored);
                Assert.Equal("super-secret-token-value", restored!.AccessToken);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Legacy_plaintext_file_still_loads()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAccountStore store = new JsonAccountStore(paths);

            try
            {
                Directory.CreateDirectory(paths.DataRoot);

                // 老版本写的是明文，必须还能读——否则升级后所有人被动退出
                File.WriteAllText(paths.AccountsFile,
                    @"{ ""name"": ""Alex"", ""uuid"": ""00000000000000000000000000000001"", ""type"": ""Microsoft"", ""accessToken"": ""legacy-token"" }");

                Account? restored = await store.LoadAsync(CancellationToken.None);

                Assert.NotNull(restored);
                Assert.Equal("legacy-token", restored!.AccessToken);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Offline_account_writes_no_token()
        {
            string root = NewRoot();
            AppPaths paths = new AppPaths(root);
            JsonAccountStore store = new JsonAccountStore(paths);

            try
            {
                await store.SaveAsync(OfflineAccountFactory.Create("Steve"), CancellationToken.None);

                string raw = File.ReadAllText(paths.AccountsFile);
                Assert.Contains("\"accessToken\": null", raw);
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
}

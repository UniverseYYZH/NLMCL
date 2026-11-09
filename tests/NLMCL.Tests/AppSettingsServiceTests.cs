using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>设置服务：并发更新不能互相覆盖，异常后锁必须释放。</summary>
    public sealed class AppSettingsServiceTests
    {
        [Fact]
        public async Task Concurrent_updates_are_serialized()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-settings-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root);

            try
            {
                JsonAppSettingsStore store = new JsonAppSettingsStore(paths);
                AppSettingsService service = new AppSettingsService(store);

                await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                    service.UpdateAsync(
                        settings => settings.DefaultMemoryMb += 1,
                        CancellationToken.None)));

                AppSettings saved = await store.LoadAsync(CancellationToken.None);
                Assert.Equal(2048 + 20, saved.DefaultMemoryMb);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Update_releases_lock_after_exception()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-settings-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(root);

            try
            {
                JsonAppSettingsStore store = new JsonAppSettingsStore(paths);
                AppSettingsService service = new AppSettingsService(store);

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.UpdateAsync(
                        _ => throw new InvalidOperationException("boom"),
                        CancellationToken.None));

                await service.UpdateAsync(
                    settings => settings.Theme = "dark",
                    CancellationToken.None);

                AppSettings saved = await store.LoadAsync(CancellationToken.None);
                Assert.Equal("dark", saved.Theme);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static void TryDelete(string root)
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}

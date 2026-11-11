using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Maintenance;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>缓存清理：只扫/删安全类别，绝不能碰存档和最近的分块。</summary>
    public sealed class CacheCleanerTests
    {
        [Fact]
        public async Task Scans_and_cleans_only_selected_safe_categories()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-cache-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(Path.Combine(root, "roaming"));
            string game = Path.Combine(root, ".minecraft");
            string temp = Path.Combine(root, "temp");

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                Directory.CreateDirectory(game);
                Directory.CreateDirectory(temp);

                // 旧的下载分块：应被统计/清理
                string oldParts = Path.Combine(game, "versions", "1.21.11", "mods", "a.jar.parts");
                Directory.CreateDirectory(oldParts);
                File.WriteAllBytes(Path.Combine(oldParts, "0.part"), new byte[2048]);
                Directory.SetLastWriteTimeUtc(oldParts, DateTime.UtcNow.AddHours(-8));

                // 最近的分块：可能还在下载，不能动
                string newParts = Path.Combine(game, "versions", "1.21.11", "mods", "b.jar.parts");
                Directory.CreateDirectory(newParts);
                File.WriteAllBytes(Path.Combine(newParts, "0.part"), new byte[1024]);

                // 存档：绝对不能动
                string saves = Path.Combine(game, "saves", "world");
                Directory.CreateDirectory(saves);
                File.WriteAllText(Path.Combine(saves, "level.dat"), "keep");

                // 存档目录里同名后缀的目录也不能当成下载缓存。
                string saveParts = Path.Combine(saves, "data.parts");
                Directory.CreateDirectory(saveParts);
                File.WriteAllText(Path.Combine(saveParts, "0.part"), "keep");
                Directory.SetLastWriteTimeUtc(saveParts, DateTime.UtcNow.AddHours(-8));

                string logs = Path.Combine(paths.DataRoot, "logs");
                Directory.CreateDirectory(logs);
                string oldLog = Path.Combine(logs, "crash-old.log");
                File.WriteAllText(oldLog, "old");
                File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-20));
                string newLog = Path.Combine(logs, "crash-new.log");
                File.WriteAllText(newLog, "new");

                string broken = paths.SettingsFile + ".broken";
                File.WriteAllText(broken, "{ broken");

                string diag = Path.Combine(temp, "nlmcl-diag-test");
                Directory.CreateDirectory(diag);
                File.WriteAllText(Path.Combine(diag, "x.txt"), "x");

                string tempLog = Path.Combine(temp, "nlmcl-download.log");
                File.WriteAllText(tempLog, "log");

                CacheCleaner cleaner = new CacheCleaner(paths, temp);
                IReadOnlyList<CacheCategory> categories = await cleaner.ScanAsync(game, CancellationToken.None);

                CacheCategory parts = Assert.Single(
                    categories, item => item.Id == CacheCleaner.DownloadPartsId);
                Assert.Equal(1, parts.ItemCount);

                Assert.Contains(categories, item => item.Id == CacheCleaner.TempDiagnosticsId);
                Assert.Contains(categories, item => item.Id == CacheCleaner.TempLogsId);
                Assert.Contains(categories, item => item.Id == CacheCleaner.OldLogsId);
                Assert.Contains(categories, item => item.Id == CacheCleaner.BrokenSettingsId);

                CacheCleanupResult result = await cleaner.CleanAsync(
                    game,
                    new[]
                    {
                        CacheCleaner.DownloadPartsId,
                        CacheCleaner.TempDiagnosticsId,
                        CacheCleaner.TempLogsId,
                        CacheCleaner.OldLogsId,
                        CacheCleaner.BrokenSettingsId
                    },
                    CancellationToken.None);

                Assert.True(result.FreedBytes > 0);
                Assert.Equal(5, result.DeletedCount);
                Assert.Empty(result.Errors);

                Assert.False(Directory.Exists(oldParts));
                Assert.True(Directory.Exists(newParts));
                Assert.True(File.Exists(Path.Combine(saves, "level.dat")));
                Assert.True(Directory.Exists(saveParts));
                Assert.False(File.Exists(oldLog));
                Assert.True(File.Exists(newLog));
                Assert.False(File.Exists(broken));
                Assert.False(Directory.Exists(diag));
                Assert.False(File.Exists(tempLog));
            }
            finally
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

        [Fact]
        public async Task Skin_temp_files_are_grouped_and_cleaned_safely()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-cache-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(Path.Combine(root, "roaming"));
            string game = Path.Combine(root, ".minecraft");
            string temp = Path.Combine(root, "temp");

            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                Directory.CreateDirectory(game);
                Directory.CreateDirectory(temp);

                string[] skinFiles =
                {
                    Path.Combine(temp, "nlmcl-skin-src-aaa.png"),
                    Path.Combine(temp, "nlmcl-skin-doll-bbb.png"),
                    Path.Combine(temp, "nlmcl-mua-ccc.png"),
                    Path.Combine(temp, "nlmcl-avatar-ddd.png"),
                    Path.Combine(temp, "nlmcl-default-skin-eee.png")
                };

                foreach (string file in skinFiles)
                {
                    File.WriteAllBytes(file, new byte[16]);
                }

                string keep = Path.Combine(temp, "nlmcl-other.png");
                File.WriteAllBytes(keep, new byte[16]);

                string stable = Path.Combine(paths.SkinDirectory, "cache", "account-test.png");
                Directory.CreateDirectory(Path.GetDirectoryName(stable)!);
                File.WriteAllBytes(stable, new byte[16]);

                CacheCleaner cleaner = new CacheCleaner(paths, temp);
                IReadOnlyList<CacheCategory> categories = await cleaner.ScanAsync(game, CancellationToken.None);

                CacheCategory skin = Assert.Single(
                    categories, item => item.Id == CacheCleaner.SkinTempId);
                Assert.Equal(5, skin.ItemCount);
                Assert.Equal(5 * 16, skin.SizeBytes);

                CacheCleanupResult result = await cleaner.CleanAsync(
                    game, new[] { CacheCleaner.SkinTempId }, CancellationToken.None);

                Assert.Equal(5, result.DeletedCount);
                Assert.Equal(5 * 16, result.FreedBytes);
                Assert.Empty(result.Errors);

                foreach (string file in skinFiles)
                {
                    Assert.False(File.Exists(file), file);
                }

                Assert.True(File.Exists(keep));
                Assert.True(File.Exists(stable));
            }
            finally
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

        [Fact]
        public async Task Missing_game_directory_is_safe()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-cache-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(Path.Combine(root, "roaming"));
            string temp = Path.Combine(root, "temp");
            Directory.CreateDirectory(temp);

            try
            {
                CacheCleaner cleaner = new CacheCleaner(paths, temp);

                IReadOnlyList<CacheCategory> categories = await cleaner.ScanAsync(
                    Path.Combine(root, "missing"), CancellationToken.None);

                Assert.DoesNotContain(categories, item => item.Id == CacheCleaner.DownloadPartsId);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

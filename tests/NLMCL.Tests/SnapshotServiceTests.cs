using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Snapshots;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 实例快照：备份、回滚、保护快照、隔离目录识别。
    /// </summary>
    public class SnapshotServiceTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-snap-" + Guid.NewGuid().ToString("N"));

        private static AppPaths PathsOf(string root) => new AppPaths(root);

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, Encoding.UTF8);
        }

        /// <summary>造一个"隔离实例"：存档在 versions\&lt;folder&gt;\ 下。</summary>
        private static void CreateIsolatedInstance(string root, string folder)
        {
            string dir = Path.Combine(root, ".minecraft", "versions", folder);
            Write(Path.Combine(dir, "saves", "world", "level.dat"), "level-1");
            Write(Path.Combine(dir, "options.txt"), "fov:70");
            Write(Path.Combine(dir, "config", "mod.toml"), "a=1");
        }

        [Fact]
        public async Task Backs_up_isolated_instance_data()
        {
            string root = NewRoot();
            try
            {
                CreateIsolatedInstance(root, "1.20.1");
                AppPaths paths = PathsOf(root);
                SnapshotService service = new SnapshotService(paths);

                SnapshotInfo info = await service.CreateAsync(
                    Path.Combine(root, ".minecraft"), "1.20.1", "装 mod 前", CancellationToken.None);

                Assert.Contains("saves", info.Items);
                Assert.Contains("config", info.Items);
                Assert.Contains("options.txt", info.Items);
                Assert.True(info.FileCount >= 3, "应至少备份 3 个文件，实际 " + info.FileCount + "，items=" + string.Join(",", info.Items));
                Assert.True(info.SizeBytes > 0);

                string snapshotDir = Path.Combine(paths.SnapshotDirectory, info.Id);
                Assert.True(File.Exists(Path.Combine(snapshotDir, "saves", "world", "level.dat")));
                Assert.True(File.Exists(Path.Combine(snapshotDir, "options.txt")));
                Assert.True(File.Exists(Path.Combine(snapshotDir, SnapshotService.MetadataFileName)));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Falls_back_to_minecraft_root_for_non_isolated_versions()
        {
            string root = NewRoot();
            try
            {
                // 非隔离：存档直接在 .minecraft 根
                Write(Path.Combine(root, ".minecraft", "saves", "w", "level.dat"), "x");
                Directory.CreateDirectory(Path.Combine(root, ".minecraft", "versions", "1.20.1"));

                SnapshotService service = new SnapshotService(PathsOf(root));
                SnapshotInfo info = await service.CreateAsync(
                    Path.Combine(root, ".minecraft"), "1.20.1", "", CancellationToken.None);

                Assert.Contains("saves", info.Items);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Throws_when_there_is_nothing_to_back_up()
        {
            string root = NewRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, ".minecraft", "versions", "1.20.1"));
                SnapshotService service = new SnapshotService(PathsOf(root));

                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.CreateAsync(Path.Combine(root, ".minecraft"), "1.20.1", "", CancellationToken.None));

                Assert.Contains("没有可备份的内容", error.Message);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Restore_replaces_content_and_keeps_a_safety_snapshot()
        {
            string root = NewRoot();
            try
            {
                CreateIsolatedInstance(root, "1.20.1");
                SnapshotService service = new SnapshotService(PathsOf(root));
                string mc = Path.Combine(root, ".minecraft");

                SnapshotInfo good = await service.CreateAsync(mc, "1.20.1", "好状态", CancellationToken.None);

                // 把存档改坏
                Write(Path.Combine(mc, "versions", "1.20.1", "saves", "world", "level.dat"), "broken");

                await service.RestoreAsync(good, mc, CancellationToken.None);

                string restored = File.ReadAllText(
                    Path.Combine(mc, "versions", "1.20.1", "saves", "world", "level.dat"));
                Assert.Equal("level-1", restored);

                IReadOnlyList<SnapshotInfo> all = await service.ListAsync(CancellationToken.None);
                Assert.Contains(all, item => item.Note.Contains("回滚前自动备份"));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Restore_refuses_to_fall_back_when_the_instance_is_gone()
        {
            string root = NewRoot();
            try
            {
                CreateIsolatedInstance(root, "1.20.1");
                SnapshotService service = new SnapshotService(PathsOf(root));
                string mc = Path.Combine(root, ".minecraft");

                SnapshotInfo snapshot = await service.CreateAsync(
                    mc, "1.20.1", "实例还在时", CancellationToken.None);

                Directory.Delete(Path.Combine(mc, "versions", "1.20.1"), true);

                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.RestoreAsync(snapshot, mc, CancellationToken.None));

                Assert.Contains("实例目录不存在", error.Message);
                Assert.False(Directory.Exists(Path.Combine(mc, "saves")));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task List_is_newest_first_and_delete_removes_it()
        {
            string root = NewRoot();
            try
            {
                CreateIsolatedInstance(root, "1.20.1");
                SnapshotService service = new SnapshotService(PathsOf(root));
                string mc = Path.Combine(root, ".minecraft");

                SnapshotInfo first = await service.CreateAsync(mc, "1.20.1", "一号", CancellationToken.None);
                await Task.Delay(1100); // 快照 id 精确到秒
                SnapshotInfo second = await service.CreateAsync(mc, "1.20.1", "二号", CancellationToken.None);

                IReadOnlyList<SnapshotInfo> list = await service.ListAsync(CancellationToken.None);
                Assert.Equal(2, list.Count);
                Assert.Equal(second.Id, list[0].Id);
                Assert.Equal(first.Id, list[1].Id);

                await service.DeleteAsync(first.Id, CancellationToken.None);
                list = await service.ListAsync(CancellationToken.None);
                Assert.Single(list);
                Assert.Equal(second.Id, list[0].Id);
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
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

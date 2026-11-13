using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.SafeMode;
using Xunit;

namespace NLMCL.Tests
{
    public class SafeModeServiceTests
    {
        [Fact]
        public async Task Begin_disables_root_jars_and_restore_puts_them_back()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-safe-mode-");
            string mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            string first = Path.Combine(mods, "sodium.jar");
            string second = Path.Combine(mods, "create.jar");
            File.WriteAllText(first, "sodium");
            File.WriteAllText(second, "create");
            File.WriteAllText(Path.Combine(mods, "notes.txt"), "keep");

            SafeModeService service = new SafeModeService(Path.Combine(root, "data"));
            SafeModeSession session = await service.BeginAsync(mods, CancellationToken.None);

            Assert.Equal(2, session.DisabledCount);
            Assert.False(File.Exists(first));
            Assert.False(File.Exists(second));
            Assert.True(File.Exists(first + ".disabled"));
            Assert.True(File.Exists(second + ".disabled"));
            Assert.True(File.Exists(Path.Combine(mods, "notes.txt")));
            Assert.True(File.Exists(session.JournalPath));

            SafeModeRestoreResult result = await service.RestoreAsync(session, CancellationToken.None);

            Assert.Equal(2, result.Restored);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.False(File.Exists(first + ".disabled"));
            Assert.False(File.Exists(second + ".disabled"));
            Assert.False(File.Exists(session.JournalPath));
        }

        [Fact]
        public async Task Recover_restores_a_journal_left_by_a_crash()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-safe-mode-recover-");
            string mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            string jar = Path.Combine(mods, "fabric-api.jar");
            File.WriteAllText(jar, "api");

            string data = Path.Combine(root, "data");
            SafeModeService first = new SafeModeService(data);
            SafeModeSession session = await first.BeginAsync(mods, CancellationToken.None);

            // 模拟进程被强杀：不调用 RestoreAsync，直接换一个实例启动。
            SafeModeService second = new SafeModeService(data);
            SafeModeRecoveryResult recovery = await second.RecoverPendingAsync(CancellationToken.None);

            Assert.Equal(1, recovery.Journals);
            Assert.Equal(1, recovery.Restored);
            Assert.True(File.Exists(jar));
            Assert.False(File.Exists(jar + ".disabled"));
            Assert.False(File.Exists(session.JournalPath));
        }

        [Fact]
        public async Task Begin_refuses_an_existing_disabled_name_without_touching_jars()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-safe-mode-conflict-");
            string mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            string jar = Path.Combine(mods, "sodium.jar");
            File.WriteAllText(jar, "sodium");
            File.WriteAllText(jar + ".disabled", "old");

            SafeModeService service = new SafeModeService(Path.Combine(root, "data"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.BeginAsync(mods, CancellationToken.None));

            Assert.True(File.Exists(jar));
            Assert.True(File.Exists(jar + ".disabled"));
            string journalDirectory = Path.Combine(root, "data", "safe-mode");
            Assert.True(!Directory.Exists(journalDirectory)
                || Directory.GetFiles(journalDirectory, "*.json").Length == 0);
        }

        [Fact]
        public async Task Restore_keeps_its_journal_when_both_names_exist()
        {
            string root = TestSupport.CreateTemporaryDirectory("nlmcl-safe-mode-conflict-restore-");
            string mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            string jar = Path.Combine(mods, "sodium.jar");
            File.WriteAllText(jar, "sodium");

            SafeModeService service = new SafeModeService(Path.Combine(root, "data"));
            SafeModeSession session = await service.BeginAsync(mods, CancellationToken.None);

            // 用户在安全模式运行期间又把同名 jar 放了回来。
            File.WriteAllText(jar, "new sodium");

            SafeModeRestoreResult first = await service.RestoreAsync(session, CancellationToken.None);

            Assert.Equal(0, first.Restored);
            Assert.Single(first.Warnings);
            Assert.True(File.Exists(session.JournalPath));
            Assert.True(File.Exists(jar));
            Assert.True(File.Exists(jar + ".disabled"));

            // 用户删掉自己新放的文件后，下一次恢复应能完成并清掉 journal。
            File.Delete(jar);
            SafeModeRestoreResult second = await service.RestoreAsync(session, CancellationToken.None);

            Assert.Equal(1, second.Restored);
            Assert.True(File.Exists(jar));
            Assert.False(File.Exists(jar + ".disabled"));
            Assert.False(File.Exists(session.JournalPath));
        }
    }
}

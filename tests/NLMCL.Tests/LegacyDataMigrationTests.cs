using System;
using System.IO;
using System.Text;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 改名迁移：老版本数据在 %APPDATA%\NLCL，新名字第一次启动时应自动搬过来。
    /// </summary>
    public class LegacyDataMigrationTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlmcl-migrate-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Copies_legacy_folder_when_new_one_is_missing()
        {
            string root = NewRoot();
            Directory.CreateDirectory(root);

            try
            {
                string legacy = Path.Combine(root, "NLCL");
                Directory.CreateDirectory(legacy);
                File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"gameDirectory\":\"D:\\\\MC\"}", Encoding.UTF8);
                Directory.CreateDirectory(Path.Combine(legacy, "instances"));
                File.WriteAllText(Path.Combine(legacy, "instances", "a.json"), "{}", Encoding.UTF8);

                string target = Path.Combine(root, "NLMCL");
                bool migrated = LegacyDataMigration.TryMigrate(target);

                Assert.True(migrated);
                Assert.True(File.Exists(Path.Combine(target, "settings.json")));
                Assert.True(File.Exists(Path.Combine(target, "instances", "a.json")));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Never_overwrites_existing_new_folder()
        {
            string root = NewRoot();
            Directory.CreateDirectory(root);

            try
            {
                string legacy = Path.Combine(root, "NLCL");
                Directory.CreateDirectory(legacy);
                File.WriteAllText(Path.Combine(legacy, "settings.json"), "old", Encoding.UTF8);

                string target = Path.Combine(root, "NLMCL");
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "settings.json"), "new", Encoding.UTF8);

                bool migrated = LegacyDataMigration.TryMigrate(target);

                Assert.False(migrated); // 没有可补的东西
                Assert.Equal("new", File.ReadAllText(Path.Combine(target, "settings.json")));
            }
            finally
            {
                TryDelete(root);
            }
        }

        /// <summary>
        /// 新目录已存在（例如测试或先跑过新版本，只写了个 pcl_settings.json）时，
        /// 老配置也要能补齐——否则改名的用户会"配置全丢"。
        /// </summary>
        [Fact]
        public void Fills_missing_files_even_when_new_folder_exists()
        {
            string root = NewRoot();
            Directory.CreateDirectory(root);

            try
            {
                string legacy = Path.Combine(root, "NLCL");
                Directory.CreateDirectory(legacy);
                File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"gameDirectory\":\"D:\\\\MC\"}", Encoding.UTF8);
                File.WriteAllText(Path.Combine(legacy, "accounts.json"), "{}", Encoding.UTF8);

                string target = Path.Combine(root, "NLMCL");
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "pcl_settings.json"), "{}", Encoding.UTF8);

                bool migrated = LegacyDataMigration.TryMigrate(target);

                Assert.True(migrated);
                Assert.True(File.Exists(Path.Combine(target, "settings.json")));
                Assert.True(File.Exists(Path.Combine(target, "accounts.json")));
                Assert.True(File.Exists(Path.Combine(target, "pcl_settings.json")));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Does_nothing_when_there_is_no_legacy_folder()
        {
            string root = NewRoot();
            Directory.CreateDirectory(root);

            try
            {
                Assert.False(LegacyDataMigration.TryMigrate(Path.Combine(root, "NLMCL")));
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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Core.Versions;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>实例管理：重命名、复制、删除、名字校验。</summary>
    public class VersionManagerTests
    {
        private static string NewMinecraft()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-vm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "versions"));
            return root;
        }

        private static void CreateInstance(string mc, string name, string extraFile = "options.txt")
        {
            string dir = Path.Combine(mc, "versions", name);
            Directory.CreateDirectory(Path.Combine(dir, "mods"));
            File.WriteAllText(
                Path.Combine(dir, name + ".json"),
                @"{ ""id"": """ + name + @""", ""inheritsFrom"": ""1.20.1"" }");
            File.WriteAllText(Path.Combine(dir, extraFile), "data");
            File.WriteAllText(Path.Combine(dir, "mods", "a.jar"), "jar");
        }

        [Fact]
        public async Task Rename_moves_folder_and_updates_id()
        {
            string mc = NewMinecraft();

            try
            {
                CreateInstance(mc, "Old");

                await new VersionManager().RenameAsync(mc, "Old", "New", CancellationToken.None);

                string target = Path.Combine(mc, "versions", "New");
                Assert.True(Directory.Exists(target));
                Assert.False(Directory.Exists(Path.Combine(mc, "versions", "Old")));
                Assert.True(File.Exists(Path.Combine(target, "mods", "a.jar")));

                string json = File.ReadAllText(Path.Combine(target, "New.json"));
                Assert.Contains("\"New\"", json);
                Assert.False(File.Exists(Path.Combine(target, "Old.json")));
            }
            finally
            {
                Directory.Delete(mc, true);
            }
        }

        [Fact]
        public async Task Copy_creates_independent_instance()
        {
            string mc = NewMinecraft();

            try
            {
                CreateInstance(mc, "Src");

                string created = await new VersionManager()
                    .CopyAsync(mc, "Src", "Copy", CancellationToken.None);

                Assert.Equal("Copy", created);

                string target = Path.Combine(mc, "versions", "Copy");
                Assert.True(File.Exists(Path.Combine(target, "options.txt")));
                Assert.True(File.Exists(Path.Combine(target, "mods", "a.jar")));
                Assert.True(Directory.Exists(Path.Combine(mc, "versions", "Src")));
                Assert.Contains("\"Copy\"", File.ReadAllText(Path.Combine(target, "Copy.json")));
            }
            finally
            {
                Directory.Delete(mc, true);
            }
        }

        [Fact]
        public async Task Delete_removes_whole_directory()
        {
            string mc = NewMinecraft();

            try
            {
                CreateInstance(mc, "Gone");

                await new VersionManager().DeleteAsync(mc, "Gone", CancellationToken.None);

                Assert.False(Directory.Exists(Path.Combine(mc, "versions", "Gone")));
            }
            finally
            {
                Directory.Delete(mc, true);
            }
        }

        [Fact]
        public void Name_validation_rejects_conflicts_and_bad_chars()
        {
            string mc = NewMinecraft();

            try
            {
                CreateInstance(mc, "Existing");
                VersionManager manager = new VersionManager();

                Assert.True(manager.IsNameAvailable(mc, "Fresh"));
                Assert.False(manager.IsNameAvailable(mc, "Existing"));
                Assert.False(manager.IsNameAvailable(mc, "bad/name"));
                Assert.False(manager.IsNameAvailable(mc, "   "));
                Assert.False(manager.IsNameAvailable(mc, "Existing", ignoreFolder: "Existing"));
            }
            finally
            {
                Directory.Delete(mc, true);
            }
        }
    }
}

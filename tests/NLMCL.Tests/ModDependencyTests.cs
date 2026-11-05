using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Core.Mods;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ModDependencyTests
    {
        [Fact]
        public async Task Mod_with_missing_dependency_is_flagged()
        {
            string dir = NewDirectory();

            WriteJar(Path.Combine(dir, "shooting-star.jar"), "shooting_star_demo_multi",
                ("fabric-api", "*"), ("rimulib", "^1.2.2"), ("minecraft", "1.21.1"), ("java", ">=17"),
                ("fabricloader", ">=0.15.0"));

            ModManager manager = new ModManager();
            var mods = await manager.ListAsync(dir, CancellationToken.None);

            var entry = mods.Single();
            Assert.True(entry.HasMissingDependencies);
            Assert.Contains("fabric-api", entry.MissingDependencies);
            Assert.Contains("rimulib", entry.MissingDependencies);

            // 游戏/Java/加载器本身不该算缺失
            Assert.DoesNotContain("minecraft", entry.MissingDependencies);
            Assert.DoesNotContain("java", entry.MissingDependencies);
            Assert.DoesNotContain("fabricloader", entry.MissingDependencies);
        }

        [Fact]
        public async Task Dependency_present_in_mods_folder_is_not_flagged()
        {
            string dir = NewDirectory();

            WriteJar(Path.Combine(dir, "shooting-star.jar"), "shooting_star_demo_multi",
                ("fabric-api", "*"), ("rimulib", "^1.2.2"));
            WriteJar(Path.Combine(dir, "fabric-api.jar"), "fabric-api", ("minecraft", "*"));
            WriteJar(Path.Combine(dir, "rimulib.jar"), "rimulib", ("minecraft", "*"));

            var mods = await new ModManager().ListAsync(dir, CancellationToken.None);

            Assert.All(mods, mod => Assert.False(mod.HasMissingDependencies));
        }

        [Fact]
        public async Task Disabled_dependency_does_not_count_as_installed()
        {
            string dir = NewDirectory();

            WriteJar(Path.Combine(dir, "shooting-star.jar"), "shooting_star_demo_multi", ("rimulib", "*"));
            WriteJar(Path.Combine(dir, "rimulib.jar.disabled"), "rimulib", ("minecraft", "*"));

            var mods = await new ModManager().ListAsync(dir, CancellationToken.None);

            var star = mods.Single(item => item.FileName.StartsWith("shooting-star", StringComparison.Ordinal));
            Assert.Contains("rimulib", star.MissingDependencies);
        }

        private static string NewDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nlmcl-moddep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteJar(string path, string id, params (string Id, string Version)[] depends)
        {
            string dependsJson = string.Join(",", depends.Select(
                item => "\"" + item.Id + "\":\"" + item.Version + "\""));

            string json = "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"" + id
                + "\",\"version\":\"1.0.0\",\"depends\":{" + dependsJson + "}}";

            using (FileStream stream = File.Create(path))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("fabric.mod.json");
                using (StreamWriter writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(json);
                }
            }
        }
    }
}
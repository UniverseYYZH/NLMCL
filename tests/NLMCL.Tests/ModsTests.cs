using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core.Mods;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>Mod 管理：元数据解析、启用/禁用（改后缀）、删除、列表。</summary>
    public class ModsTests
    {
        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nlmcl-mods-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteJar(string path, string entryName, string content)
        {
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);

                using (Stream stream = entry.Open())
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(content);
                }
            }
        }

        [Fact]
        public async Task Reads_fabric_metadata()
        {
            string dir = NewDir();

            try
            {
                WriteJar(
                    Path.Combine(dir, "sodium.jar"),
                    "fabric.mod.json",
                    @"{ ""id"": ""sodium"", ""name"": ""Sodium"", ""version"": ""0.5.13"" }");

                ModManager manager = new ModManager();
                var mods = await manager.ListAsync(dir, CancellationToken.None);

                ModEntry mod = Assert.Single(mods);
                Assert.Equal("Sodium", mod.Name);
                Assert.Equal("0.5.13", mod.Version);
                Assert.Equal("fabric", mod.Loader);
                Assert.True(mod.Enabled);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task Reads_forge_toml_metadata()
        {
            string dir = NewDir();

            try
            {
                WriteJar(
                    Path.Combine(dir, "jei.jar"),
                    "META-INF/mods.toml",
                    "[[mods]]\nmodId=\"jei\"\ndisplayName=\"Just Enough Items\"\nversion=\"15.2.0\"\n");

                var mods = await new ModManager().ListAsync(dir, CancellationToken.None);

                ModEntry mod = Assert.Single(mods);
                Assert.Equal("Just Enough Items", mod.Name);
                Assert.Equal("15.2.0", mod.Version);
                Assert.Equal("forge", mod.Loader);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task Disabled_files_are_listed_and_can_be_toggled()
        {
            string dir = NewDir();

            try
            {
                WriteJar(
                    Path.Combine(dir, "old.jar.disabled"),
                    "fabric.mod.json",
                    @"{ ""id"": ""old"", ""name"": ""Old Mod"", ""version"": ""1.0"" }");

                ModManager manager = new ModManager();
                var mods = await manager.ListAsync(dir, CancellationToken.None);

                ModEntry mod = Assert.Single(mods);
                Assert.False(mod.Enabled);
                Assert.Equal("Old Mod", mod.Name);

                await manager.EnableAsync(mod, CancellationToken.None);

                Assert.True(File.Exists(Path.Combine(dir, "old.jar")));
                Assert.False(File.Exists(Path.Combine(dir, "old.jar.disabled")));

                await manager.DisableAsync(mod, CancellationToken.None);

                Assert.False(File.Exists(Path.Combine(dir, "old.jar")));
                Assert.True(File.Exists(Path.Combine(dir, "old.jar.disabled")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task Delete_removes_file()
        {
            string dir = NewDir();

            try
            {
                WriteJar(Path.Combine(dir, "x.jar"), "fabric.mod.json", @"{ ""id"": ""x"" }");

                ModManager manager = new ModManager();
                ModEntry mod = (await manager.ListAsync(dir, CancellationToken.None)).Single();

                await manager.DeleteAsync(mod, CancellationToken.None);

                Assert.False(File.Exists(mod.FilePath));
                Assert.Empty(await manager.ListAsync(dir, CancellationToken.None));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task View_model_lists_and_toggles()
        {
            string dir = NewDir();

            try
            {
                WriteJar(Path.Combine(dir, "a.jar"), "fabric.mod.json", @"{ ""id"": ""a"", ""name"": ""A"", ""version"": ""1"" }");

                ModsViewModel vm = new ModsViewModel(new ModManager());
                vm.SetInstance(dir, "TestInstance", "1.20.1", "fabric");

                await vm.RefreshAsync();

                Assert.Single(vm.Mods);
                Assert.True(vm.HasMods);
                Assert.Contains("共 1 个模组", vm.StatusText);

                await vm.ToggleAsync(vm.Mods[0]);

                Assert.False(vm.Mods[0].Enabled);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task Update_check_runs_concurrently_and_applies_results()
        {
            FakeUpdateChecker checker = new FakeUpdateChecker();
            ModsViewModel vm = new ModsViewModel(new ModManager(), checker);
            vm.Mods.Add(new ModEntry("a.jar", "a.jar", 1) { Version = "1.0" });
            vm.Mods.Add(new ModEntry("b.jar", "b.jar", 1) { Version = "1.0" });

            await vm.CheckUpdatesAsync();

            Assert.True(checker.MaxConcurrency >= 2);
            Assert.Equal("2.0", vm.Mods[0].LatestVersion);
            Assert.Equal("2.0", vm.Mods[1].LatestVersion);
            Assert.Equal("发现 2 个模组有新版本。", vm.StatusText);
        }

        private sealed class FakeUpdateChecker : IModUpdateChecker
        {
            private int _current;
            private int _max;

            public int MaxConcurrency => _max;

            public async Task<ModUpdateInfo?> CheckAsync(
                ModEntry entry, string? gameVersion, string? loader, CancellationToken cancellationToken)
            {
                int current = Interlocked.Increment(ref _current);

                try
                {
                    InterlockedMax(ref _max, current);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    return new ModUpdateInfo { LatestVersion = "2.0" };
                }
                finally
                {
                    Interlocked.Decrement(ref _current);
                }
            }

            private static void InterlockedMax(ref int target, int value)
            {
                int current;

                do
                {
                    current = target;

                    if (value <= current)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref target, value, current) != current);
            }
        }
    }
}

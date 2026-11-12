using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NLMCL.Abstractions;
using NLMCL.Core.Instances;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class InstanceContentServiceTests
    {
        [Fact]
        public void Counts_and_imports_saves_and_packs()
        {
            string root = CreateRoot();
            string game = Path.Combine(root, "game");
            string saves = Path.Combine(game, "saves");
            string resourcePacks = Path.Combine(game, "resourcepacks");
            string shaderPacks = Path.Combine(game, "shaderpacks");

            Directory.CreateDirectory(Path.Combine(saves, "Existing"));
            Directory.CreateDirectory(resourcePacks);
            Directory.CreateDirectory(shaderPacks);
            File.WriteAllText(Path.Combine(resourcePacks, "old.zip"), "old");
            File.WriteAllText(Path.Combine(shaderPacks, "shader.zip"), "shader");

            var service = new InstanceContentService();

            Assert.Equal(1, service.Count(game, InstanceContentKind.Saves));
            Assert.Equal(1, service.Count(game, InstanceContentKind.ResourcePacks));
            Assert.Equal(1, service.Count(game, InstanceContentKind.ShaderPacks));

            string saveZip = Path.Combine(root, "MyWorld.zip");
            using (ZipArchive archive = ZipFile.Open(saveZip, ZipArchiveMode.Create))
            {
                Write(archive, "MyWorld/level.dat", "level");
                Write(archive, "MyWorld/region/r.0.0.mca", "region");
            }

            string imported = service.ImportAsync(game, InstanceContentKind.Saves, saveZip).GetAwaiter().GetResult();
            Assert.Equal(Path.Combine(saves, "MyWorld"), imported);
            Assert.True(File.Exists(Path.Combine(imported, "level.dat")));
            Assert.True(File.Exists(Path.Combine(imported, "region", "r.0.0.mca")));

            string second = service.ImportAsync(game, InstanceContentKind.Saves, saveZip).GetAwaiter().GetResult();
            Assert.Equal(Path.Combine(saves, "MyWorld (2)"), second);
            Assert.Equal(3, service.Count(game, InstanceContentKind.Saves));

            string packZip = Path.Combine(root, "Pack.zip");
            using (ZipArchive archive = ZipFile.Open(packZip, ZipArchiveMode.Create))
            {
                Write(archive, "pack.mcmeta", "{}");
            }

            string pack = service.ImportAsync(game, InstanceContentKind.ResourcePacks, packZip).GetAwaiter().GetResult();
            Assert.Equal(Path.Combine(resourcePacks, "Pack.zip"), pack);
            Assert.Equal(2, service.Count(game, InstanceContentKind.ResourcePacks));
        }

        [Fact]
        public void Save_import_rejects_paths_outside_the_world_folder()
        {
            string root = CreateRoot();
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, "saves"));

            string saveZip = Path.Combine(root, "bad.zip");
            using (ZipArchive archive = ZipFile.Open(saveZip, ZipArchiveMode.Create))
            {
                Write(archive, "../outside.txt", "bad");
            }

            var service = new InstanceContentService();

            Assert.Throws<InvalidDataException>(() =>
                service.ImportAsync(game, InstanceContentKind.Saves, saveZip).GetAwaiter().GetResult());
            Assert.False(File.Exists(Path.Combine(game, "outside.txt")));
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(game, "saves")));
        }

        private static void Write(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using (StreamWriter writer = new StreamWriter(entry.Open()))
            {
                writer.Write(content);
            }
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}

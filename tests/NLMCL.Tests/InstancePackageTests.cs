using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Core.Instances;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class InstancePackageTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "nlmcl-instance-package-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        [Fact]
        public async Task Export_and_import_preserve_files_and_rewrite_the_version_id()
        {
            string instance = Path.Combine(_root, "versions", "my-instance");
            Directory.CreateDirectory(Path.Combine(instance, "mods"));
            Directory.CreateDirectory(Path.Combine(instance, "logs"));
            Directory.CreateDirectory(Path.Combine(instance, "crash-reports"));
            File.WriteAllText(Path.Combine(instance, "my-instance.json"), "{\"id\":\"my-instance\",\"inheritsFrom\":\"1.21.1\"}");
            File.WriteAllText(Path.Combine(instance, "mods", "test.jar"), "jar");
            File.WriteAllText(Path.Combine(instance, "logs", "latest.log"), "secret");
            File.WriteAllText(Path.Combine(instance, "crash-reports", "crash.txt"), "secret");

            string zip = Path.Combine(_root, "out", "instance.zip");
            var service = new InstancePackageService();
            await service.ExportAsync(instance, zip, CancellationToken.None);

            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName == "nlmcl-package.json");
                Assert.Contains(archive.Entries, entry => entry.FullName == "mods/test.jar");
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("crash-reports/", StringComparison.Ordinal));
            }

            string versions = Path.Combine(_root, "imported", "versions");
            Directory.CreateDirectory(Path.Combine(versions, "my-instance"));
            string imported = await service.ImportAsync(zip, versions, CancellationToken.None);

            Assert.Equal("my-instance-imported", Path.GetFileName(imported));
            Assert.True(File.Exists(Path.Combine(imported, "mods", "test.jar")));
            JObject json = JObject.Parse(File.ReadAllText(Path.Combine(imported, "my-instance-imported.json")));
            Assert.Equal("my-instance-imported", (string?)json["id"]);
            Assert.False(File.Exists(Path.Combine(imported, "my-instance.json")));
            Assert.False(Directory.Exists(Path.Combine(imported, "logs")));
        }

        [Fact]
        public async Task Restore_replaces_the_instance_with_the_backup_contents()
        {
            string instance = Path.Combine(_root, "versions", "my-instance");
            Directory.CreateDirectory(Path.Combine(instance, "mods"));
            File.WriteAllText(Path.Combine(instance, "my-instance.json"), "{\"id\":\"my-instance\"}");
            File.WriteAllText(Path.Combine(instance, "mods", "test.jar"), "old");

            string zip = Path.Combine(_root, "backup.zip");
            var service = new InstancePackageService();
            await service.ExportAsync(instance, zip, CancellationToken.None);

            File.WriteAllText(Path.Combine(instance, "mods", "test.jar"), "new");
            File.WriteAllText(Path.Combine(instance, "mods", "added-later.jar"), "added");

            await service.RestoreAsync(zip, instance, CancellationToken.None);

            Assert.Equal("old", File.ReadAllText(Path.Combine(instance, "mods", "test.jar")));
            Assert.False(File.Exists(Path.Combine(instance, "mods", "added-later.jar")));
            JObject json = JObject.Parse(File.ReadAllText(Path.Combine(instance, "my-instance.json")));
            Assert.Equal("my-instance", (string?)json["id"]);
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(instance)!, "my-instance.nlmcl-restore-*"));
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(instance)!, "my-instance.nlmcl-rollback-*"));
        }

        [Fact]
        public async Task Import_rejects_entries_that_escape_the_instance_directory()
        {
            string zip = Path.Combine(_root, "evil.zip");
            Directory.CreateDirectory(_root);
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                ZipArchiveEntry manifest = archive.CreateEntry("nlmcl-package.json");
                using (var writer = new StreamWriter(manifest.Open()))
                {
                    writer.Write("{\"format\":1,\"name\":\"evil\",\"sourceFolder\":\"evil\"}");
                }

                archive.CreateEntry("../outside.txt");
            }

            var service = new InstancePackageService();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ImportAsync(zip, Path.Combine(_root, "versions"), CancellationToken.None));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
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

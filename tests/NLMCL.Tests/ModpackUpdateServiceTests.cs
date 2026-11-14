using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public class ModpackUpdateServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "nlmcl-modpack-update-" + Guid.NewGuid().ToString("N"));

        public ModpackUpdateServiceTests()
        {
            Directory.CreateDirectory(_root);
        }

        [Fact]
        public async Task Check_returns_the_newest_matching_version_and_persists_project_id()
        {
            string instance = Path.Combine(_root, "versions", "My Pack");
            Directory.CreateDirectory(instance);
            ModpackManifestStore.Write(instance, new ModpackManifest
            {
                ProjectId = "proj",
                VersionId = "v1",
                Name = "My Pack",
                MinecraftVersion = "1.21.1",
                Loader = "Fabric"
            });

            string versions = @"[
              { ""id"": ""v1"", ""version_number"": ""1.0.0"",
                ""date_published"": ""2026-01-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v1.mrpack"", ""filename"": ""v1.mrpack"", ""primary"": true } ] },
              { ""id"": ""v2"", ""version_number"": ""2.0.0"",
                ""date_published"": ""2026-02-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v2.mrpack"", ""filename"": ""v2.mrpack"", ""primary"": true,
                  ""hashes"": { ""sha1"": ""abc123"" } } ] }
            ]";

            FakeDownload download = new FakeDownload(versions);
            ModpackUpdateService service = new ModpackUpdateService(
                download, new FakePackages(), new AppPaths(Path.Combine(_root, "data"), portable: true));

            ModpackUpdateInfo? update = await service.CheckAsync(instance, CancellationToken.None);

            Assert.NotNull(update);
            Assert.Equal("v2", update!.VersionId);
            Assert.Equal("2.0.0", update.VersionName);
            Assert.Equal("https://cdn/v2.mrpack", update.DownloadUrl);
            Assert.Equal("v2.mrpack", update.FileName);
            Assert.Equal("abc123", update.Sha1);
            Assert.Contains("game_versions=", download.LastUrl);
            Assert.Contains("loaders=", download.LastUrl);
        }

        [Fact]
        public async Task Check_skips_curseforge_packs()
        {
            string instance = Path.Combine(_root, "versions", "CF Pack");
            Directory.CreateDirectory(instance);
            ModpackManifestStore.Write(instance, new ModpackManifest
            {
                Source = "curseforge",
                VersionId = "1",
                Name = "CF Pack"
            });

            ModpackUpdateService service = new ModpackUpdateService(
                new FakeDownload("[]"), new FakePackages(),
                new AppPaths(Path.Combine(_root, "data"), portable: true));

            Assert.Null(await service.CheckAsync(instance, CancellationToken.None));
        }

        [Fact]
        public async Task Check_does_not_offer_an_older_version_when_already_latest()
        {
            string instance = Path.Combine(_root, "versions", "Latest Pack");
            Directory.CreateDirectory(instance);
            ModpackManifestStore.Write(instance, new ModpackManifest
            {
                ProjectId = "proj",
                VersionId = "v2",
                Name = "Latest Pack",
                MinecraftVersion = "1.21.1",
                Loader = "Fabric"
            });

            string versions = @"[
              { ""id"": ""v2"", ""version_number"": ""2.0.0"",
                ""date_published"": ""2026-02-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v2.mrpack"", ""filename"": ""v2.mrpack"", ""primary"": true } ] },
              { ""id"": ""v1"", ""version_number"": ""1.0.0"",
                ""date_published"": ""2026-01-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v1.mrpack"", ""filename"": ""v1.mrpack"", ""primary"": true } ] }
            ]";

            ModpackUpdateService service = new ModpackUpdateService(
                new FakeDownload(versions), new FakePackages(),
                new AppPaths(Path.Combine(_root, "data"), portable: true));

            Assert.Null(await service.CheckAsync(instance, CancellationToken.None));
        }

        [Fact]
        public async Task Check_allows_a_newer_fabric_loader()
        {
            string instance = Path.Combine(_root, "versions", "Loader Pack");
            Directory.CreateDirectory(instance);
            ModpackManifestStore.Write(instance, new ModpackManifest
            {
                ProjectId = "proj",
                VersionId = "v1",
                Name = "Loader Pack",
                MinecraftVersion = "1.21.1",
                Loader = "Fabric",
                LoaderVersion = "0.16.10"
            });

            string versions = @"[
              { ""id"": ""v3"", ""version_number"": ""2.0.0"",
                ""date_published"": ""2026-02-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v3.mrpack"", ""filename"": ""v3.mrpack"", ""primary"": true } ] },
              { ""id"": ""v2"", ""version_number"": ""1.5.0"",
                ""date_published"": ""2026-01-15T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v2.mrpack"", ""filename"": ""v2.mrpack"", ""primary"": true } ] },
              { ""id"": ""v1"", ""version_number"": ""1.0.0"",
                ""date_published"": ""2026-01-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v1.mrpack"", ""filename"": ""v1.mrpack"", ""primary"": true } ] }
            ]";

            ModpackUpdateService service = new ModpackUpdateService(
                new FakeDownload(versions), new FakePackages(),
                new AppPaths(Path.Combine(_root, "data"), portable: true),
                new FakeFiles());

            ModpackUpdateInfo? update = await service.CheckAsync(instance, CancellationToken.None);

            Assert.NotNull(update);
            Assert.Equal("v3", update!.VersionId);
            Assert.Equal("2.0.0", update.VersionName);
        }

        [Fact]
        public async Task Check_skips_versions_that_require_a_newer_forge()
        {
            string instance = Path.Combine(_root, "versions", "Forge Pack");
            Directory.CreateDirectory(instance);
            ModpackManifestStore.Write(instance, new ModpackManifest
            {
                ProjectId = "proj",
                VersionId = "v1",
                Name = "Forge Pack",
                MinecraftVersion = "1.21.1",
                Loader = "Forge",
                LoaderVersion = "47.2.0"
            });

            string versions = @"[
              { ""id"": ""v3"", ""version_number"": ""2.0.0"",
                ""date_published"": ""2026-02-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v3.mrpack"", ""filename"": ""v3.mrpack"", ""primary"": true } ] },
              { ""id"": ""v2"", ""version_number"": ""1.5.0"",
                ""date_published"": ""2026-01-15T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v2.mrpack"", ""filename"": ""v2.mrpack"", ""primary"": true } ] },
              { ""id"": ""v1"", ""version_number"": ""1.0.0"",
                ""date_published"": ""2026-01-01T00:00:00Z"",
                ""files"": [ { ""url"": ""https://cdn/v1.mrpack"", ""filename"": ""v1.mrpack"", ""primary"": true } ] }
            ]";

            ModpackUpdateService service = new ModpackUpdateService(
                new FakeDownload(versions), new FakePackages(),
                new AppPaths(Path.Combine(_root, "data"), portable: true),
                new FakeFiles("forge", "47.4.0", "47.2.0"));

            ModpackUpdateInfo? update = await service.CheckAsync(instance, CancellationToken.None);

            Assert.NotNull(update);
            Assert.Equal("v2", update!.VersionId);
            Assert.Equal("1.5.0", update.VersionName);
        }

        [Fact]
        public async Task Backup_creates_a_zip_under_the_modpack_backup_directory()
        {
            string instance = Path.Combine(_root, "versions", "Backup Pack");
            Directory.CreateDirectory(instance);
            ModpackManifestStore.Write(instance, new ModpackManifest
            {
                VersionId = "v1",
                Name = "Backup Pack",
                MinecraftVersion = "1.21.1",
                Loader = "Fabric"
            });

            FakePackages packages = new FakePackages();
            AppPaths paths = new AppPaths(Path.Combine(_root, "data"), portable: true);
            ModpackUpdateService service = new ModpackUpdateService(
                new FakeDownload("[]"), packages, paths);

            string backup = await service.BackupAsync(instance, CancellationToken.None);

            Assert.StartsWith(Path.Combine(paths.DataRoot, "modpack-backups"), backup);
            Assert.True(File.Exists(backup));
            Assert.Equal(instance, packages.LastExportSource);
            Assert.Equal(backup, ModpackManifestStore.Read(instance)?.LastBackup);
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
            catch (Exception)
            {
                // 临时目录清理失败不影响测试结论。
            }
        }

        private sealed class FakeDownload : DownloadServiceStub
        {
            private readonly string _body;

            public FakeDownload(string body)
            {
                _body = body;
            }

            public string LastUrl { get; private set; } = string.Empty;

            public override Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
            {
                LastUrl = url;
                return Task.FromResult(_body);
            }
        }

        private sealed class FakeFiles : IFileDownloader
        {
            private readonly string _dependency;
            private readonly string _newer;
            private readonly string _older;

            public FakeFiles(
                string dependency = "fabric-loader",
                string newer = "0.19.3",
                string older = "0.16.10")
            {
                _dependency = dependency;
                _newer = newer;
                _older = older;
            }

            public Task DownloadAsync(
                string url, string targetPath, CancellationToken cancellationToken)
                => DownloadAsync(url, targetPath, null, cancellationToken);

            public Task DownloadAsync(
                string url, string targetPath, IProgress<double>? progress,
                CancellationToken cancellationToken)
            {
                string loader = url.Contains("v3") ? _newer : _older;
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using (FileStream stream = File.Create(targetPath))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = archive.CreateEntry("modrinth.index.json");
                    using (StreamWriter writer = new StreamWriter(entry.Open()))
                    {
                        writer.Write("{\"dependencies\":{\"" + _dependency + "\":\"" + loader + "\"}}");
                    }
                }

                return Task.CompletedTask;
            }

            public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
                => Task.FromResult("{}");
        }

        private sealed class FakePackages : IInstancePackageService
        {
            public string LastExportSource { get; private set; } = string.Empty;

            public Task<string> ExportAsync(
                string instancePath, string destinationZip, CancellationToken cancellationToken = default)
            {
                LastExportSource = instancePath;
                Directory.CreateDirectory(Path.GetDirectoryName(destinationZip)!);
                File.WriteAllText(destinationZip, "backup");
                return Task.FromResult(destinationZip);
            }

            public Task<string> ImportAsync(
                string packageZip, string versionsDirectory, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("本测试不导入实例包。");
            }

            public Task RestoreAsync(
                string packageZip, string instanceDirectory, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("本测试不回滚实例包。");
            }
        }
    }
}

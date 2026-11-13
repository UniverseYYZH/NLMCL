using System;
using System.Collections.Generic;
using System.IO;
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
                ""files"": [ { ""url"": ""https://cdn/v2.mrpack"", ""filename"": ""v2.mrpack"", ""primary"": true } ] }
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
        public async Task Backup_creates_a_zip_under_the_modpack_backup_directory()
        {
            string instance = Path.Combine(_root, "versions", "Backup Pack");
            Directory.CreateDirectory(instance);

            FakePackages packages = new FakePackages();
            AppPaths paths = new AppPaths(Path.Combine(_root, "data"), portable: true);
            ModpackUpdateService service = new ModpackUpdateService(
                new FakeDownload("[]"), packages, paths);

            string backup = await service.BackupAsync(instance, CancellationToken.None);

            Assert.StartsWith(Path.Combine(paths.DataRoot, "modpack-backups"), backup);
            Assert.True(File.Exists(backup));
            Assert.Equal(instance, packages.LastExportSource);
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
        }
    }
}

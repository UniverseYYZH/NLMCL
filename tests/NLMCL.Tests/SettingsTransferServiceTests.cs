using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;
using NLMCL.Core;
using NLMCL.Core.Settings;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>设置导入导出：只带走可移植字段，不覆盖本机路径与账号。</summary>
    public sealed class SettingsTransferServiceTests
    {
        [Fact]
        public async Task Export_excludes_machine_specific_fields_and_import_preserves_them()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-transfer-" + Guid.NewGuid().ToString("N"));
            AppPaths exportPaths = new AppPaths(Path.Combine(root, "export"));
            AppPaths importPaths = new AppPaths(Path.Combine(root, "import"));
            string file = Path.Combine(root, "settings.json");

            try
            {
                JsonAppSettingsStore exportStore = new JsonAppSettingsStore(exportPaths);
                await exportStore.SaveAsync(new AppSettings
                {
                    GameDirectory = @"C:\old\.minecraft",
                    MicrosoftClientId = "old-client",
                    DefaultJavaPath = @"C:\old\java\javaw.exe",
                    WizardCompleted = true,
                    Theme = "dark",
                    UiScale = 1.2,
                    UiScaleMaximized = 1.8,
                    DownloadThreads = 8,
                    DefaultMemoryMb = 6144,
                    UseMirror = false,
                    DefaultJvmArgs = "-XX:+UseG1GC",
                    DefaultGameArgs = "--demo"
                }, CancellationToken.None);

                SettingsTransferService export = new SettingsTransferService(
                    new AppSettingsService(exportStore));
                await export.ExportAsync(file, CancellationToken.None);

                JObject document = JObject.Parse(File.ReadAllText(file));
                JObject settings = (JObject)document["settings"]!;

                Assert.Null(settings["gameDirectory"]);
                Assert.Null(settings["microsoftClientId"]);
                Assert.Null(settings["defaultJavaPath"]);
                Assert.Null(settings["wizardCompleted"]);
                Assert.Equal("dark", settings.Value<string>("theme"));
                Assert.Equal("--demo", settings.Value<string>("defaultGameArgs"));

                JsonAppSettingsStore importStore = new JsonAppSettingsStore(importPaths);
                await importStore.SaveAsync(new AppSettings
                {
                    GameDirectory = @"D:\local\.minecraft",
                    MicrosoftClientId = "local-client",
                    DefaultJavaPath = @"D:\local\java\javaw.exe",
                    WizardCompleted = true
                }, CancellationToken.None);

                SettingsTransferService import = new SettingsTransferService(
                    new AppSettingsService(importStore));
                SettingsImportResult result = await import.ImportAsync(file, CancellationToken.None);

                AppSettings loaded = await importStore.LoadAsync(CancellationToken.None);

                Assert.Equal(@"D:\local\.minecraft", loaded.GameDirectory);
                Assert.Equal("local-client", loaded.MicrosoftClientId);
                Assert.Equal(@"D:\local\java\javaw.exe", loaded.DefaultJavaPath);
                Assert.True(loaded.WizardCompleted);

                Assert.Equal("dark", loaded.Theme);
                Assert.Equal(1.2, loaded.UiScale, 3);
                Assert.Equal(1.8, loaded.UiScaleMaximized, 3);
                Assert.Equal(8, loaded.DownloadThreads);
                Assert.Equal(6144, loaded.DefaultMemoryMb);
                Assert.False(loaded.UseMirror);
                Assert.Equal("-XX:+UseG1GC", loaded.DefaultJvmArgs);
                Assert.Equal("--demo", loaded.DefaultGameArgs);

                Assert.Contains("主题", result.AppliedFields);
                Assert.Empty(result.Warnings);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Import_clamps_invalid_ranges_and_reports_warnings()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-transfer-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(Path.Combine(root, "app"));
            string file = Path.Combine(root, "settings.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(file,
                    "{\"schema\":\"nlmcl-settings\",\"version\":1,\"settings\":{" +
                    "\"uiScale\":99,\"uiScaleMaximized\":0.1,\"downloadThreads\":0," +
                    "\"defaultMemoryMb\":1}}");

                JsonAppSettingsStore store = new JsonAppSettingsStore(paths);
                SettingsTransferService import = new SettingsTransferService(
                    new AppSettingsService(store));

                SettingsImportResult result = await import.ImportAsync(file, CancellationToken.None);
                AppSettings loaded = await store.LoadAsync(CancellationToken.None);

                Assert.Equal(1.5, loaded.UiScale, 3);
                Assert.Equal(1.0, loaded.UiScaleMaximized, 3);
                Assert.Equal(1, loaded.DownloadThreads);
                Assert.Equal(512, loaded.DefaultMemoryMb);
                Assert.NotEmpty(result.Warnings);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public async Task Import_rejects_non_nlmcl_json()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-transfer-" + Guid.NewGuid().ToString("N"));
            AppPaths paths = new AppPaths(Path.Combine(root, "app"));
            string file = Path.Combine(root, "settings.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(file, "{\"settings\":{}}");

                SettingsTransferService import = new SettingsTransferService(
                    new AppSettingsService(new JsonAppSettingsStore(paths)));

                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    import.ImportAsync(file, CancellationToken.None));
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static void TryDelete(string root)
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}

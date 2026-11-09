using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Maintenance;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>全盘 .minecraft 扫描：递归、深度、跳过系统目录。</summary>
    public sealed class GameDirectoryScannerTests
    {
        [Fact]
        public void Finds_nested_minecraft_directories_and_skips_temp()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-scan-" + Guid.NewGuid().ToString("N"));

            try
            {
                string first = Path.Combine(root, "games", ".minecraft");
                string second = Path.Combine(root, "one", "two", ".minecraft");
                string skipped = Path.Combine(root, "Temp", ".minecraft");

                Directory.CreateDirectory(first);
                Directory.CreateDirectory(second);
                Directory.CreateDirectory(skipped);

                List<string> found = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                GameDirectoryScanner.ScanRoot(
                    root, 6, 500, found, seen, CancellationToken.None);

                Assert.Contains(first, found);
                Assert.Contains(second, found);
                Assert.DoesNotContain(skipped, found);
            }
            finally
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
        [Fact]
        public async Task Reports_progress_and_found_directory()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-progress-" + Guid.NewGuid().ToString("N"));

            try
            {
                string minecraft = Path.Combine(root, "game", ".minecraft");
                Directory.CreateDirectory(minecraft);

                GameDirectoryScanner scanner = new GameDirectoryScanner(new[] { root }, 3, 500);
                CaptureProgress progress = new CaptureProgress();

                IReadOnlyList<string> found = await scanner.ScanAsync(progress, CancellationToken.None);

                Assert.Contains(minecraft, found);
                Assert.Contains(progress.Items, item => item.FoundDirectory == minecraft);
                Assert.Contains(progress.Items, item => item.ScannedDirectories > 0);
            }
            finally
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

        [Fact]
        public async Task Cancellation_stops_scan()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();

                GameDirectoryScanner scanner = new GameDirectoryScanner(
                    new[] { Path.GetTempPath() }, 2, 500);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => scanner.ScanAsync(cts.Token));
            }
        }

        private sealed class CaptureProgress : IProgress<GameDirectoryScanProgress>
        {
            public List<GameDirectoryScanProgress> Items { get; } = new List<GameDirectoryScanProgress>();

            public void Report(GameDirectoryScanProgress value)
            {
                Items.Add(value);
            }
        }
    }
}

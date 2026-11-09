using System;
using System.IO;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class DownloadDiagnosticsTests
    {
        [Fact]
        public void Rotates_log_and_keeps_three_backups()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "nlmcl-download.log");
            string big = new string('x', 2 * 1024 * 1024 + 32);

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    DownloadDiagnostics.WriteTo(path, big + i);
                }

                Assert.True(File.Exists(path));
                Assert.True(File.Exists(path + ".1"));
                Assert.True(File.Exists(path + ".2"));
                Assert.True(File.Exists(path + ".3"));
                Assert.False(File.Exists(path + ".4"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}

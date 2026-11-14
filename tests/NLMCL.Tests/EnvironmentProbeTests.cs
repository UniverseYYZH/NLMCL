using System;
using System.IO;
using NLMCL.Core.Runtime;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class EnvironmentProbeTests
    {
        [Fact]
        public void Can_write_an_existing_directory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "nlmcl-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Assert.True(EnvironmentProbe.CanWriteDirectory(directory));
            }
            finally
            {
                TryDelete(directory);
            }
        }

        [Fact]
        public void Cannot_write_when_the_path_is_a_file()
        {
            string file = Path.Combine(Path.GetTempPath(), "nlmcl-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(file, "x");
            try
            {
                Assert.False(EnvironmentProbe.CanWriteDirectory(file));
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        private static void TryDelete(string directory)
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}

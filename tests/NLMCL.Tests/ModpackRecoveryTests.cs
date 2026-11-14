using System;
using System.IO;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ModpackRecoveryTests : IDisposable
    {
        private readonly string _root;

        public ModpackRecoveryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "nlmcl-modpack-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "versions"));
        }

        [Fact]
        public void Restores_the_old_instance_when_the_swap_was_interrupted()
        {
            string versions = Path.Combine(_root, "versions");
            string old = Path.Combine(versions, "Pack.nlmcl-old-deadbeef");
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "marker.txt"), "old");

            int recovered = ModpackInstaller.RecoverInterruptedUpdates(versions);

            Assert.Equal(1, recovered);
            Assert.True(Directory.Exists(Path.Combine(versions, "Pack")));
            Assert.True(File.Exists(Path.Combine(versions, "Pack", "marker.txt")));
            Assert.False(Directory.Exists(old));
        }

        [Fact]
        public void Removes_stale_old_and_staging_directories_when_the_instance_exists()
        {
            string versions = Path.Combine(_root, "versions");
            string instance = Path.Combine(versions, "Pack");
            Directory.CreateDirectory(instance);
            File.WriteAllText(Path.Combine(instance, "marker.txt"), "new");
            Directory.CreateDirectory(Path.Combine(versions, "Pack.nlmcl-old-deadbeef"));
            Directory.CreateDirectory(Path.Combine(versions, "Pack.nlmcl-staging-deadbeef"));

            int recovered = ModpackInstaller.RecoverInterruptedUpdates(versions);

            Assert.Equal(0, recovered);
            Assert.True(File.Exists(Path.Combine(instance, "marker.txt")));
            Assert.False(Directory.Exists(Path.Combine(versions, "Pack.nlmcl-old-deadbeef")));
            Assert.False(Directory.Exists(Path.Combine(versions, "Pack.nlmcl-staging-deadbeef")));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}

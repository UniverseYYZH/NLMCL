using System.Collections.Generic;
using NLMCL.Abstractions;
using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>AutoModpack 版本选择：必须和服务端的 v4/v5 对得上。</summary>
    public class AutoModpackSelectorTests
    {
        private static ResourceFile File(string version, string name) =>
            new ResourceFile(name, "https://example.org/" + name, 1000, version, new[] { "1.21.1" }, "fabric");

        private static List<ResourceFile> LatestFirst() => new List<ResourceFile>
        {
            File("5.0.0-rc.7", "automodpack-5.0.0-rc.7.jar"),
            File("5.0.0-rc.6", "automodpack-5.0.0-rc.6.jar"),
            File("4.0.7", "automodpack-mc1.21.1-fabric-4.0.7.jar"),
            File("4.0.6", "automodpack-mc1.21.1-fabric-4.0.6.jar"),
            File("4.0.5", "automodpack-mc1.21.1-fabric-4.0.5.jar")
        };

        [Fact]
        public void Pinned_version_wins_over_latest()
        {
            ResourceFile? picked = AutoModpackSelector.Pick(LatestFirst(), "4.0.6");

            Assert.NotNull(picked);
            Assert.Equal("4.0.6", picked!.VersionName);
        }

        [Fact]
        public void Falls_back_to_same_major_when_exact_missing()
        {
            List<ResourceFile> files = new List<ResourceFile>
            {
                File("5.0.0-rc.7", "automodpack-5.0.0-rc.7.jar"),
                File("4.0.7", "automodpack-mc1.21.1-fabric-4.0.7.jar")
            };

            ResourceFile? picked = AutoModpackSelector.Pick(files, "4.0.6");

            Assert.NotNull(picked);
            Assert.Equal("4.0.7", picked!.VersionName);
        }

        [Fact]
        public void Latest_is_used_when_nothing_pinned()
        {
            ResourceFile? picked = AutoModpackSelector.Pick(LatestFirst(), null);

            Assert.NotNull(picked);
            Assert.Equal("5.0.0-rc.7", picked!.VersionName);
        }
    }
}

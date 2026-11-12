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
        public void Pinned_major_absent_returns_null_instead_of_installing_latest()
        {
            List<ResourceFile> files = new List<ResourceFile>
            {
                File("5.0.0-rc.7", "automodpack-5.0.0-rc.7.jar"),
                File("5.0.0-rc.6", "automodpack-5.0.0-rc.6.jar")
            };

            ResourceFile? picked = AutoModpackSelector.Pick(files, "4.0.6");

            Assert.Null(picked);
        }

        [Fact]
        public void Latest_is_used_when_nothing_pinned()
        {
            ResourceFile? picked = AutoModpackSelector.Pick(LatestFirst(), null);

            Assert.NotNull(picked);
            Assert.Equal("5.0.0-rc.7", picked!.VersionName);
        }

        [Fact]
        public void Installed_jar_matches_the_pinned_exact_version()
        {
            Assert.True(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.0.6.jar", "4.0.6"));
        }

        [Fact]
        public void Installed_jar_does_not_match_a_different_major_version()
        {
            Assert.False(AutoModpackSelector.MatchesVersion(
                "automodpack-5.0.0-rc.7.jar", "4.0.6"));
            Assert.False(AutoModpackSelector.MatchesVersion(
                "automodpack-5.0.0-rc.7.jar", "4"));
        }

        [Fact]
        public void Installed_jar_matches_a_pinned_major_version()
        {
            Assert.True(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.0.7.jar", "4"));
        }

        [Fact]
        public void Installed_jar_matches_a_later_patch_of_the_same_major()
        {
            // Pick 在精确 4.0.6 缺失时会落到同主版本 4.0.7；界面检查必须认这个结果，
            // 否则会反复提示“版本不匹配”。4.0.60 是同一主版本里更高的补丁，不能因
            // 字符串包含关系误判成 4.0.6，但作为同主版本兜底仍是可接受的。
            Assert.True(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.0.7.jar", "4.0.6"));
            Assert.True(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.0.60.jar", "4.0.6"));
            Assert.False(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-14.0.6.jar", "4.0.6"));
        }

        [Fact]
        public void Picked_jar_is_reported_as_installed()
        {
            List<ResourceFile> files = new List<ResourceFile>
            {
                File("5.0.0-rc.7", "automodpack-5.0.0-rc.7.jar"),
                File("4.0.7", "automodpack-mc1.21.1-fabric-4.0.7.jar")
            };

            ResourceFile? picked = AutoModpackSelector.Pick(files, "4.0.6");

            Assert.NotNull(picked);
            Assert.True(AutoModpackSelector.MatchesVersion(
                picked!.Name, "4.0.6"), "Pick 选出的 jar 必须被 MatchesVersion 认作已安装");
        }

        [Fact]
        public void Installed_jar_does_not_match_a_version_substring_of_a_larger_number()
        {
            Assert.False(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-14.0.6.jar", "4.0.6"));
        }

        [Fact]
        public void Installed_jar_matches_a_pinned_major_minor_version()
        {
            Assert.True(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.0.7.jar", "4.0"));
            Assert.True(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.0.60.jar", "4.0"));
            Assert.False(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-4.1.0.jar", "4.0"));
            Assert.False(AutoModpackSelector.MatchesVersion(
                "automodpack-mc1.21.1-fabric-14.0.7.jar", "4.0"));
        }

        [Fact]
        public void Pinned_major_minor_does_not_fall_back_to_another_minor()
        {
            List<ResourceFile> files = new List<ResourceFile>
            {
                File("4.1.0", "automodpack-mc1.21.1-fabric-4.1.0.jar"),
                File("5.0.0-rc.7", "automodpack-5.0.0-rc.7.jar")
            };

            Assert.Null(AutoModpackSelector.Pick(files, "4.0"));
        }
    }
}

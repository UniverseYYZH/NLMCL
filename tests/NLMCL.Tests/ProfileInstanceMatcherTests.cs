using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>版本目录匹配：1.21.1 不能命中 1.21.10。</summary>
    public class ProfileInstanceMatcherTests
    {
        [Theory]
        [InlineData("fabric-loader-0.20.1-1.21.1", "1.21.1", "fabric", true)]
        [InlineData("fabric-loader-0.16.9-1.21.10", "1.21.1", "fabric", false)]
        [InlineData("fabric-loader-0.16.9-1.21.10", "1.21.10", "fabric", true)]
        [InlineData("1.21.1", "1.21.1", "", true)]
        [InlineData("1.21.1-forge-47.4.28", "1.21.1", "fabric", false)]
        [InlineData("fabric-loader-0.20.1-1.21.2", "1.21.1", "fabric", false)]
        public void Matches_version_by_suffix(string folder, string minecraft, string loader, bool expected)
        {
            Assert.Equal(expected, ProfileInstanceMatcher.Matches(folder, minecraft, loader));
        }

        [Fact]
        public void Empty_inputs_do_not_match()
        {
            Assert.False(ProfileInstanceMatcher.Matches(null, "1.21.1", "fabric"));
            Assert.False(ProfileInstanceMatcher.Matches("fabric-loader-0.20.1-1.21.1", "", "fabric"));
        }
    }
}

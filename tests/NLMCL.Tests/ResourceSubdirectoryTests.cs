using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class ResourceSubdirectoryTests
    {
        [Theory]
        [InlineData(ResourceKind.Mod, "mods")]
        [InlineData(ResourceKind.ResourcePack, "resourcepacks")]
        [InlineData(ResourceKind.ShaderPack, "shaderpacks")]
        [InlineData(ResourceKind.DataPack, "datapacks")]
        public void Kind_maps_to_the_folder_the_game_reads(ResourceKind kind, string expected)
        {
            Assert.Equal(expected, ResourceViewModel.SubdirectoryFor(kind));
        }
    }
}
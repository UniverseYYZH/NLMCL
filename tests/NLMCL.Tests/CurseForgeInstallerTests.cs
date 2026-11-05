using System;
using Newtonsoft.Json.Linq;
using NLMCL.Core.Resources;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class CurseForgeInstallerTests
    {
        [Theory]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei", "jei")]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei/files/123456", "jei")]
        [InlineData("https://legacy.curseforge.com/minecraft/mc-mods/the-shooting-star", "the-shooting-star")]
        [InlineData("the-shooting-star", "the-shooting-star")]
        public void Slug_is_extracted_from_url_or_plain_slug(string input, string expected)
        {
            Assert.Equal(expected, CurseForgeInstaller.ExtractSlug(input));
        }

        [Theory]
        [InlineData("https://www.curseforge.com/minecraft/mc-mods/jei", "mc-mods")]
        [InlineData("https://www.curseforge.com/minecraft/modpacks/the-shooting-star", "modpacks")]
        [InlineData("https://www.curseforge.com/minecraft/texture-packs/faithful-32x", "texture-packs")]
        [InlineData("jei", "mc-mods")]
        public void Project_type_is_detected_from_url(string input, string expected)
        {
            Assert.Equal(expected, CurseForgeInstaller.ExtractType(input));
        }

        [Fact]
        public void Direct_download_url_follows_forgecdn_sharding()
        {
            Assert.Equal(
                "https://mediafilez.forgecdn.net/files/9407/746/jei.jar",
                CurseForgeInstaller.BuildDownloadUrl(9407746, "jei.jar"));
        }

        [Fact]
        public void File_picker_prefers_matching_version_and_loader()
        {
            JArray files = JArray.Parse(@"[
                {""id"":1,""name"":""a.jar"",""versions"":[""Forge"",""1.20.1""]},
                {""id"":2,""name"":""b.jar"",""versions"":[""Fabric"",""1.21.1""]},
                {""id"":3,""name"":""c.jar"",""versions"":[""Fabric"",""1.20.1""]}
            ]");

            JObject? picked = CurseForgeInstaller.PickFile(files, "1.21.1", "fabric");
            Assert.NotNull(picked);
            Assert.Equal(2, picked!.Value<int>("id"));

            // 版本对但加载器不对时，退回"只按版本"
            JObject? fallback = CurseForgeInstaller.PickFile(files, "1.20.1", "fabric");
            Assert.NotNull(fallback);
            Assert.Equal(3, fallback!.Value<int>("id"));
        }
    }
}
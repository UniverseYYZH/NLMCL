using NLMCL.Core.Logging;
using Xunit;

namespace NLMCL.Tests
{
    public class LogClassifierTests
    {
        [Theory]
        [InlineData("java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main has been compiled by a more recent version")]
        [InlineData("This mod requires Java 21 or newer")]
        [InlineData("Java 版本不兼容，请升级到 Java 17")]
        public void Detects_java_incompatibility(string line)
        {
            Assert.Equal(LogTag.JavaIncompatible, LogClassifier.Classify(line));
        }

        [Theory]
        [InlineData("Mod Resolution encountered an error")]
        [InlineData("Missing or unsupported mandatory dependencies: modid")]
        [InlineData("模组冲突：两个 mod 都注册了 minecraft:stone")]
        [InlineData("org.spongepowered.asm.mixin.transformer.MixinTransformerError: Critical injection failure")]
        public void Detects_mod_conflicts(string line)
        {
            Assert.Equal(LogTag.ModConflict, LogClassifier.Classify(line));
        }

        [Theory]
        [InlineData("Failed to load texture minecraft:textures/block/stone.png")]
        [InlineData("Unable to load model minecraft:block/furnace")]
        [InlineData("无法加载纹理：assets/textures/gui/icon.png")]
        public void Detects_resource_failures(string line)
        {
            Assert.Equal(LogTag.ResourceFailure, LogClassifier.Classify(line));
        }

        [Theory]
        [InlineData("[Server thread/INFO] Starting minecraft server version 1.21.1")]
        [InlineData("[Render thread/INFO] OpenGL debug message")]
        [InlineData("")]
        public void Normal_lines_stay_normal(string line)
        {
            Assert.Equal(LogTag.Normal, LogClassifier.Classify(line));
        }

        [Fact]
        public void Java_wins_over_mod_conflict_because_it_cascades()
        {
            // 一个 Java 版本错误会连带刷出一堆模组加载失败；先标 Java 才不误导用户。
            const string line = "java.lang.UnsupportedClassVersionError while loading mod resolution";

            Assert.Equal(LogTag.JavaIncompatible, LogClassifier.Classify(line));
        }

        [Fact]
        public void Labels_and_colors_are_stable_for_ui()
        {
            Assert.Equal("Java 不兼容", LogClassifier.LabelOf(LogTag.JavaIncompatible));
            Assert.Equal("模组冲突", LogClassifier.LabelOf(LogTag.ModConflict));
            Assert.Equal("资源加载失败", LogClassifier.LabelOf(LogTag.ResourceFailure));
            Assert.Equal("正常信息", LogClassifier.LabelOf(LogTag.Normal));

            // 配色写死给界面用，改动需要同步 XAML
            Assert.Equal("#C79BFF", LogClassifier.ColorOf(LogTag.JavaIncompatible));
            Assert.Equal("#FF8A7A", LogClassifier.ColorOf(LogTag.ModConflict));
        }
    }
}

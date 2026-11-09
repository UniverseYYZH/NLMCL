using System;
using System.IO;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core.Maintenance;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class VersionDetailsTests
    {
        [Fact]
        public void Reads_json_jar_java_and_mods()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-details-" + Guid.NewGuid().ToString("N"));
            string minecraft = Path.Combine(root, ".minecraft");
            string version = Path.Combine(minecraft, "versions", "1.20.1");

            try
            {
                Directory.CreateDirectory(Path.Combine(version, "mods"));
                File.WriteAllText(Path.Combine(version, "1.20.1.json"),
                    "{\"id\":\"1.20.1\",\"mainClass\":\"net.minecraft.client.main.Main\"," +
                    "\"assets\":\"5\",\"inheritsFrom\":\"1.20\"," +
                    "\"javaVersion\":{\"majorVersion\":17}," +
                    "\"libraries\":[{\"name\":\"a\"},{\"name\":\"b\"}]}");
                File.WriteAllBytes(Path.Combine(version, "1.20.1.jar"), new byte[2048]);
                File.WriteAllBytes(Path.Combine(version, "mods", "a.jar"), Array.Empty<byte>());
                File.WriteAllBytes(Path.Combine(version, "mods", "b.jar.disabled"), Array.Empty<byte>());
                File.WriteAllText(Path.Combine(version, "mods", "readme.txt"), "x");

                VersionDetailsData data = new VersionDetailsService().Read(minecraft, "1.20.1");

                Assert.True(data.VersionJsonExists);
                Assert.True(data.ClientJarExists);
                Assert.Equal(2048, data.ClientJarSize);
                Assert.Equal("net.minecraft.client.main.Main", data.MainClass);
                Assert.Equal("5", data.Assets);
                Assert.Equal("1.20", data.InheritsFrom);
                Assert.Equal(17, data.JavaMajor);
                Assert.Equal(2, data.LibraryCount);
                Assert.Equal(2, data.ModCount);
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
        public void Show_version_details_populates_lines()
        {
            string root = Path.Combine(Path.GetTempPath(), "nlmcl-details-vm-" + Guid.NewGuid().ToString("N"));
            string minecraft = Path.Combine(root, ".minecraft");
            string version = Path.Combine(minecraft, "versions", "1.20.1");

            try
            {
                Directory.CreateDirectory(version);
                File.WriteAllText(Path.Combine(version, "1.20.1.json"),
                    "{\"id\":\"1.20.1\",\"mainClass\":\"net.minecraft.client.main.Main\"," +
                    "\"libraries\":[]}");

                MainViewModel vm = TestSupport.CreateMain(
                    new TestSupport.EmptyCatalog(),
                    new NLMCL.Core.AppPaths(root),
                    versionDetails: new VersionDetailsService());

                VersionItem item = new VersionItem(
                    new MinecraftVersion("1.20.1", "1.20.1", VersionKind.Release, DateTimeOffset.Now),
                    false,
                    minecraft);

                vm.ShowVersionDetailsCommand.Execute(item);

                Assert.True(vm.IsVersionDetailsOpen);
                Assert.Contains(vm.VersionDetailsLines, line => line.Label == "版本名称");
                Assert.Contains(vm.VersionDetailsLines, line =>
                    line.Label == "主类" && line.Value == "net.minecraft.client.main.Main");
                Assert.Contains(vm.VersionDetailsLines, line => line.Label == "实例内存");

                vm.CloseVersionDetailsCommand.Execute(null);
                Assert.False(vm.IsVersionDetailsOpen);
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
    }
}

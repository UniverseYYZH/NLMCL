using System;
using System.IO;
using System.Text;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 手填 Java 路径时，版本号必须能读出来——否则 PCL 会按 Java 17 生成参数，
    /// 给真正的 Java 8 加上 -XX:+UseZGC，JVM 直接拒绝启动。
    /// </summary>
    public class JavaVersionProbeTests
    {
        [Theory]
        [InlineData("1.8.0_411", 8)]
        [InlineData("1.8.0_202", 8)]
        [InlineData("17.0.9", 17)]
        [InlineData("21", 21)]
        [InlineData("21-ea", 21)]
        [InlineData("25+12", 25)]
        [InlineData("\"17.0.9\"", 17)]
        public void Parses_release_version_strings(string raw, int expectedMajor)
        {
            Version? version = JavaVersionProbe.Parse(raw);

            Assert.NotNull(version);
            Assert.Equal(expectedMajor, version!.Major);
        }

        [Fact]
        public void Returns_null_for_garbage()
        {
            Assert.Null(JavaVersionProbe.Parse("not-a-version"));
            Assert.Null(JavaVersionProbe.Parse(""));
            Assert.Null(JavaVersionProbe.Parse(null));
        }

        [Fact]
        public void Reads_version_from_release_file()
        {
            string home = Path.Combine(Path.GetTempPath(), "nlmcl-release-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(home, "bin"));
            string executable = Path.Combine(home, "bin", "javaw.exe");
            File.WriteAllBytes(executable, Array.Empty<byte>());
            File.WriteAllText(
                Path.Combine(home, "release"),
                "IMPLEMENTOR=\"Eclipse Adoptium\"\nJAVA_VERSION=\"1.8.0_411\"\n",
                Encoding.ASCII);

            try
            {
                Version? version = JavaVersionProbe.TryRead(executable);

                Assert.NotNull(version);
                Assert.Equal(8, version!.Major);
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }
        }

        [Fact]
        public void Returns_null_when_release_file_is_missing()
        {
            string home = Path.Combine(Path.GetTempPath(), "nlmcl-norelease-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(home, "bin"));
            string executable = Path.Combine(home, "bin", "javaw.exe");
            File.WriteAllBytes(executable, Array.Empty<byte>());

            try
            {
                Assert.Null(JavaVersionProbe.TryRead(executable));
            }
            finally
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }
}

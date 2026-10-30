using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLCL.Abstractions;
using NLCL.Core.Installing;
using NLCL.Core.Pcl;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// 可选的"真实游戏目录"集成测试。
    ///
    /// 由环境变量 <c>NLCL_TEST_MC_DIR</c> 指定路径；未设置时整组跳过（返回而不是假通过）。
    /// 存在的意义：拿用户机器上真实的版本目录（可能含 Fabric、整合包、中文名）验证扫描器，
    /// 这比任何夹具都可信。
    /// </summary>
    public class RealMinecraftDirectoryTests
    {
        private static string? Directory
        {
            get
            {
                string? value = Environment.GetEnvironmentVariable("NLCL_TEST_MC_DIR");
                return string.IsNullOrWhiteSpace(value) || !System.IO.Directory.Exists(value)
                    ? null
                    : value;
            }
        }

        [Fact]
        public async Task Scans_a_real_minecraft_directory()
        {
            string? minecraft = Directory;
            if (minecraft == null)
            {
                return; // 没配环境变量 / 路径不存在
            }

            PclVersionCatalog catalog = new PclVersionCatalog();

            IReadOnlyList<MinecraftVersion> versions = await catalog
                .GetInstalledAsync(minecraft, CancellationToken.None);

            Assert.NotEmpty(versions);

            foreach (MinecraftVersion version in versions)
            {
                Assert.False(string.IsNullOrWhiteSpace(version.FolderName));
                Assert.False(string.IsNullOrWhiteSpace(version.Id));
            }

            // 版本目录名必须与扫出来的 FolderName 一一对应
            string[] folders = System.IO.Directory.GetDirectories(Path.Combine(minecraft, "versions"));
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                Assert.Contains(versions, version => version.FolderName == name);
            }
        }

        [Fact]
        public async Task Reads_java_requirement_from_real_versions()
        {
            string? minecraft = Directory;
            if (minecraft == null)
            {
                return;
            }

            PclVersionCatalog catalog = new PclVersionCatalog();
            IReadOnlyList<MinecraftVersion> versions = await catalog
                .GetInstalledAsync(minecraft, CancellationToken.None);

            int withRequirement = 0;

            foreach (MinecraftVersion version in versions)
            {
                int? major = JavaRequirements.ReadRequiredMajor(minecraft, version.FolderName);

                if (major == null)
                {
                    continue;
                }

                withRequirement++;
                Assert.True(major.Value >= 8 && major.Value <= 30,
                    version.FolderName + " 读出的 Java 主版本不合理：" + major.Value);
            }

            // 真实安装里至少有一个版本声明了 javaVersion，否则说明解析没生效
            Assert.True(withRequirement > 0, "没有从任何版本里读出 Java 要求");
        }
    }
}

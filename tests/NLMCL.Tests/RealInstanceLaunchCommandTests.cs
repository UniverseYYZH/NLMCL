using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLMCL.Core.Pcl.Vb;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 用**真实的已安装版本**构建启动命令行，并把它写到 artifacts 里供人工核对。
    ///
    /// 由 <c>NLMCL_TEST_MC_DIR</c> 与 <c>NLMCL_TEST_VERSION</c> 两个环境变量启用；
    /// 未设置时跳过（返回而不是假通过）。
    ///
    /// 只构建命令、不真的启动游戏——启动是用户点按钮的事。但命令对不对，
    /// 这里能查出绝大部分问题：classpath 是否指向存在的文件、natives 是否解压过、
    /// 占位符是否全部替换、主类是否正确。
    /// </summary>
    public class RealInstanceLaunchCommandTests
    {
        private static (string Directory, string Version)? Target
        {
            get
            {
                string? dir = Environment.GetEnvironmentVariable("NLMCL_TEST_MC_DIR");
                string? version = Environment.GetEnvironmentVariable("NLMCL_TEST_VERSION");

                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(version))
                {
                    return null;
                }

                if (!System.IO.Directory.Exists(Path.Combine(dir!, "versions", version!)))
                {
                    return null;
                }

                return (dir!, version!);
            }
        }

        [Fact]
        public void Builds_a_command_for_a_real_instance()
        {
            (string Directory, string Version)? target = Target;
            if (target == null)
            {
                return;
            }

            (string minecraft, string version) = target.Value;
            string versionFolder = Path.Combine(minecraft, "versions", version);

            McInstance instance = new McInstance(versionFolder);

            string command = PclLaunchCommandBuilder.BuildCommand(
                instance,
                JavaFolder: @"C:\jdk",
                JavaVersion: new Version(25, 0, 1),
                UserName: "Steve",
                Uuid: "00000000-0000-0000-0000-000000000000",
                AccessToken: "0");

            // 落盘，供人工核对
            string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            System.IO.Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "launch-command.txt"), command, Encoding.UTF8);

            Assert.False(string.IsNullOrWhiteSpace(command));
            Assert.DoesNotContain("${", command);

            // classpath 里每个条目都必须真实存在
            List<string> missing = new List<string>();
            int separator = command.IndexOf(" -cp ", StringComparison.Ordinal);
            if (separator < 0)
            {
                separator = command.IndexOf(" -classpath ", StringComparison.Ordinal);
            }

            Assert.True(separator >= 0, "命令行里没有 -cp：\n" + command);

            int start = separator + 5;
            int end = command.IndexOf('"', start + 1);
            string classPath = end > start
                ? command.Substring(start + 1, end - start - 1)
                : command.Substring(start).Split(' ')[0];

            foreach (string entry in classPath.Split(';'))
            {
                if (Path.IsPathRooted(entry) && !File.Exists(entry))
                {
                    missing.Add(entry);
                }
            }

            Assert.True(missing.Count == 0,
                "classpath 里有 " + missing.Count + " 个文件不存在：\n" + string.Join("\n", missing.Take(10)));

            // natives 目录必须存在且非空（否则 LWJGL 会崩）
            int nativesIndex = command.IndexOf("-Djava.library.path=", StringComparison.Ordinal);
            Assert.True(nativesIndex >= 0, "命令行里没有 -Djava.library.path：\n" + command);

            string nativesPath = command.Substring(nativesIndex + "-Djava.library.path=".Length);

            // 整个参数通常被引号包住（路径里有空格），先按引号取；没有引号才按空格取
            if (nativesPath.StartsWith("\"", StringComparison.Ordinal))
            {
                nativesPath = nativesPath.Substring(1, nativesPath.IndexOf('"', 1) - 1);
            }
            else
            {
                int quote = nativesPath.IndexOf('"');
                nativesPath = quote >= 0 ? nativesPath.Substring(0, quote) : nativesPath.Split(' ')[0];
            }

            // 值可能是分号分隔的多个目录（PCL 按库把 natives 解压到子目录）
            string[] parts = nativesPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.NotEmpty(parts);

            int dllCount = 0;
            foreach (string part in parts)
            {
                Assert.True(System.IO.Directory.Exists(part), "natives 目录不存在：" + part);
                dllCount += System.IO.Directory.GetFiles(part, "*.dll").Length;
            }

            Assert.True(dllCount > 0, "natives 目录里一个 dll 都没有");
        }
    }
}

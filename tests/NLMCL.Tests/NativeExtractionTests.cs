using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using NLMCL.Core.Pcl.Vb;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 回归：PCL 的启动流程在构建参数之前会先把 *-natives.jar 里的 dll 解压到
    /// &lt;版本&gt;-natives\。我们只切了「构建参数」，漏了这一步，
    /// 结果 -Djava.library.path 指向空目录，LWJGL 报 "Failed to locate library: lwjgl.dll"，
    /// Fabric + sodium 在 preLaunch 阶段直接崩。
    /// </summary>
    public class NativeExtractionTests : IDisposable
    {
        private readonly string _root;

        private const string VersionJson = @"{
            ""id"": ""1.21.1"",
            ""type"": ""release"",
            ""mainClass"": ""net.minecraft.client.main.Main"",
            ""assetIndex"": { ""id"": ""17"", ""url"": ""https://example.invalid/17.json"" },
            ""downloads"": { ""client"": { ""url"": ""https://example.invalid/client.jar"", ""sha1"": ""0000000000000000000000000000000000000000"", ""size"": 1 } },
            ""libraries"": [
                {
                    ""name"": ""org.lwjgl:lwjgl:3.3.3"",
                    ""downloads"": {
                        ""artifact"": {
                            ""path"": ""org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar"",
                            ""url"": ""https://example.invalid/lwjgl.jar"",
                            ""sha1"": ""0000000000000000000000000000000000000000"",
                            ""size"": 1
                        },
                        ""classifiers"": {
                            ""natives-windows"": {
                                ""path"": ""org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar"",
                                ""url"": ""https://example.invalid/lwjgl-natives.jar"",
                                ""sha1"": ""0000000000000000000000000000000000000000"",
                                ""size"": 1
                            }
                        }
                    },
                    ""natives"": { ""windows"": ""natives-windows"" }
                }
            ]
        }";

        public NativeExtractionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "nlmcl-natives-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static void RunSta(Action action)
        {
            Exception? failure = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA 线程超时。");

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>造一个只有 natives 归档的最小安装，然后跑 PCL 的解压逻辑。</summary>
        [Fact]
        public void Extracts_native_dlls_next_to_the_version()
        {
            string versionDirectory = Path.Combine(_root, "versions", "1.21.1");
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(
                Path.Combine(versionDirectory, "1.21.1.json"), VersionJson, Encoding.UTF8);

            string jarPath = Path.Combine(
                _root, "libraries", "org", "lwjgl", "lwjgl", "3.3.3",
                "lwjgl-3.3.3-natives-windows.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
            WriteZipWithEntry(jarPath, "lwjgl.dll", new byte[] { 1, 2, 3, 4 });

            RunSta(() =>
            {
                McInstance instance = new McInstance(versionDirectory);
                instance.Check();

                PclLaunchCommandBuilder.PrepareNatives(instance);
            });

            string extracted = Path.Combine(
                versionDirectory, "1.21.1-natives", "lwjgl.dll");

            Assert.True(File.Exists(extracted), "natives 没有被解压到 <版本>-natives\\：" + extracted);
            Assert.Equal(4, new FileInfo(extracted).Length);
        }

        private static void WriteZipWithEntry(string path, string entryName, byte[] content)
        {
            using (FileStream stream = File.Create(path))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                using (Stream output = entry.Open())
                {
                    output.Write(content, 0, content.Length);
                }
            }
        }

        /// <summary>
        /// 新格式（1.19+）：natives 是独立库（name 以 <c>:natives-windows</c> 结尾），
        /// 没有传统的 <c>natives.windows</c> 字段。PCL 不认这种，需要我们自己平铺解压，
        /// 否则 <c>-Djava.library.path</c> 指向空目录，LWJGL 报 lwjgl.dll 找不到。
        /// </summary>
        [Fact]
        public void Extracts_modern_format_natives_flat()
        {
            string minecraft = Path.Combine(_root, "modern");
            string versionDirectory = Path.Combine(minecraft, "versions", "1.20.1");
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(Path.Combine(versionDirectory, "1.20.1.json"), ModernFormatJson, Encoding.UTF8);

            string jarPath = Path.Combine(
                minecraft, "libraries", "org", "lwjgl", "lwjgl", "3.3.1",
                "lwjgl-3.3.1-natives-windows.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
            WriteZipWithEntry(jarPath, "windows/x64/org/lwjgl/lwjgl.dll", new byte[] { 9, 8, 7 });

            RunSta(() =>
            {
                McInstance instance = new McInstance(versionDirectory);
                instance.Check();

                PclLaunchCommandBuilder.PrepareNatives(instance);
            });

            // 平铺：jar 里的 windows/x64/org/lwjgl/ 层级不能保留，否则 dll 的依赖找不到
            string extracted = Path.Combine(versionDirectory, "1.20.1-natives", "lwjgl.dll");
            Assert.True(File.Exists(extracted), "新格式 natives 没有解压出来：" + extracted);
            Assert.Equal(3, new FileInfo(extracted).Length);
        }

        private const string ModernFormatJson = @"{
            ""id"": ""1.20.1"",
            ""type"": ""release"",
            ""mainClass"": ""net.minecraft.client.main.Main"",
            ""libraries"": [
                {
                    ""name"": ""org.lwjgl:lwjgl:3.3.1:natives-windows"",
                    ""rules"": [ { ""action"": ""allow"", ""os"": { ""name"": ""windows"" } } ],
                    ""downloads"": {
                        ""artifact"": {
                            ""path"": ""org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar"",
                            ""url"": ""https://example.invalid/n.jar"",
                            ""sha1"": ""0000000000000000000000000000000000000000"",
                            ""size"": 1
                        }
                    }
                }
            ]
        }";

        /// <summary>
        /// 可选的真实实例诊断：设置 NLMCL_TEST_MC_DIR 与 NLMCL_TEST_VERSION 后运行，
        /// 它会列出该版本所有 natives 条目、文件是否存在、以及最终解压结果。
        /// 用来回答"为什么真实实例的 -natives 目录还是空的"。
        /// </summary>
        [Fact]
        public void Diagnose_real_instance_natives()
        {
            string? minecraft = Environment.GetEnvironmentVariable("NLMCL_TEST_MC_DIR");
            string? version = Environment.GetEnvironmentVariable("NLMCL_TEST_VERSION");
            if (string.IsNullOrWhiteSpace(minecraft) || string.IsNullOrWhiteSpace(version))
            {
                return; // 没配环境变量：跳过
            }

            string versionDirectory = Path.Combine(minecraft, "versions", version);
            Assert.True(Directory.Exists(versionDirectory), "版本目录不存在：" + versionDirectory);

            string nativesFolder = Path.Combine(versionDirectory, version + "-natives");
            StringBuilder report = new StringBuilder();
            report.AppendLine("版本目录: " + versionDirectory);
            report.AppendLine("natives 目录: " + nativesFolder);
            report.AppendLine();

            RunSta(() =>
            {
                McInstance instance = new McInstance(versionDirectory);
                instance.Check();

                PclLaunchCommandBuilder.PrepareNatives(instance);

                System.Collections.Generic.List<McLibEntry> libraries =
                    ModMinecraftHelpers.McLibListGet(instance, true);

                int nativesFlagged = 0;
                foreach (McLibEntry entry in libraries)
                {
                    if (!entry.IsNatives)
                    {
                        continue;
                    }

                    nativesFlagged++;
                    report.AppendLine("  [IsNatives] " + (File.Exists(entry.LocalPath) ? "存在 " : "缺失 ") + entry.LocalPath);
                }

                report.AppendLine("库总数: " + libraries.Count + "，其中 IsNatives=True: " + nativesFlagged);
                report.AppendLine();

                report.AppendLine("含 natives-windows 的条目：");
                foreach (McLibEntry entry in libraries)
                {
                    if (entry.LocalPath == null || entry.LocalPath.IndexOf("natives-windows", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    report.AppendLine("  " + (File.Exists(entry.LocalPath) ? "存在 " : "缺失 ")
                        + "IsNatives=" + entry.IsNatives + "  " + entry.LocalPath);
                }

                report.AppendLine();

                string command = PclLaunchCommandBuilder.BuildCommand(
                    instance,
                    @"C:\jdk17",
                    new Version(17, 0, 2),
                    "Player",
                    "00000000-0000-0000-0000-000000000000",
                    "0");

                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "nlmcl-launch-command.txt"), command, Encoding.UTF8);

                int libraryPathIndex = command.IndexOf("-Djava.library.path=", StringComparison.Ordinal);
                if (libraryPathIndex >= 0)
                {
                    int end = command.IndexOf("\" ", libraryPathIndex, StringComparison.Ordinal);
                    report.AppendLine("java.library.path: "
                        + (end > 0 ? command.Substring(libraryPathIndex, end - libraryPathIndex) : command.Substring(libraryPathIndex)));
                }
                else
                {
                    report.AppendLine("java.library.path: 参数里没有！");
                }

                report.AppendLine();
                report.AppendLine("classpath 里的 natives jar：");
                foreach (string module in new[] { "lwjgl", "lwjgl-glfw", "lwjgl-openal", "lwjgl-jemalloc", "lwjgl-stb", "lwjgl-tinyfd" })
                {
                    string jar = module + "-3.3.1-natives-windows.jar";
                    report.AppendLine("  " + (command.Contains(jar) ? "在 classpath " : "不在 classpath") + "  " + jar);
                }

                if (command.Contains("javaagent") || command.Contains("-javaagent"))
                {
                    report.AppendLine();
                    report.AppendLine("注意：命令行里出现了 -javaagent（JLW / LUA）");
                }
            });

            int extracted = 0;
            if (Directory.Exists(nativesFolder))
            {
                extracted = Directory.GetFiles(nativesFolder, "*.dll", SearchOption.AllDirectories).Length;
            }

            report.AppendLine();
            report.AppendLine("解压出的 dll 数量: " + extracted);

            string reportPath = Path.Combine(Path.GetTempPath(), "nlmcl-natives-diag.txt");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            Assert.True(File.Exists(reportPath));
        }
    }
}

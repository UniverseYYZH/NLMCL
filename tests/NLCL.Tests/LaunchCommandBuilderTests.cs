using System;
using System.IO;
using System.Text;
using System.Threading;
using NLCL.Core.Pcl.Vb;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// 启动参数管线的集成测试：造一个最小可用的版本目录，走真实的 PCL 参数构建逻辑。
    /// 不启动进程（那需要真实 Java），只验证命令行内容。
    /// </summary>
    public class LaunchCommandBuilderTests : IDisposable
    {
        private readonly string _root;
        private readonly string _versionDirectory;

        private const string VersionJson = @"{
            ""id"": ""1.21.1"",
            ""type"": ""release"",
            ""releaseTime"": ""2024-08-08T14:00:00+00:00"",
            ""mainClass"": ""net.minecraft.client.main.Main"",
            ""assetIndex"": { ""id"": ""17"", ""url"": ""https://example.invalid/17.json"" },
            ""downloads"": { ""client"": { ""url"": ""https://example.invalid/client.jar"", ""sha1"": ""0000000000000000000000000000000000000000"", ""size"": 1 } },
            ""arguments"": {
                ""game"": [
                    ""--username"", ""${auth_player_name}"",
                    ""--version"", ""${version_name}"",
                    ""--gameDir"", ""${game_directory}"",
                    ""--assetsDir"", ""${assets_root}"",
                    ""--assetIndex"", ""${assets_index_name}"",
                    ""--uuid"", ""${auth_uuid}"",
                    ""--accessToken"", ""${access_token}"",
                    ""--userType"", ""${user_type}"",
                    ""--versionType"", ""${version_type}""
                ],
                ""jvm"": [
                    ""-Djava.library.path=${natives_directory}"",
                    ""-cp"", ""${classpath}""
                ]
            },
            ""libraries"": [
                { ""name"": ""com.example:alpha:1.0"", ""downloads"": { ""artifact"": { ""path"": ""com/example/alpha/1.0/alpha-1.0.jar"", ""url"": ""https://example.invalid/a.jar"", ""sha1"": ""0000000000000000000000000000000000000000"", ""size"": 1 } } }
            ]
        }";

        public LaunchCommandBuilderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "nlcl-launch-" + Guid.NewGuid().ToString("N"));
            _versionDirectory = Path.Combine(_root, "versions", "1.21.1");
            Directory.CreateDirectory(_versionDirectory);
            File.WriteAllText(Path.Combine(_versionDirectory, "1.21.1.json"), VersionJson, Encoding.UTF8);
            File.WriteAllBytes(Path.Combine(_versionDirectory, "1.21.1.jar"), new byte[] { 1, 2, 3 });

            string indexDirectory = Path.Combine(_root, "assets", "indexes");
            Directory.CreateDirectory(indexDirectory);
            File.WriteAllText(Path.Combine(indexDirectory, "17.json"), "{\"objects\":{}}", Encoding.UTF8);
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


        /// <summary>
        /// 参数管线会触碰 WPF 派生的占位窗体（FormMain 继承自 Window），
        /// 而 WPF 要求创建线程是 STA，因此测试统一放到 STA 线程上执行。
        /// </summary>
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

        [Fact]
        public void Builds_command_with_all_placeholders_resolved()
        {
            RunSta(() =>
            {
            McInstance instance = new McInstance(_versionDirectory);

            string command = PclLaunchCommandBuilder.BuildCommand(
                instance,
                JavaFolder: @"C:\Program Files\Java\jdk-17",
                JavaVersion: new Version(17, 0, 2),
                UserName: "Steve",
                Uuid: "00000000-0000-0000-0000-000000000000",
                AccessToken: "test-token");

            // 所有 ${...} 都必须被替换掉——残留说明替换表缺项
            Assert.DoesNotContain("${", command);

            // 主类与基础参数
            Assert.Contains("net.minecraft.client.main.Main", command);
            Assert.Contains("--username", command);
            Assert.Contains("Steve", command);

            // classpath 里必须包含客户端 jar 与库
            Assert.Contains("1.21.1.jar", command);
            Assert.Contains("alpha-1.0.jar", command);
            });
        }

        [Fact]
        public void Resolves_asset_index_name_and_natives_folder()
        {
            RunSta(() =>
            {
            McInstance instance = new McInstance(_versionDirectory);

            string command = PclLaunchCommandBuilder.BuildCommand(
                instance, @"C:\jdk17", new Version(17, 0), "Steve", "uuid", "token");

            // 资源索引名来自版本 JSON 的 assetIndex.id
            Assert.Contains("17", command);
            // natives 目录指向版本目录下的 natives
            Assert.Contains("natives", command);
            });
        }

        [Fact]
        public void Applies_extra_game_args_and_server_address()
        {
            RunSta(() =>
            {
            McInstance instance = new McInstance(_versionDirectory);

            string command = PclLaunchCommandBuilder.BuildCommand(
                instance, @"C:\jdk17", new Version(17, 0), "Steve", "uuid", "token",
                ServerIp: "127.0.0.1:25565",
                ExtraGameArgs: new[] { "--width", "1280" });

            Assert.Contains("127.0.0.1:25565", command);
            Assert.Contains("--width", command);
            Assert.Contains("1280", command);
            });
        }

        /// <summary>
        /// 回归：界面上的"内存 (MB)"曾经只写进启动日志，命令行永远是 -Xmx2048m。
        /// 这条测试直接断言 PCL 参数管线产出的 -Xmx。
        /// </summary>
        [Fact]
        public void Applies_requested_memory_to_xmx()
        {
            RunSta(() =>
            {
                McInstance instance = new McInstance(_versionDirectory);

                string command = PclLaunchCommandBuilder.BuildCommand(
                    instance, @"C:\jdk17", new Version(17, 0), "Steve", "uuid", "token",
                    RamMb: 6144);

                Assert.Contains("-Xmx6144m", command);
            });
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Settings
{
    /// <summary>
    /// 把实例设置存在版本目录下的 <c>nlmcl-settings.json</c>。
    ///
    /// 为什么放在版本目录而不是全局文件：
    /// 用户复制/移动一个版本文件夹时，设置应该跟着走；
    /// 删掉版本时设置自然一起消失，不会留下孤儿配置。
    /// </summary>
    public sealed class JsonInstanceSettingsStore : IInstanceSettingsStore
    {
        public const string FileName = "nlmcl-settings.json";

        public Task<InstanceSettings> LoadAsync(
            string minecraftDirectory, string versionId, CancellationToken cancellationToken)
        {
            string file = PathOf(minecraftDirectory, versionId);

            if (!File.Exists(file))
            {
                return Task.FromResult(new InstanceSettings());
            }

            try
            {
                string json = File.ReadAllText(file, Encoding.UTF8);
                JObject root = JObject.Parse(json);

                InstanceSettings settings = new InstanceSettings
                {
                    MemoryMb = root.Value<int?>("memoryMb") ?? InstanceSettings.DefaultMemoryMb,
                    JavaPath = root.Value<string>("javaPath"),
                    ExtraArguments = root.Value<string>("extraArguments"),
                    ServerAddress = root.Value<string>("serverAddress"),
                    WindowWidth = root.Value<int?>("windowWidth") ?? 0,
                    WindowHeight = root.Value<int?>("windowHeight") ?? 0,
                    Fullscreen = root.Value<bool?>("fullscreen") ?? false,
                    UseProxy = root.Value<bool?>("useProxy") ?? false,
                    ProxyHost = root.Value<string>("proxyHost"),
                    ProxyPort = root.Value<int?>("proxyPort") ?? 0,
                    SoftwareOpenGl = root.Value<bool?>("softwareOpenGl") ?? false
                };

                return Task.FromResult(settings);
            }
            catch (JsonException)
            {
                // 坏文件不该拦住启动：回落到默认设置，用户改动时会覆盖它
                return Task.FromResult(new InstanceSettings());
            }
            catch (IOException)
            {
                return Task.FromResult(new InstanceSettings());
            }
        }

        public bool Exists(string minecraftDirectory, string versionId)
        {
            return File.Exists(PathOf(minecraftDirectory, versionId));
        }

        public Task SaveAsync(
            string minecraftDirectory, string versionId, InstanceSettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            string file = PathOf(minecraftDirectory, versionId);
            string? folder = Path.GetDirectoryName(file);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            JObject root = new JObject
            {
                ["memoryMb"] = settings.MemoryMb,
                ["javaPath"] = settings.JavaPath,
                ["extraArguments"] = settings.ExtraArguments,
                ["serverAddress"] = settings.ServerAddress,
                ["windowWidth"] = settings.WindowWidth,
                ["windowHeight"] = settings.WindowHeight,
                ["fullscreen"] = settings.Fullscreen,
                ["useProxy"] = settings.UseProxy,
                ["proxyHost"] = settings.ProxyHost,
                ["proxyPort"] = settings.ProxyPort,
                ["softwareOpenGl"] = settings.SoftwareOpenGl
            };

            File.WriteAllText(file, root.ToString(Formatting.Indented), Encoding.UTF8);
            return Task.CompletedTask;
        }

        private static string PathOf(string minecraftDirectory, string versionId)
        {
            if (string.IsNullOrWhiteSpace(minecraftDirectory))
            {
                throw new ArgumentException("Minecraft 目录不能为空。", nameof(minecraftDirectory));
            }

            if (string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException("版本名不能为空。", nameof(versionId));
            }

            return Path.Combine(minecraftDirectory, "versions", versionId, FileName);
        }
    }
}

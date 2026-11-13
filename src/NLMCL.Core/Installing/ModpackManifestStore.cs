using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 实例目录里的 <c>.nlmcl-modpack.json</c>：记录来源、版本和整合包自己的 Mod 清单。
    /// 更新检查只依赖这个文件，不读取启动器的账号或全局设置。
    /// </summary>
    public static class ModpackManifestStore
    {
        public const string FileName = ".nlmcl-modpack.json";

        public static string PathFor(string instanceDirectory)
        {
            if (string.IsNullOrWhiteSpace(instanceDirectory))
            {
                throw new ArgumentException("实例目录为空。", nameof(instanceDirectory));
            }

            return Path.Combine(instanceDirectory, FileName);
        }

        public static ModpackManifest? Read(string instanceDirectory)
        {
            string path = PathFor(instanceDirectory);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                ModpackManifest? manifest = JsonConvert.DeserializeObject<ModpackManifest>(
                    File.ReadAllText(path, Encoding.UTF8));

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Name))
                {
                    return null;
                }

                manifest.ModFiles ??= new System.Collections.Generic.List<string>();
                return manifest;
            }
            catch (Exception)
            {
                // 损坏的元数据不能让实例无法启动；当作“没有整合包信息”。
                return null;
            }
        }

        public static void Write(string instanceDirectory, ModpackManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            Directory.CreateDirectory(instanceDirectory);
            manifest.InstalledAt = manifest.InstalledAt == default
                ? DateTimeOffset.Now
                : manifest.InstalledAt;

            string path = PathFor(instanceDirectory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(manifest, Formatting.Indented),
                new UTF8Encoding(false));

            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
    }
}

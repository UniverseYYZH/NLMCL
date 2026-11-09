using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Maintenance
{
    /// <summary>读取本地版本 JSON 与目录信息，用于版本详情浮层。</summary>
    public sealed class VersionDetailsService : IVersionDetailsService
    {
        public VersionDetailsData Read(string gameDirectory, string folderName)
        {
            VersionDetailsData data = new VersionDetailsData();

            if (string.IsNullOrWhiteSpace(gameDirectory)
                || string.IsNullOrWhiteSpace(folderName)
                || !string.Equals(Path.GetFileName(folderName), folderName, StringComparison.Ordinal))
            {
                data.Error = "版本目录名无效。";
                return data;
            }

            string versionDirectory = Path.Combine(gameDirectory, "versions", folderName);
            data.VersionJsonPath = Path.Combine(versionDirectory, folderName + ".json");
            data.ClientJarPath = Path.Combine(versionDirectory, folderName + ".jar");

            try
            {
                data.VersionJsonExists = File.Exists(data.VersionJsonPath);

                if (!data.VersionJsonExists)
                {
                    string? fallback = Directory.Exists(versionDirectory)
                        ? Directory.GetFiles(versionDirectory, "*.json")
                            .FirstOrDefault(path => !path.EndsWith(
                                "nlmcl-settings.json", StringComparison.OrdinalIgnoreCase))
                        : null;

                    if (fallback != null)
                    {
                        data.VersionJsonPath = fallback;
                        data.VersionJsonExists = true;
                    }
                }

                data.ClientJarExists = File.Exists(data.ClientJarPath);

                if (data.ClientJarExists)
                {
                    data.ClientJarSize = new FileInfo(data.ClientJarPath).Length;
                }

                if (data.VersionJsonExists)
                {
                    JObject root = JObject.Parse(File.ReadAllText(data.VersionJsonPath));
                    data.VersionId = root.Value<string>("id");
                    data.MainClass = root.Value<string>("mainClass");
                    data.Assets = root.Value<string>("assets");
                    data.InheritsFrom = root.Value<string>("inheritsFrom");
                    data.LibraryCount = (root["libraries"] as JArray)?.Count ?? 0;

                    JToken? java = root["javaVersion"];
                    data.JavaMajor = java?.Value<int?>("majorVersion");
                }

                string mods = Path.Combine(versionDirectory, "mods");
                if (Directory.Exists(mods))
                {
                    data.ModCount = Directory.GetFiles(mods)
                        .Count(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex)
            {
                data.Error = ex.Message;
            }

            return data;
        }
    }
}

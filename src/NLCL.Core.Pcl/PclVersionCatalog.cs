using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLCL.Abstractions;
using PCLCS;
using AbstractionsVersion = NLCL.Abstractions.MinecraftVersion;

namespace NLCL.Core.Pcl
{
    /// <summary>
    /// <see cref="IVersionCatalog"/> 的适配器：用 PCL 的 <see cref="McVersion"/> 识别本地版本。
    ///
    /// 这是整个解决方案里唯一的「防腐层」——上层只认识 NLCL.Abstractions 的接口与 DTO，
    /// 不知道 PCL 的存在，因此日后换实现只需替换本类型。
    /// </summary>
    public sealed class PclVersionCatalog : IVersionCatalog
    {
        /// <summary>inheritsFrom 链的最大深度，防止损坏的 JSON 形成环。</summary>
        private const int MaxInheritDepth = 8;

        public Task<IReadOnlyList<AbstractionsVersion>> GetInstalledAsync(
            string minecraftDirectory,
            CancellationToken cancellationToken)
        {
            if (minecraftDirectory == null)
            {
                throw new ArgumentNullException(nameof(minecraftDirectory));
            }

            // 目录枚举是阻塞 IO，放到线程池，避免卡住调用方（UI）线程。
            return Task.Run<IReadOnlyList<AbstractionsVersion>>(
                () => Scan(minecraftDirectory, cancellationToken), cancellationToken);
        }

        private static IReadOnlyList<AbstractionsVersion> Scan(string minecraftDirectory, CancellationToken cancellationToken)
        {
            List<AbstractionsVersion> result = new List<AbstractionsVersion>();

            string versionsDirectory = Path.Combine(minecraftDirectory, "versions");
            if (!Directory.Exists(versionsDirectory))
            {
                return result;
            }

            foreach (string folder in Directory.GetDirectories(versionsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string folderName = Path.GetFileName(folder);
                string? jsonPath = ResolveJsonPath(folder, folderName);
                if (jsonPath == null)
                {
                    continue;
                }

                try
                {
                    JObject json = LoadWithInheritance(jsonPath, versionsDirectory, folderName);
                    McVersion version = new McVersion(folderName, json);
                    result.Add(Map(folderName, folder, version, json));
                }
                catch (Exception)
                {
                    // 单个版本 JSON 损坏不应让整个列表失败——这与 PCL 的 Check() 容错策略一致。
                }
            }

            return result
                .OrderByDescending(version => version.ReleasedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }

        /// <summary>
        /// 找出该版本的 JSON：优先 <c>&lt;目录名&gt;.json</c>，否则取目录里唯一的 JSON。
        /// </summary>
        private static string? ResolveJsonPath(string folder, string folderName)
        {
            string exact = Path.Combine(folder, folderName + ".json");
            if (File.Exists(exact))
            {
                return exact;
            }

            string[] candidates = Directory.GetFiles(folder, "*.json");
            return candidates.Length > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// 读取版本 JSON，并沿 <c>inheritsFrom</c> 向上合并父版本。
        /// 子版本覆盖顶层字段；<c>libraries</c> 做拼接（加载器版本的关键）。
        /// </summary>
        private static JObject LoadWithInheritance(string jsonPath, string versionsDirectory, string folderName)
        {
            JObject current = ReadObject(jsonPath);
            JObject merged = (JObject)current.DeepClone();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { folderName };

            string? inherit = current.Value<string>("inheritsFrom");
            int depth = 0;

            while (!string.IsNullOrEmpty(inherit) && depth++ < MaxInheritDepth)
            {
                if (inherit == null || !visited.Add(inherit))
                {
                    break;
                }

                string parentJsonPath = Path.Combine(versionsDirectory, inherit, inherit + ".json");
                if (!File.Exists(parentJsonPath))
                {
                    break;
                }

                JObject parent = ReadObject(parentJsonPath);
                merged = MergeChildOverParent(merged, parent);
                inherit = parent.Value<string>("inheritsFrom");
            }

            return merged;
        }

        /// <summary>子覆盖父；<c>libraries</c> 拼接，使加载器依赖不丢失。</summary>
        private static JObject MergeChildOverParent(JObject child, JObject parent)
        {
            JObject merged = (JObject)parent.DeepClone();

            foreach (JProperty property in child.Properties())
            {
                if (property.Name == "libraries")
                {
                    JArray childLibraries = property.Value as JArray ?? new JArray();
                    JArray parentLibraries = merged["libraries"] as JArray ?? new JArray();
                    merged["libraries"] = new JArray(parentLibraries.Concat(childLibraries).ToList());
                }
                else
                {
                    merged[property.Name] = property.Value.DeepClone();
                }
            }

            return merged;
        }

        private static JObject ReadObject(string path)
        {
            return JObject.Parse(File.ReadAllText(path));
        }

        private static AbstractionsVersion Map(
            string folderName, string folderPath, McVersion version, JObject json)
        {
            DateTime releaseTime = version.ReleaseTime;
            DateTimeOffset? releasedAt = releaseTime.Year > 1970
                ? new DateTimeOffset(DateTime.SpecifyKind(releaseTime, DateTimeKind.Local))
                : (DateTimeOffset?)null;

            return new AbstractionsVersion(
                version.VanillaName,
                folderName,
                MapKind(version),
                releasedAt,
                HasClientJar(folderPath, folderName, json));
        }

        private static VersionKind MapKind(McVersion version)
        {
            if (version.IsFool || version.IsSnapshot)
            {
                return VersionKind.Snapshot;
            }

            // PCL 只把 2013 年前的版本统一标成 "Old"，不区分 Alpha/Beta；
            // 这里按发布时间补一个更细的划分，纯粹是展示需要。
            if (string.Equals(version.VanillaName, "Old", StringComparison.OrdinalIgnoreCase))
            {
                return version.ReleaseTime.Year < 2011 ? VersionKind.OldAlpha : VersionKind.OldBeta;
            }

            return version.Valid ? VersionKind.Release : VersionKind.Unknown;
        }

        /// <summary>
        /// 客户端 JAR 是否存在。
        ///
        /// 不能只看版本自己的目录：inheritsFrom 实例（Fabric / Forge / 整合包）的 jar 在**父版本**目录里，
        /// 只查自己的话会给这些实例标上"未找到客户端 JAR，可能无法启动"的误报。
        /// </summary>
        private static bool HasClientJar(string folderPath, string folderName, JObject json)
        {
            try
            {
                if (HasJarIn(folderPath, folderName))
                {
                    return true;
                }

                string trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string? versionsRoot = Path.GetDirectoryName(trimmed);
                if (string.IsNullOrEmpty(versionsRoot))
                {
                    return false;
                }

                string? parent = json.Value<string>("inheritsFrom");

                for (int depth = 0; depth < 5 && !string.IsNullOrEmpty(parent); depth++)
                {
                    string parentFolder = Path.Combine(versionsRoot!, parent!);
                    if (HasJarIn(parentFolder, parent!))
                    {
                        return true;
                    }

                    string parentJson = Path.Combine(parentFolder, parent + ".json");
                    if (!File.Exists(parentJson))
                    {
                        break;
                    }

                    parent = JObject.Parse(File.ReadAllText(parentJson)).Value<string>("inheritsFrom");
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasJarIn(string folderPath, string folderName)
        {
            return File.Exists(Path.Combine(folderPath, folderName + ".jar"))
                || Directory.GetFiles(folderPath, "*.jar").Length > 0;
        }
    }
}

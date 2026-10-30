using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NLCL.Core.Installing
{
    /// <summary>
    /// 把版本 JSON 翻译成一份"要下哪些文件"的清单。
    ///
    /// 这是 §2.1a 批准的唯一豁免点：PCL 的等价实现（`ModDownload.DlClientFix` +
    /// `McLibNetFilesFromInstance` + `McAssetsFixList`）与资源广场/UI 同属一个编译单元，
    /// 无法单独取出。**本类只做枚举与路径映射**，不做任何 IO、下载或哈希。
    /// </summary>
    internal static class VersionPlanBuilder
    {
        public static VersionInstallPlan Build(
            string versionId,
            string versionJsonText,
            string minecraftDirectory,
            InstallOptions options)
        {
            JObject json = JObject.Parse(versionJsonText);

            string versionsRoot = Path.Combine(minecraftDirectory, "versions");
            string versionDirectory = Path.Combine(versionsRoot, versionId);
            VersionInstallPlan plan = new VersionInstallPlan(
                versionDirectory,
                Path.Combine(versionDirectory, versionId + ".json"));

            // 必须与 PCL 启动端一致：GetNativesFolder() 返回 <版本目录>\<版本名>-natives\
            // 之前写成 <版本目录>\natives，结果 java.library.path 指向空目录，LWJGL 报 lwjgl.dll 找不到
            plan.NativesDirectory = Path.Combine(versionDirectory, versionId + "-natives");

            // 客户端 JAR
            JToken? client = json["downloads"]?["client"];
            if (client != null)
            {
                string? url = client.Value<string>("url");
                if (url != null && url.Length > 0)
                {
                    plan.Files.Add(new RemoteFile(
                        url,
                        Path.Combine(versionDirectory, versionId + ".jar"),
                        client.Value<string>("sha1"),
                        client.Value<long?>("size") ?? 0));
                }
            }

            // 资源索引（先下载，之后再解析出具体资源文件）
            JToken? assetIndex = json["assetIndex"];
            if (assetIndex != null)
            {
                string? url = assetIndex.Value<string>("url");
                string indexId = assetIndex.Value<string>("id") ?? versionId;
                if (url != null && url.Length > 0)
                {
                    plan.AssetIndex = new RemoteFile(
                        url,
                        Path.Combine(minecraftDirectory, "assets", "indexes", indexId + ".json"),
                        assetIndex.Value<string>("sha1"),
                        0);
                }
            }

            // 库文件
            string librariesRoot = Path.Combine(minecraftDirectory, "libraries");
            foreach (JToken library in json["libraries"] as JArray ?? new JArray())
            {
                if (!IsAllowedOnThisPlatform(library["rules"]))
                {
                    continue;
                }

                JToken? downloads = library["downloads"];
                string? name = library.Value<string>("name") ?? string.Empty;

                // 旧格式（≤1.18.2）：库节点带 natives.windows + downloads.classifiers。
                // 注意这里**不能 continue**：同一个库往往同时有 artifact（类 jar，要进 classpath）
                // 和 natives 归档（dll，要解压）。以前跳过 artifact，1.13–1.18.2 装完必缺 lwjgl 类。
                string? classifier = library["natives"]?.Value<string>("windows");
                JToken? nativeEntry = classifier == null
                    ? null
                    : downloads?["classifiers"]?[classifier];

                if (nativeEntry != null)
                {
                    string? nativePath = nativeEntry.Value<string>("path");
                    if (nativePath != null && nativePath.Length > 0)
                    {
                        plan.NativeArchives.Add(new RemoteFile(
                            nativeEntry.Value<string>("url"),
                            Path.Combine(librariesRoot, nativePath),
                            nativeEntry.Value<string>("sha1"),
                            nativeEntry.Value<long?>("size") ?? 0));
                    }
                }

                // 普通库：Forge/Mojang 的新格式都走 artifact
                JToken? artifact = downloads?["artifact"];
                if (artifact == null)
                {
                    // 少数库只有 maven 坐标，PCL 会自行拼 URL；M3-B 范围内跳过。
                    continue;
                }

                string? relative = artifact.Value<string>("path");
                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                plan.Files.Add(new RemoteFile(
                    artifact.Value<string>("url"),
                    Path.Combine(librariesRoot, relative),
                    artifact.Value<string>("sha1"),
                    artifact.Value<long?>("size") ?? 0));
            }

            // 资源对象
            if (plan.AssetIndex != null)
            {
                // 由调用方在下载完索引后再补齐（需要索引内容）。
                _ = options;
            }

            return plan;
        }

        /// <summary>把资源索引里的 objects 转成待下载文件。</summary>
        public static IEnumerable<RemoteFile> BuildAssetFiles(
            string assetIndexJson,
            string minecraftDirectory,
            string resourcesBaseUrl,
            int maxFiles)
        {
            JObject root = JObject.Parse(assetIndexJson);
            JObject? objects = root["objects"] as JObject;
            if (objects == null)
            {
                return Enumerable.Empty<RemoteFile>();
            }

            List<RemoteFile> files = new List<RemoteFile>();
            string objectsRoot = Path.Combine(minecraftDirectory, "assets", "objects");

            foreach (JProperty property in objects.Properties())
            {
                if (maxFiles > 0 && files.Count >= maxFiles)
                {
                    break;
                }

                string? hash = property.Value.Value<string>("hash");
                if (hash == null || hash.Length < 2)
                {
                    continue;
                }

                string prefix = hash.Substring(0, 2);
                files.Add(new RemoteFile(
                    resourcesBaseUrl.TrimEnd('/') + "/" + prefix + "/" + hash,
                    Path.Combine(objectsRoot, prefix, hash),
                    hash,
                    property.Value.Value<long?>("size") ?? 0));
            }

            return files;
        }

        /// <summary>按 Mojang 的规则列表判断该库是否适用于本机。</summary>
        internal static bool IsAllowedOnThisPlatform(JToken? rules)
        {
            if (rules == null || rules.Type != JTokenType.Array || !rules.HasValues)
            {
                return true;
            }

            bool allowed = false;

            foreach (JToken rule in rules)
            {
                string action = rule.Value<string>("action") ?? string.Empty;
                bool matches = RuleMatches(rule);

                if (action == "allow" && matches)
                {
                    allowed = true;
                }
                else if (action == "disallow" && matches)
                {
                    allowed = false;
                }
            }

            return allowed;
        }

        private static bool RuleMatches(JToken rule)
        {
            // 操作系统
            JToken os = rule["os"];
            if (os != null)
            {
                string? name = os.Value<string>("name");
                if (!string.IsNullOrEmpty(name) && !string.Equals(name, "windows", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string arch = os.Value<string>("arch");
                if (!string.IsNullOrEmpty(arch) && !string.Equals(arch, CurrentArch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 特性：NLCL 目前不启用任何特性（demo、自定义分辨率等），
            // 因此任何要求特性为 true 的规则都不匹配。
            JObject features = rule["features"] as JObject;
            if (features != null)
            {
                foreach (JProperty feature in features.Properties())
                {
                    if (feature.Value.Type == JTokenType.Boolean && feature.Value.Value<bool>())
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string CurrentArch =>
            Environment.Is64BitProcess ? "amd64" : "x86";
    }
}

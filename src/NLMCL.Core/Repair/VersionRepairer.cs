using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Repair
{
    /// <summary>
    /// 版本修复。
    ///
    /// 收集范围：
    /// - 客户端 JAR（<c>jar</c> 字段指向的版本目录，默认自己）
    /// - 各层版本 JSON 的 <c>libraries</c>（按 OS 规则过滤，Windows 才要）
    ///
    /// 下载优先走 BMCLAPI（<see cref="Installing.MojangMirror"/>），失败回落官方地址。
    /// </summary>
    public sealed class VersionRepairer : IVersionRepairer
    {
        private readonly IFileDownloader _files;

        public VersionRepairer(IFileDownloader? files = null)
        {
            _files = files ?? new Installing.HttpFileDownloader();
        }

        public Task<RepairReport> InspectAsync(
            string minecraftDirectory, string instanceFolder, CancellationToken cancellationToken)
        {
            // 校验 SHA1 要读完整文件，不能放在 UI 线程上做。
            return Task.Run(() =>
            {
                List<NeededFile> needed = Collect(minecraftDirectory, instanceFolder, out string? error);

                if (error != null)
                {
                    return new RepairReport(0, 0, 0, new[] { error });
                }

                List<string> missing = needed
                    .Where(file => !IsHealthy(file))
                    .Select(file => file.RelativePath)
                    .ToList();

                return new RepairReport(missing.Count, 0, 0, missing.Take(20).ToList());
            }, cancellationToken);
        }

        public async Task<RepairReport> RepairAsync(
            string minecraftDirectory, string instanceFolder,
            IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
        {
            List<NeededFile> needed = Collect(minecraftDirectory, instanceFolder, out string? error);

            if (error != null)
            {
                return new RepairReport(0, 0, 1, new[] { error });
            }

            List<NeededFile> missing = needed.Where(file => !IsHealthy(file)).ToList();

            if (missing.Count == 0)
            {
                return new RepairReport(0, 0, 0, Array.Empty<string>());
            }

            int repaired = 0;
            List<string> failures = new List<string>();

            for (int index = 0; index < missing.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                NeededFile file = missing[index];

                progress?.Report(new InstallProgress(
                    "修复 " + (index + 1) + "/" + missing.Count + "：" + Path.GetFileName(file.Target),
                    (int)(100.0 * index / missing.Count), 0));

                try
                {
                    string? directory = Path.GetDirectoryName(file.Target);

                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory!);
                    }

                    // 文件存在但哈希不符时不能断点续传，否则会继续拼在坏文件后面。
                    if (File.Exists(file.Target))
                    {
                        File.Delete(file.Target);
                    }

                    await DownloadAsync(file, cancellationToken).ConfigureAwait(false);
                    repaired++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add(file.RelativePath + "（" + ex.Message + "）");
                }
            }

            progress?.Report(new InstallProgress("修复完成", 100, 0));

            return new RepairReport(missing.Count, repaired, failures.Count, failures.Take(10).ToList());
        }

        private async Task DownloadAsync(NeededFile file, CancellationToken cancellationToken)
        {
            string? mirrored = Installing.MojangMirror.TryRewrite(file.Url);
            Exception? firstError = null;

            if (mirrored != null)
            {
                try
                {
                    await _files.DownloadAsync(mirrored, file.Target, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    firstError = ex;
                }
            }

            try
            {
                await _files.DownloadAsync(file.Url, file.Target, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception second)
            {
                throw new InvalidOperationException(
                    "下载失败（镜像：" + (firstError?.Message ?? "-") + "；官方：" + second.Message + "）", second);
            }
        }

        /// <summary>文件存在还不够；元数据给了 SHA1 时，内容也必须对得上。</summary>
        internal static bool IsHealthy(NeededFile file)
        {
            if (!File.Exists(file.Target))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(file.Sha1))
            {
                return true;
            }

            try
            {
                using FileStream stream = File.OpenRead(file.Target);
                using System.Security.Cryptography.SHA1 sha1 =
                    System.Security.Cryptography.SHA1.Create();
                string actual = BitConverter
                    .ToString(sha1.ComputeHash(stream))
                    .Replace("-", string.Empty);

                return string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>沿 inheritsFrom 链收集需要的文件。</summary>
        internal static List<NeededFile> Collect(
            string minecraftDirectory, string instanceFolder, out string? error)
        {
            error = null;
            List<NeededFile> result = new List<NeededFile>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string folder = instanceFolder;
            string versionsRoot = Path.Combine(minecraftDirectory, "versions");

            for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(folder); depth++)
            {
                string jsonPath = Path.Combine(versionsRoot, folder, folder + ".json");

                if (!File.Exists(jsonPath))
                {
                    error = "找不到版本 JSON：" + jsonPath;
                    return result;
                }

                JObject json;

                try
                {
                    json = JObject.Parse(File.ReadAllText(jsonPath));
                }
                catch (Exception)
                {
                    error = "版本 JSON 无法解析：" + jsonPath;
                    return result;
                }

                AddLibraries(minecraftDirectory, json, result, seen);

                // 客户端 JAR 由"最底层"（原版）那层的 downloads.client 提供
                string jarName = json.Value<string>("jar") ?? folder;
                string jarPath = Path.Combine(versionsRoot, jarName, jarName + ".jar");

                if (!seen.Contains(jarPath))
                {
                    seen.Add(jarPath);

                    string? url = json
                        .SelectToken("downloads.client.url")?.Value<string>();
                    string? sha1 = json
                        .SelectToken("downloads.client.sha1")?.Value<string>();

                    if (string.IsNullOrEmpty(url))
                    {
                        string jarJson = Path.Combine(versionsRoot, jarName, jarName + ".json");

                        if (File.Exists(jarJson))
                        {
                            try
                            {
                                JObject baseJson = JObject.Parse(File.ReadAllText(jarJson));
                                url = baseJson.SelectToken("downloads.client.url")?.Value<string>();
                                sha1 = baseJson.SelectToken("downloads.client.sha1")?.Value<string>();
                            }
                            catch (Exception)
                            {
                                // 忽略
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        result.Add(new NeededFile(
                            jarPath, url!, "versions/" + jarName + "/" + jarName + ".jar", sha1));
                    }
                }

                folder = json.Value<string>("inheritsFrom") ?? string.Empty;
            }

            return result;
        }

        private static void AddLibraries(
            string minecraftDirectory, JObject json, List<NeededFile> result, HashSet<string> seen)
        {
            if (json["libraries"] is not JArray libraries)
            {
                return;
            }

            foreach (JToken library in libraries)
            {
                if (!IsAllowedOnWindows(library))
                {
                    continue;
                }

                string? path = library.SelectToken("downloads.artifact.path")?.Value<string>();
                string? url = library.SelectToken("downloads.artifact.url")?.Value<string>();
                string? sha1 = library.SelectToken("downloads.artifact.sha1")?.Value<string>();

                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                string target = Path.Combine(
                    minecraftDirectory, "libraries", path!.Replace('/', Path.DirectorySeparatorChar));

                if (seen.Add(target))
                {
                    result.Add(new NeededFile(target, url!, "libraries/" + path, sha1));
                }
            }
        }

        /// <summary>只处理 Windows 允许的库（rules 里明确 deny windows 的跳过）。</summary>
        internal static bool IsAllowedOnWindows(JToken library)
        {
            if (library["rules"] is not JArray rules || rules.Count == 0)
            {
                return true;
            }

            bool allowed = false;

            foreach (JToken rule in rules)
            {
                string? action = rule.Value<string>("action");
                string? os = rule.SelectToken("os.name")?.Value<string>();

                if (!string.IsNullOrEmpty(os) && !string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                allowed = string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase);
            }

            return allowed;
        }

        /// <summary>一个待检查/待下载的文件。</summary>
        internal sealed class NeededFile
        {
            public NeededFile(string target, string url, string relativePath, string? sha1 = null)
            {
                Target = target;
                Url = url;
                RelativePath = relativePath;
                Sha1 = sha1;
            }

            public string Target { get; }

            public string Url { get; }

            public string RelativePath { get; }

            public string? Sha1 { get; }
        }
    }
}

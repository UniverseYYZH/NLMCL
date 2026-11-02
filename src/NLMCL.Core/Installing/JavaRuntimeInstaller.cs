using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 按 Mojang 的 Java 运行时清单下载组件，落到 <c>%APPDATA%\.minecraft\runtime\&lt;component&gt;</c>。
    ///
    /// 清单地址与结构直接沿用 PCL <c>ModJava.vb</c> 里用的那份
    /// （<c>piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96.../all.json</c>）：
    /// 顶层按平台分组（<c>windows-x64</c>），每个组件给一个 <c>manifest.url</c>，
    /// 里面是 <c>files</c> 字典（每项带 downloads.raw 的 url / sha1 / size）。
    ///
    /// 为什么不直接调 PCL 的 <c>SelectOrDownloadJava</c>：那个函数把「挑 Java → 弹窗 →
    /// 下载 → 更新列表」揉在一起，且 <c>TryFixOrHint=False</c> 时会在下载前直接返回 Nothing。
    /// 这里只复用它的**数据来源**，下载与落盘走本项目的 <see cref="IDownloadService"/>（PCL ModNet）。
    /// </summary>
    public sealed class JavaRuntimeInstaller : IJavaRuntimeInstaller
    {
        /// <summary>与 PCL 相同的清单地址。</summary>
        public const string ManifestUrl =
            "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

        private const string Platform = "windows-x64";

        private readonly IDownloadService _download;
        private readonly IAppPaths _paths;

        public JavaRuntimeInstaller(IDownloadService download, IAppPaths paths)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public async Task<JavaRuntime> InstallAsync(
            string component,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                throw new ArgumentException("Java 组件名不能为空。", nameof(component));
            }

            progress?.Report(new InstallProgress("读取 Java 运行时清单", 2, 0));

            string index = await _download
                .GetStringAsync(ManifestUrl, cancellationToken)
                .ConfigureAwait(false);

            string manifestUrl = ResolvePlatformManifest(index, component);
            string manifest = await _download
                .GetStringAsync(manifestUrl, cancellationToken)
                .ConfigureAwait(false);

            List<RuntimeFile> files = ParseFiles(manifest);
            if (files.Count == 0)
            {
                throw new InvalidDataException("Java 运行时清单里没有任何文件：" + component);
            }

            progress?.Report(new InstallProgress("下载 Java 运行时（" + files.Count + " 个文件）", 5, 0));

            string root = Path.Combine(_paths.DefaultMinecraftDirectory, "runtime", component);

            for (int index2 = 0; index2 < files.Count; index2++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RuntimeFile file = files[index2];
                string target = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!IsUpToDate(target, file.Sha1))
                {
                    string? directory = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory!);
                    }

                    await DownloadVerifiedAsync(file, target, cancellationToken).ConfigureAwait(false);
                }

                double percent = 5 + (index2 + 1) * 90.0 / files.Count;
                progress?.Report(new InstallProgress(
                    "下载 Java 运行时 " + (index2 + 1) + "/" + files.Count, percent, 0));
            }

            progress?.Report(new InstallProgress("Java 运行时安装完成", 100, 0));

            string home = root + Path.DirectorySeparatorChar;

            // 用 JDK 自带的 release 文件识别真实主版本；读不到就留给调用方重新探测
            Version? version = JavaVersionProbe.TryRead(Path.Combine(root, "bin", "javaw.exe"));

            return new JavaRuntime(home, version);
        }

        /// <summary>从全量清单里取出某个组件的 windows-x64 平台清单地址。</summary>
        internal static string ResolvePlatformManifest(string indexJson, string component)
        {
            JObject root = JObject.Parse(indexJson);

            JArray? entries = root[Platform]?[component] as JArray;
            JToken? first = entries?.First;

            string? url = first?.SelectToken("manifest.url")?.Value<string>();
            if (string.IsNullOrEmpty(url))
            {
                throw new InvalidDataException(
                    "Mojang 的 Java 清单里没有组件 " + component + "（" + Platform + "）。");
            }

            return url!;
        }

        /// <summary>解析平台清单里的 files 字典（跳过 directory / link）。</summary>
        internal static List<RuntimeFile> ParseFiles(string manifestJson)
        {
            List<RuntimeFile> result = new List<RuntimeFile>();

            JObject? files = JObject.Parse(manifestJson)["files"] as JObject;
            if (files == null)
            {
                return result;
            }

            foreach (JProperty property in files.Properties())
            {
                JToken entry = property.Value;
                if (!string.Equals(entry.Value<string>("type"), "file", StringComparison.Ordinal))
                {
                    continue;
                }

                string? url = entry.SelectToken("downloads.raw.url")?.Value<string>();
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                result.Add(new RuntimeFile(
                    property.Name,
                    url!,
                    entry.SelectToken("downloads.raw.sha1")?.Value<string>()));
            }

            return result;
        }

        private static bool IsUpToDate(string target, string? sha1)
        {
            if (!File.Exists(target))
            {
                return false;
            }

            if (string.IsNullOrEmpty(sha1))
            {
                return new FileInfo(target).Length > 0;
            }

            string actual = CryptographyUtils.ComputeFileHash(target, CryptographyUtils.HashMethod.Sha1);
            return string.Equals(actual, sha1, StringComparison.OrdinalIgnoreCase);
        }

        private async Task DownloadVerifiedAsync(RuntimeFile file, string target, CancellationToken cancellationToken)
        {
            string temp = target + ".nlmcl-tmp";

            try
            {
                await _download.DownloadFileAsync(file.Url, temp, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(file.Sha1))
                {
                    string actual = CryptographyUtils.ComputeFileHash(temp, CryptographyUtils.HashMethod.Sha1);
                    if (!string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Java 运行时文件校验失败：" + file.RelativePath);
                    }
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temp, target);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>运行时清单里的一个文件。</summary>
        internal sealed class RuntimeFile
        {
            public RuntimeFile(string relativePath, string url, string? sha1)
            {
                RelativePath = relativePath;
                Url = url;
                Sha1 = sha1;
            }

            public string RelativePath { get; }

            public string Url { get; }

            public string? Sha1 { get; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// Fabric 安装：从 Fabric 官方 meta 取 <c>profile/json</c>，落成版本 JSON 并下载其库。
    ///
    /// **为什么不搬 PCL 的 `ModDownloadLib.vb`**：那个文件 2072 行、16 处窗体引用，
    /// 安装函数（<c>McDownloadFabricLoaderSave</c>）接收的是页面上的 <c>JObject</c> 与
    /// <c>MyListItem</c>，且依赖 <c>ResourceVersion</c>（资源广场模型）。实测探针只报 40 个错误，
    /// 但那是**类型绑定中止**造成的低估（同一现象曾让 ModLaunch 从 4 变 382）。
    ///
    /// 而 Fabric 的 profile 接口本身返回的就是一份**完整版本 JSON**（含 inheritsFrom 与库列表），
    /// 因此这里只做「取 JSON → 落盘 → 下库」三件事，属于 §2.1a 的安装编排豁免范围。
    /// 下载仍走 <see cref="IDownloadService"/>（PCL `ModNet`），哈希仍走 MeloongCore。
    /// </summary>
    public sealed class FabricLoaderInstaller : IVersionInstaller
    {
        public const string MetaBaseUrl = "https://meta.fabricmc.net/v2/versions/loader";

        private readonly IDownloadService _download;
        private readonly VanillaVersionInstaller _vanilla;
        private readonly IFileDownloader? _fallback;

        public FabricLoaderInstaller(
            IDownloadService download,
            VanillaVersionInstaller vanilla,
            IFileDownloader? fallback = null)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _vanilla = vanilla ?? throw new ArgumentNullException(nameof(vanilla));
            _fallback = fallback;
        }

        public async Task InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!string.Equals(request.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("FabricLoaderInstaller 只处理 fabric，收到：" + request.Loader);
            }

            if (string.IsNullOrWhiteSpace(request.LoaderVersion))
            {
                throw new ArgumentException("安装 Fabric 必须指定加载器版本。", nameof(request));
            }

            string profileUrl = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2}/profile/json",
                MetaBaseUrl,
                request.VersionId,
                request.LoaderVersion);

            progress?.Report(new InstallProgress("获取 Fabric 版本信息", 3, 0));
            string profile;

            try
            {
                profile = await _download.GetStringAsync(profileUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch when (_fallback != null)
            {
                // PCL ModNet 在部分网络/进程环境下会连不上 meta.fabricmc.net；
                // 该接口只是公开 JSON，直接用系统 HTTP 栈兜底即可。
                progress?.Report(new InstallProgress("PCL 下载失败，改走直连 Fabric 元数据", 4, 0));
                profile = await _fallback!.GetStringAsync(profileUrl, cancellationToken).ConfigureAwait(false);
            }

            JObject root = JObject.Parse(profile);
            string? id = root.Value<string>("id");
            string? inheritsFrom = root.Value<string>("inheritsFrom");

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("Fabric 返回的版本信息缺少 id。");
            }

            if (!string.Equals(inheritsFrom, request.VersionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Fabric 版本信息与请求的原版不一致（inheritsFrom=" + inheritsFrom + "）。");
            }

            // 0) 父版本（原版）必须存在。
            // Fabric 的 profile 只有"加载器 + inheritsFrom"，不带原版的 client jar、库和资源；
            // 父版本缺失时直接落 JSON，得到的实例能被扫到、点启动却起不来。
            string parentJson = Path.Combine(
                request.MinecraftDirectory, "versions", inheritsFrom!, inheritsFrom + ".json");

            if (!File.Exists(parentJson))
            {
                progress?.Report(new InstallProgress("先安装原版 " + inheritsFrom, 4, 0));

                await _vanilla.InstallAsync(
                    new InstallRequest(request.MinecraftDirectory, inheritsFrom!),
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            // 1) 下库（先下到临时文件、校验通过再落到最终位置）
            List<RemoteFile> libraries = BuildLibraryList(root, request.MinecraftDirectory);
            progress?.Report(new InstallProgress(
                "下载 Fabric 库（" + libraries.Count + " 个文件）", 10, 0));

            await DownloadAllAsync(libraries, progress, cancellationToken).ConfigureAwait(false);

            // 2) 库里都下完了才落版本 JSON：中途失败不会留下一个"能扫到但打不开"的半装实例
            string versionDirectory = Path.Combine(request.MinecraftDirectory, "versions", id!);
            Directory.CreateDirectory(versionDirectory);
            string jsonPath = Path.Combine(versionDirectory, id + ".json");
            File.WriteAllText(jsonPath, root.ToString());

            progress?.Report(new InstallProgress("Fabric 安装完成", 100, 0));
        }

        /// <summary>把 profile 里的 <c>libraries</c> 转成待下载文件列表。</summary>
        internal static List<RemoteFile> BuildLibraryList(JObject root, string minecraftDirectory)
        {
            List<RemoteFile> result = new List<RemoteFile>();
            JArray? libraries = root["libraries"] as JArray;

            if (libraries == null)
            {
                return result;
            }

            foreach (JToken token in libraries)
            {
                string? name = token.Value<string>("name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string? relativePath = MavenPath(name!);
                if (relativePath == null)
                {
                    continue;
                }

                string? url = token.Value<string>("url");
                string? explicitUrl = token.SelectToken("downloads.artifact.url")?.Value<string>();

                string? fullUrl = explicitUrl;
                if (string.IsNullOrEmpty(fullUrl) && !string.IsNullOrEmpty(url))
                {
                    fullUrl = url!.TrimEnd('/') + "/" + relativePath;
                }

                if (string.IsNullOrEmpty(fullUrl))
                {
                    continue;
                }

                string? sha1 = token.Value<string>("sha1")
                    ?? token.SelectToken("downloads.artifact.sha1")?.Value<string>();
                long size = token.Value<long?>("size")
                    ?? token.SelectToken("downloads.artifact.size")?.Value<long>()
                    ?? 0;

                result.Add(new RemoteFile(
                    fullUrl!,
                    Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar)),
                    sha1,
                    size));
            }

            return result;
        }

        /// <summary>把 Maven 坐标转成仓库内的相对路径；解析失败返回 null。</summary>
        public static string? MavenPath(string coordinates)
        {
            string[] parts = coordinates.Split(':');
            if (parts.Length < 3)
            {
                return null;
            }

            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];
            string? classifier = parts.Length > 3 ? parts[3] : null;

            string fileName = artifact + "-" + version + (classifier == null ? "" : "-" + classifier) + ".jar";

            return group.Replace('.', '/') + "/" + artifact + "/" + version + "/" + fileName;
        }

        private async Task DownloadAllAsync(
            List<RemoteFile> files,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken)
        {
            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RemoteFile file = files[index];
                string? directory = Path.GetDirectoryName(file.TargetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (IsUpToDate(file))
                {
                    continue;
                }

                // 先下到临时文件：中途断网/关窗口不会留下截断的 jar，被当成"已安装"
                string tempPath = file.TargetPath + ".nlmcl-tmp";

                try
                {
                    // Fabric 的 maven 没有国内镜像，普通下载会因超时/连接假死失败。
                    // 用带停滞检测的封装：只要文件还在变大就继续等，连续 30 秒不涨才重试。
                    try
                    {
                        await ResilientDownload
                            .DownloadAsync(
                                _download, file.Url, tempPath, cancellationToken, fallback: _fallback)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch when (_fallback != null)
                    {
                        progress?.Report(new InstallProgress(
                            "PCL 下载失败，改走直连：" + Path.GetFileName(file.TargetPath), 0, 0));
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await _fallback!
                            .DownloadAsync(file.Url, tempPath, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!string.IsNullOrEmpty(file.Sha1))
                    {
                        string actual = CryptographyUtils.ComputeFileHash(
                            tempPath, CryptographyUtils.HashMethod.Sha1);

                        if (!string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                "文件校验失败：" + file.Url + "（期望 " + file.Sha1 + "，实际 " + actual + "）");
                        }
                    }
                    else if (file.Size > 0 && new FileInfo(tempPath).Length != file.Size)
                    {
                        throw new InvalidDataException(
                            "文件大小不符：" + file.Url + "（期望 " + file.Size + "，实际 "
                            + new FileInfo(tempPath).Length + "）");
                    }

                    if (File.Exists(file.TargetPath))
                    {
                        File.Delete(file.TargetPath);
                    }

                    File.Move(tempPath, file.TargetPath);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }

                double percent = files.Count == 0 ? 100 : 10 + (index + 1) * 89.0 / files.Count;
                progress?.Report(new InstallProgress(
                    "下载 Fabric 库 " + (index + 1) + "/" + files.Count, percent, 0));
            }
        }

        private static bool IsUpToDate(RemoteFile file)
        {
            if (!File.Exists(file.TargetPath))
            {
                return false;
            }

            FileInfo info = new FileInfo(file.TargetPath);

            if (!string.IsNullOrEmpty(file.Sha1))
            {
                string actual = CryptographyUtils.ComputeFileHash(
                    file.TargetPath, CryptographyUtils.HashMethod.Sha1);

                return string.Equals(actual, file.Sha1, StringComparison.OrdinalIgnoreCase);
            }

            // 没有哈希：有大小就比大小；连大小都没有时，至少排除 0 字节的残缺文件
            if (file.Size > 0)
            {
                return info.Length == file.Size;
            }

            return info.Length > 0;
        }
    }
}

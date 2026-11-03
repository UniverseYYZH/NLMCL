using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>
    /// 预置 Forge / NeoForge 安装器需要的库。
    ///
    /// 为什么必须这么做：官方安装器 **自己**从 <c>maven.neoforged.net</c> /
    /// <c>maven.minecraftforge.net</c> 下载库，`--mirror` 参数只是切"赞助商镜像"，
    /// 不改下载地址（实测 82 次 connect timeout）。
    /// 但安装器会跳过本地已有的库文件——所以先把它们从国内镜像拉齐，
    /// 安装器就只剩本地解压/patch 了。
    ///
    /// 实测：预置 78 个文件后，安装器下载尝试从 82 次降到 0 次，58 秒装完。
    /// </summary>
    public sealed class LoaderLibrarySeeder
    {
        /// <summary>按顺序尝试：BMCLAPI → 阿里云公共仓库 → Maven Central。</summary>
        private static readonly string[] MavenBases =
        {
            "https://bmclapi2.bangbang93.com/maven/",
            "https://maven.aliyun.com/repository/public/",
            "https://repo1.maven.org/maven2/"
        };

        private readonly IDownloadService _download;

        public LoaderLibrarySeeder(IDownloadService download)
        {
            _download = download ?? throw new ArgumentNullException(nameof(download));
        }

        /// <summary>返回成功预置的文件数；缺失的（处理器产物）会被静默跳过。</summary>
        public async Task<int> SeedAsync(
            string installerJar,
            string minecraftDirectory,
            IProgress<InstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> paths = ReadLibraryPaths(installerJar);

            if (paths.Count == 0)
            {
                return 0;
            }

            string libraries = Path.Combine(minecraftDirectory, "libraries");
            Directory.CreateDirectory(libraries);

            int seeded = 0;

            for (int index = 0; index < paths.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relative = paths[index];
                string target = Path.Combine(libraries, relative.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(target) && new FileInfo(target).Length > 0)
                {
                    seeded++;
                    continue;
                }

                progress?.Report(new InstallProgress(
                    "预置库 " + (index + 1) + "/" + paths.Count, 10 + (int)(10.0 * index / paths.Count), 0));

                if (await TryDownloadAsync(relative, target, cancellationToken).ConfigureAwait(false))
                {
                    seeded++;
                }
            }

            return seeded;
        }

        /// <summary>从安装器 jar 里读出所有库的相对路径（libraries 根下）。</summary>
        internal static IReadOnlyList<string> ReadLibraryPaths(string installerJar)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(installerJar))
                {
                    JObject? profile = ReadJson(archive, "install_profile.json");
                    JObject? version = ReadJson(archive, "version.json");

                    if (version?["libraries"] is JArray versionLibraries)
                    {
                        foreach (JToken library in versionLibraries)
                        {
                            AddArtifactPath(paths, library);
                        }
                    }

                    if (profile?["libraries"] is JArray profileLibraries)
                    {
                        foreach (JToken library in profileLibraries)
                        {
                            if (!AddArtifactPath(paths, library))
                            {
                                // 只给了 Maven 坐标（name 字段），自己拼路径
                                string? coord = library.Value<string>("name");
                                string? path = ArtifactPath(coord);
                                if (path != null)
                                {
                                    paths.Add(path);
                                }
                            }
                        }
                    }

                    // data 里的值可能是 [group:artifact:version:classifier@ext]（例如 clientdata lzma）
                    if (profile?["data"] is JObject data)
                    {
                        foreach (JProperty entry in data.Properties())
                        {
                        foreach (JProperty side in ((entry.Value as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()))
                            {
                            string? value = side.Value.Type == JTokenType.String ? side.Value.Value<string>() : null;
                                string? path = BracketedArtifactPath(value);
                                if (path != null)
                                {
                                    paths.Add(path);
                                }
                            }
                        }
                    }
                }
            }
            catch (InvalidDataException)
            {
                // 不是合法 zip（下载损坏、或单测里的假 jar）：交给安装器自己处理
                return Array.Empty<string>();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }

            return paths.ToList();
        }

        private static JObject? ReadJson(ZipArchive archive, string name)
        {
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                item => string.Equals(item.FullName, name, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                return null;
            }

            using (Stream stream = entry.Open())
            using (StreamReader reader = new StreamReader(stream))
            {
                try
                {
                    return JObject.Parse(reader.ReadToEnd());
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private static bool AddArtifactPath(HashSet<string> paths, JToken library)
        {
            string? path = library.SelectToken("downloads.artifact.path")?.Value<string>();

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            paths.Add(path!.TrimStart('/'));
            return true;
        }

        /// <summary>[group:artifact:version:classifier@ext] → 相对路径。</summary>
        internal static string? BracketedArtifactPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value!.Trim();

            if (!text.StartsWith("[", StringComparison.Ordinal) || !text.EndsWith("]", StringComparison.Ordinal))
            {
                return null;
            }

            return ArtifactPath(text.Substring(1, text.Length - 2));
        }

        /// <summary>group:artifact:version[:classifier][@ext] → group/artifact/version/artifact-version[-classifier].ext。</summary>
        internal static string? ArtifactPath(string? coordinate)
        {
            if (string.IsNullOrWhiteSpace(coordinate))
            {
                return null;
            }

            string text = coordinate!.Trim();
            string extension = "jar";

            int at = text.IndexOf('@');
            if (at >= 0)
            {
                extension = text.Substring(at + 1);
                text = text.Substring(0, at);
            }

            string[] parts = text.Split(':');

            if (parts.Length < 3)
            {
                return null;
            }

            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length > 3 && parts[3].Length > 0 ? "-" + parts[3] : string.Empty;

            return group.Replace('.', '/') + "/" + artifact + "/" + version + "/"
                + artifact + "-" + version + classifier + "." + extension;
        }

        /// <summary>按镜像顺序尝试；都失败就跳过（处理器产物本来就不在仓库里）。</summary>
        private async Task<bool> TryDownloadAsync(
            string relativePath, string target, CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            foreach (string baseUrl in MavenBases)
            {
                try
                {
                    await _download
                        .DownloadFileAsync(baseUrl + relativePath, target, cancellationToken)
                        .ConfigureAwait(false);

                    if (File.Exists(target) && new FileInfo(target).Length > 0)
                    {
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // 换下一个镜像
                }
            }

            return false;
        }
    }
}

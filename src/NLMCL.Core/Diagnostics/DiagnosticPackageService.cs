using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;
using NLMCL.Core.Crash;

namespace NLMCL.Core.Diagnostics
{
    /// <summary>
    /// 生成玩家可发送的诊断包。只读取显式传入的实例路径和 DataRoot 下的启动日志，
    /// 永远不读取 accounts.json。
    /// </summary>
    public sealed class DiagnosticPackageService : IDiagnosticPackageService
    {
        private const long MaxTextBytes = 2 * 1024 * 1024;

        private readonly IAppPaths _paths;

        public DiagnosticPackageService(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public Task<DiagnosticPackageResult> CreateAsync(
            DiagnosticPackageRequest request, string destinationZip, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(destinationZip))
            {
                throw new ArgumentException("诊断包路径不能为空。", nameof(destinationZip));
            }

            return Task.Run(() => CreateCore(request, destinationZip, cancellationToken), cancellationToken);
        }

        private DiagnosticPackageResult CreateCore(
            DiagnosticPackageRequest request, string destinationZip, CancellationToken cancellationToken)
        {
            string? folder = Path.GetDirectoryName(destinationZip);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string temporary = destinationZip + ".tmp";
            TryDelete(temporary);

            List<string> missing = new List<string>();

            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? latestLog = FindLatestLog(request);
                    AddFile(zip, "logs/latest.log", latestLog, missing, "latest.log");

                    string? crashReport = CrashAnalyzer.FindNewestCrashReport(
                        request.MinecraftDirectory, request.VersionFolder);
                    AddFile(zip, "logs/crash-report.txt", crashReport, missing, "最近崩溃报告");

                    AddFile(zip, "logs/last-launch.txt",
                        Path.Combine(_paths.DataRoot, "last-launch.txt"), missing, "last-launch.txt");

                    string? appCrash = FindNewestAppCrash();
                    AddFile(zip, "logs/launcher-crash.log", appCrash, missing, "启动器异常日志");

                    AddText(zip, "instance/mods.txt", BuildModList(request.ModsDirectory), missing,
                        "当前实例 Mod 列表");

                    AddText(zip, "instance/settings.txt", BuildSettings(request.InstanceSettingsFile),
                        missing, "实例设置");

                    AddText(zip, "environment.txt", BuildEnvironment(request), missing);

                    AddText(zip, "README.txt", BuildReadme(request, missing), missing);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(destinationZip))
                {
                    File.Delete(destinationZip);
                }

                File.Move(temporary, destinationZip);
                return new DiagnosticPackageResult(destinationZip, missing);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        private static string? FindLatestLog(DiagnosticPackageRequest request)
        {
            string[] candidates =
            {
                Path.Combine(request.GameDirectory, "logs", "latest.log"),
                Path.Combine(request.MinecraftDirectory, "versions", request.VersionFolder, "logs", "latest.log"),
                Path.Combine(request.MinecraftDirectory, "logs", "latest.log")
            };

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(File.Exists);
        }

        private string? FindNewestAppCrash()
        {
            string folder = Path.Combine(_paths.DataRoot, "logs");
            if (!Directory.Exists(folder))
            {
                return null;
            }

            return Directory.GetFiles(folder, "crash-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static string BuildModList(string modsDirectory)
        {
            if (!Directory.Exists(modsDirectory))
            {
                return "Mods 目录不存在：" + modsDirectory;
            }

            string[] files = Directory.GetFiles(modsDirectory, "*.jar*")
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                return "没有找到根级 Mod（mods 目录：" + modsDirectory + "）";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("目录：" + modsDirectory);
            builder.AppendLine("文件名\t状态\t大小(bytes)\t修改时间");

            foreach (string file in files)
            {
                FileInfo info = new FileInfo(file);
                bool enabled = file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                builder.Append(info.Name).Append('\t')
                    .Append(enabled ? "启用" : "禁用").Append('\t')
                    .Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .AppendLine(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string BuildSettings(string settingsFile)
        {
            if (!File.Exists(settingsFile))
            {
                return "实例设置文件不存在：" + settingsFile;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(settingsFile, Encoding.UTF8));

                // 只输出白名单字段。即使以后往 nlmcl-settings.json 里加了敏感字段，
                // 也不会因为“顺手整个复制”泄漏。
                JObject safe = new JObject
                {
                    ["memoryMb"] = root.Value<int?>("memoryMb") ?? InstanceSettings.DefaultMemoryMb,
                    ["javaPath"] = root.Value<string>("javaPath"),
                    ["extraArguments"] = root.Value<string>("extraArguments"),
                    ["serverAddress"] = root.Value<string>("serverAddress"),
                    ["windowWidth"] = root.Value<int?>("windowWidth") ?? 0,
                    ["windowHeight"] = root.Value<int?>("windowHeight") ?? 0,
                    ["fullscreen"] = root.Value<bool?>("fullscreen") ?? false,
                    ["useProxy"] = root.Value<bool?>("useProxy") ?? false,
                    ["proxyHost"] = root.Value<string>("proxyHost"),
                    ["proxyPort"] = root.Value<int?>("proxyPort") ?? 0,
                    ["softwareOpenGl"] = root.Value<bool?>("softwareOpenGl") ?? false
                };

                return string.IsNullOrEmpty(safe["javaPath"]?.ToString())
                    ? safe.ToString()
                    : "实例设置（白名单字段）：" + Environment.NewLine + safe;
            }
            catch (Exception ex)
            {
                return "实例设置无法解析：" + ex.Message;
            }
        }

        private static string BuildEnvironment(DiagnosticPackageRequest request)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("NLMCL 诊断环境");
            builder.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            builder.AppendLine("启动器版本: " + (string.IsNullOrWhiteSpace(request.AppVersion) ? "未知" : request.AppVersion));
            builder.AppendLine("系统: " + Environment.OSVersion);
            builder.AppendLine("64 位进程: " + Environment.Is64BitProcess);
            builder.AppendLine("CLR: " + Environment.Version);
            builder.AppendLine("Minecraft 目录: " + request.MinecraftDirectory);
            builder.AppendLine("实例目录名: " + request.VersionFolder);
            builder.AppendLine("实际游戏目录: " + request.GameDirectory);
            builder.AppendLine("Mods 目录: " + request.ModsDirectory);
            builder.AppendLine();
            builder.AppendLine("Java:");

            if (request.JavaHomes.Count == 0)
            {
                builder.AppendLine("- 没有探测到 Java");
            }
            else
            {
                foreach (string home in request.JavaHomes)
                {
                    builder.AppendLine("- " + home);
                }
            }

            return builder.ToString();
        }

        private static string BuildReadme(DiagnosticPackageRequest request, IReadOnlyList<string> missing)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("NLMCL 诊断包");
            builder.AppendLine("============");
            builder.AppendLine();
            builder.AppendLine("这个压缩包由 NLMCL 日志页生成，用于把排障材料一次发给维护者。");
            builder.AppendLine("不包含 accounts.json、accessToken、refreshToken、clientId 或 xuid。");
            builder.AppendLine("日志中的 UUID 只保留前 8 位，令牌类参数已替换为 <已隐藏>。");
            builder.AppendLine();
            builder.AppendLine("文件说明：");
            builder.AppendLine("- logs/latest.log：最近一次游戏的日志（过大时只保留末尾 2 MB）。");
            builder.AppendLine("- logs/crash-report.txt：最近一份 Minecraft 崩溃报告（可能不存在）。");
            builder.AppendLine("- logs/last-launch.txt：启动器记录的启动命令与退出码（已脱敏）。");
            builder.AppendLine("- logs/launcher-crash.log：启动器自身最近的未处理异常（可能不存在）。");
            builder.AppendLine("- instance/mods.txt：当前实例根级 Mod 的文件名、启用状态、大小。");
            builder.AppendLine("- instance/settings.txt：内存、窗口、Java、参数、代理开关等白名单设置。");
            builder.AppendLine("- environment.txt：启动器版本、系统、CLR、Java 列表。");
            builder.AppendLine();
            builder.AppendLine("实例：" + request.VersionFolder);
            builder.AppendLine("游戏目录：" + request.GameDirectory);

            if (missing.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("缺失项（不影响打开压缩包）：");
                foreach (string item in missing)
                {
                    builder.AppendLine("- " + item);
                }
            }

            return builder.ToString();
        }

        private static void AddFile(
            ZipArchive zip, string entryName, string? path, List<string> missing, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                missing.Add(label);
                return;
            }

            AddText(zip, entryName, ReadLimited(path), missing);
        }

        private static void AddText(
            ZipArchive zip, string entryName, string text, List<string> missing, string? missingLabel = null)
        {
            if (missingLabel != null && text.StartsWith("实例设置文件不存在", StringComparison.Ordinal))
            {
                missing.Add(missingLabel);
            }

            if (missingLabel != null && text.StartsWith("Mods 目录不存在", StringComparison.Ordinal))
            {
                missing.Add(missingLabel);
            }

            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(DiagnosticRedactor.Redact(text ?? string.Empty));
            }
        }

        private static string ReadLimited(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length <= MaxTextBytes)
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        return reader.ReadToEnd();
                    }
                }

                stream.Seek(-MaxTextBytes, SeekOrigin.End);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return "[已截断：仅保留最后 2 MB]" + Environment.NewLine + reader.ReadToEnd();
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static class DiagnosticRedactor
    {
        private static readonly Regex SecretArgument = new Regex(
            @"(?i)(--(?:accessToken|clientId|xuid)\s+)\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex JsonSecret = new Regex(
            @"""(?i:(?:accessToken|refreshToken|clientId|xuid|access_token|refresh_token))""\s*:\s*""[^""]*""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Bearer = new Regex(
            @"(?i)(Bearer\s+)\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Uuid = new Regex(
            @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string result = SecretArgument.Replace(text, "$1<已隐藏>");
            result = JsonSecret.Replace(result, m =>
            {
                int colon = m.Value.IndexOf(':');
                string key = colon < 0 ? "\"secret\"" : m.Value.Substring(0, colon).Trim();
                return key + " : \"<已隐藏>\"";
            });
            result = Bearer.Replace(result, "$1<已隐藏>");
            result = Uuid.Replace(result, m =>
                m.Value.Substring(0, 8) + "-…");
            return result;
        }
    }
}

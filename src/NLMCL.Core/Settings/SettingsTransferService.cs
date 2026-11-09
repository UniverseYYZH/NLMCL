using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// 设置导入导出。只导出可移植的设置：
    /// 主题、缩放、下载线程、默认内存、镜像开关、默认 JVM/游戏参数。
    ///
    /// 刻意不导出：游戏目录、微软 client id、默认 Java 路径、WizardCompleted。
    /// 这些是本机路径/状态，带到另一台电脑只会造成“配置看起来对、实际打不开”。
    /// </summary>
    public sealed class SettingsTransferService : ISettingsTransferService
    {
        private const string SchemaName = "nlmcl-settings";
        private const int SchemaVersion = 1;

        private readonly IAppSettingsService _settings;

        public SettingsTransferService(IAppSettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task ExportAsync(string filePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("导出路径为空。", nameof(filePath));
            }

            AppSettings settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);

            JObject document = new JObject
            {
                ["schema"] = SchemaName,
                ["version"] = SchemaVersion,
                ["exportedAt"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                ["settings"] = new JObject
                {
                    ["theme"] = settings.Theme,
                    ["uiScale"] = settings.UiScale,
                    ["uiScaleMaximized"] = settings.UiScaleMaximized,
                    ["downloadThreads"] = settings.DownloadThreads,
                    ["defaultMemoryMb"] = settings.DefaultMemoryMb,
                    ["useMirror"] = settings.UseMirror,
                    ["defaultJvmArgs"] = settings.DefaultJvmArgs,
                    ["defaultGameArgs"] = settings.DefaultGameArgs
                }
            };

            string? folder = Path.GetDirectoryName(Path.GetFullPath(filePath));

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder!);
            }

            string temp = filePath + ".tmp";
            File.WriteAllText(temp, document.ToString(Formatting.Indented), new UTF8Encoding(false));

            if (File.Exists(filePath))
            {
                File.Replace(temp, filePath, null);
            }
            else
            {
                File.Move(temp, filePath);
            }
        }

        public async Task<SettingsImportResult> ImportAsync(
            string filePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("找不到设置文件。", filePath);
            }

            JObject document;

            try
            {
                document = JObject.Parse(File.ReadAllText(filePath, Encoding.UTF8));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("不是有效的 JSON 文件。", ex);
            }

            if (!string.Equals(document.Value<string>("schema"), SchemaName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("不是 NLMCL 设置文件（缺少 schema）。");
            }

            int version = document.Value<int?>("version") ?? 0;

            if (version <= 0 || version > SchemaVersion)
            {
                throw new InvalidDataException("不支持的设置文件版本：" + version);
            }

            JObject? source = document["settings"] as JObject;

            if (source == null)
            {
                throw new InvalidDataException("设置文件里没有 settings 对象。");
            }

            List<string> applied = new List<string>();
            List<string> warnings = new List<string>();

            string? theme = null;
            bool setTheme = false;

            if (source.TryGetValue("theme", out JToken? themeToken))
            {
                string? value = themeToken.Type == JTokenType.String ? themeToken.ToString() : null;

                if (value == "light" || value == "dark" || value == "system")
                {
                    theme = value;
                    setTheme = true;
                }
                else
                {
                    warnings.Add("主题值无效，已跳过。");
                }
            }

            double? uiScale = ReadDouble(source, "uiScale", warnings);
            double? uiScaleMaximized = ReadDouble(source, "uiScaleMaximized", warnings);
            int? downloadThreads = ReadInt(source, "downloadThreads", warnings);
            int? defaultMemoryMb = ReadInt(source, "defaultMemoryMb", warnings);
            bool? useMirror = ReadBool(source, "useMirror", warnings);
            bool setJvm;
            bool setGame;
            string? jvmArgs = ReadString(source, "defaultJvmArgs", warnings, out setJvm);
            string? gameArgs = ReadString(source, "defaultGameArgs", warnings, out setGame);

            if (uiScale.HasValue)
            {
                double clamped = Clamp(uiScale.Value, 0.8, 1.5);
                if (Math.Abs(clamped - uiScale.Value) > 0.0001)
                {
                    warnings.Add("窗口缩放超出 80%–150%，已夹取。");
                }

                uiScale = clamped;
            }

            if (uiScaleMaximized.HasValue)
            {
                double clamped = Clamp(uiScaleMaximized.Value, 1.0, 2.0);
                if (Math.Abs(clamped - uiScaleMaximized.Value) > 0.0001)
                {
                    warnings.Add("最大化缩放超出 100%–200%，已夹取。");
                }

                uiScaleMaximized = clamped;
            }

            if (downloadThreads.HasValue)
            {
                downloadThreads = Math.Max(1, Math.Min(16, downloadThreads.Value));
            }

            if (defaultMemoryMb.HasValue)
            {
                defaultMemoryMb = Math.Max(512, Math.Min(65536, defaultMemoryMb.Value));
            }

            if (setTheme || uiScale.HasValue || uiScaleMaximized.HasValue || downloadThreads.HasValue
                || defaultMemoryMb.HasValue || useMirror.HasValue || setJvm || setGame)
            {
                await _settings.UpdateAsync(settings =>
                {
                    if (setTheme)
                    {
                        settings.Theme = theme!;
                    }

                    if (uiScale.HasValue)
                    {
                        settings.UiScale = uiScale.Value;
                    }

                    if (uiScaleMaximized.HasValue)
                    {
                        settings.UiScaleMaximized = uiScaleMaximized.Value;
                    }

                    if (downloadThreads.HasValue)
                    {
                        settings.DownloadThreads = downloadThreads.Value;
                    }

                    if (defaultMemoryMb.HasValue)
                    {
                        settings.DefaultMemoryMb = defaultMemoryMb.Value;
                    }

                    if (useMirror.HasValue)
                    {
                        settings.UseMirror = useMirror.Value;
                    }

                    if (setJvm)
                    {
                        settings.DefaultJvmArgs = jvmArgs;
                    }

                    if (setGame)
                    {
                        settings.DefaultGameArgs = gameArgs;
                    }
                }, cancellationToken).ConfigureAwait(false);
            }

            if (setTheme) applied.Add("主题");
            if (uiScale.HasValue) applied.Add("窗口缩放");
            if (uiScaleMaximized.HasValue) applied.Add("最大化缩放");
            if (downloadThreads.HasValue) applied.Add("下载线程");
            if (defaultMemoryMb.HasValue) applied.Add("默认内存");
            if (useMirror.HasValue) applied.Add("下载镜像");
            if (setJvm) applied.Add("默认 JVM 参数");
            if (setGame) applied.Add("默认游戏参数");

            if (applied.Count == 0)
            {
                warnings.Add("没有可导入的字段。");
            }

            return new SettingsImportResult(applied, warnings);
        }

        private static double? ReadDouble(JObject source, string name, List<string> warnings)
        {
            if (!source.TryGetValue(name, out JToken? token))
            {
                return null;
            }

            try
            {
                return token.Value<double?>();
            }
            catch (Exception ex) when (
                ex is FormatException || ex is InvalidCastException || ex is ArgumentException)
            {
                warnings.Add(name + " 不是数字，已跳过。");
                return null;
            }
        }

        private static int? ReadInt(JObject source, string name, List<string> warnings)
        {
            if (!source.TryGetValue(name, out JToken? token))
            {
                return null;
            }

            try
            {
                return token.Value<int?>();
            }
            catch (Exception ex) when (
                ex is FormatException || ex is InvalidCastException || ex is ArgumentException)
            {
                warnings.Add(name + " 不是整数，已跳过。");
                return null;
            }
        }

        private static bool? ReadBool(JObject source, string name, List<string> warnings)
        {
            if (!source.TryGetValue(name, out JToken? token))
            {
                return null;
            }

            try
            {
                return token.Value<bool?>();
            }
            catch (Exception ex) when (
                ex is FormatException || ex is InvalidCastException || ex is ArgumentException)
            {
                warnings.Add(name + " 不是布尔值，已跳过。");
                return null;
            }
        }

        private static string? ReadString(
            JObject source, string name, List<string> warnings, out bool present)
        {
            present = false;

            if (!source.TryGetValue(name, out JToken? token))
            {
                return null;
            }

            if (token.Type == JTokenType.Null)
            {
                present = true;
                return null;
            }

            if (token.Type != JTokenType.String)
            {
                warnings.Add(name + " 不是字符串，已跳过。");
                return null;
            }

            present = true;
            string value = token.ToString().Trim();
            return value.Length == 0 ? null : value;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}

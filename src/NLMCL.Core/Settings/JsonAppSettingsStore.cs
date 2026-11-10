using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLMCL.Abstractions;

namespace NLMCL.Core.Settings
{
    /// <summary>把应用设置存成 <c>%APPDATA%\NLMCL\settings.json</c>。</summary>
    public sealed class JsonAppSettingsStore : IAppSettingsStore
    {
        private readonly IAppPaths _paths;

        public JsonAppSettingsStore(IAppPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            string file = _paths.SettingsFile;

            if (!File.Exists(file))
            {
                return Task.FromResult(new AppSettings());
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
                int schemaVersion = root.Value<int?>("schemaVersion") ?? 1;

                AppSettings settings = new AppSettings
                {
                    GameDirectory = root.Value<string>("gameDirectory"),
                    GameDirectories = (root["gameDirectories"] as JArray)?
                        .Values<string>()
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!)
                        .ToList() ?? new List<string>(),
                    MicrosoftClientId = root.Value<string>("microsoftClientId"),
                    Theme = root.Value<string>("theme") ?? "light",
                    ReduceMotion = root.Value<bool?>("reduceMotion") ?? false,
                    UiScale = root.Value<double?>("uiScale") ?? 1.0,
                    UiScaleMaximized = root.Value<double?>("uiScaleMaximized") ?? 1.5,
                    DownloadThreads = root.Value<int?>("downloadThreads") ?? 6,
                    DownloadSpeedLimitMb = root.Value<int?>("downloadSpeedLimitMb") ?? 0,
                    DefaultMemoryMb = root.Value<int?>("defaultMemoryMb") ?? 2048,
                    UseMirror = root.Value<bool?>("useMirror") ?? true,
                    DefaultJvmArgs = root.Value<string>("defaultJvmArgs"),
                    DefaultGameArgs = root.Value<string>("defaultGameArgs"),
                    DefaultJavaPath = root.Value<string>("defaultJavaPath"),
                    WizardCompleted = root.Value<bool?>("wizardCompleted") ?? false,
                    DefaultPage = root.Value<string>("defaultPage") ?? "versions",
                    LastPage = root.Value<string>("lastPage") ?? "versions",
                    LastLaunched = new Dictionary<string, DateTimeOffset>(
                        root["lastLaunched"]?.ToObject<Dictionary<string, DateTimeOffset>>()
                            ?? new Dictionary<string, DateTimeOffset>(),
                        StringComparer.OrdinalIgnoreCase),
                    AutoSnapshotBeforeLaunch = root.Value<bool?>("autoSnapshotBeforeLaunch") ?? false,
                    SnapshotRetention = root.Value<int?>("snapshotRetention") ?? 5,
                    CloseBehavior = root.Value<string>("closeBehavior") ?? "exit"
                };

                if (schemaVersion > AppSettings.CurrentSchemaVersion)
                {
                    TryKeepNewerCopy(file);
                    settings.SchemaVersion = schemaVersion;
                    settings.IsReadOnly = true;
                    settings.ReadOnlyReason =
                        "设置文件由更新版本的 NLMCL 创建（schema " + schemaVersion +
                        "），已备份到 " + file + ".newer；当前版本不会覆盖它。";
                }
                else
                {
                    // 旧配置没有 schemaVersion，或版本更低：读入后按当前结构处理。
                    settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
                }

                return Task.FromResult(settings);
            }
            catch (JsonException)
            {
                // 坏配置不该拦住启动；但要留下证据，别让用户以为"设置自己没了"
                TryKeepBrokenCopy(file);
                return Task.FromResult(new AppSettings());
            }
            catch (IOException)
            {
                return Task.FromResult(new AppSettings());
            }
        }

        /// <summary>把解析失败的配置另存一份，避免下一次保存直接把它覆盖掉。</summary>
        private static void TryKeepBrokenCopy(string file)
        {
            try
            {
                File.Copy(file, file + ".broken", overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>把更高版本的配置另存一份，避免被当前程序覆盖。</summary>
        private static void TryKeepNewerCopy(string file)
        {
            try
            {
                File.Copy(file, file + ".newer", overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            // 更高版本创建的配置：不写入，保留原文件。调用方可通过
            // IsReadOnly / ReadOnlyReason 提示用户。
            if (settings.IsReadOnly
                || settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                if (File.Exists(_paths.SettingsFile))
                {
                    TryKeepNewerCopy(_paths.SettingsFile);
                }

                return Task.CompletedTask;
            }

            Directory.CreateDirectory(_paths.DataRoot);

            JObject root = new JObject
            {
                ["schemaVersion"] = AppSettings.CurrentSchemaVersion,
                ["gameDirectory"] = settings.GameDirectory,
                ["gameDirectories"] = new JArray(settings.GameDirectories ?? new List<string>()),
                ["microsoftClientId"] = settings.MicrosoftClientId,
                ["theme"] = settings.Theme,
                ["reduceMotion"] = settings.ReduceMotion,
                ["uiScale"] = settings.UiScale,
                ["uiScaleMaximized"] = settings.UiScaleMaximized,
                ["downloadThreads"] = settings.DownloadThreads,
                ["downloadSpeedLimitMb"] = settings.DownloadSpeedLimitMb,
                ["defaultMemoryMb"] = settings.DefaultMemoryMb,
                ["useMirror"] = settings.UseMirror,
                ["defaultJvmArgs"] = settings.DefaultJvmArgs,
                ["defaultGameArgs"] = settings.DefaultGameArgs,
                ["defaultJavaPath"] = settings.DefaultJavaPath,
                ["wizardCompleted"] = settings.WizardCompleted,
                ["defaultPage"] = settings.DefaultPage,
                ["lastPage"] = settings.LastPage,
                ["lastLaunched"] = JObject.FromObject(
                    settings.LastLaunched ?? new Dictionary<string, DateTimeOffset>()),
                ["autoSnapshotBeforeLaunch"] = settings.AutoSnapshotBeforeLaunch,
                ["snapshotRetention"] = settings.SnapshotRetention,
                ["closeBehavior"] = settings.CloseBehavior
            };

            // 原子替换：先写临时文件，再换名。直接 WriteAllText 会在写到一半时被截断，
            // 崩溃/断电后留下半截 JSON，下次启动就"设置全丢"。
            string text = root.ToString(Formatting.Indented);
            string temp = _paths.SettingsFile + ".tmp";

            File.WriteAllText(temp, text, Encoding.UTF8);

            if (File.Exists(_paths.SettingsFile))
            {
                File.Replace(temp, _paths.SettingsFile, null);
            }
            else
            {
                File.Move(temp, _paths.SettingsFile);
            }

            return Task.CompletedTask;
        }
    }
}

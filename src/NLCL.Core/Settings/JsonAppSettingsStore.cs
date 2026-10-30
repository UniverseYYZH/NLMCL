using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLCL.Abstractions;

namespace NLCL.Core.Settings
{
    /// <summary>把应用设置存成 <c>%APPDATA%\NLCL\settings.json</c>。</summary>
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

                return Task.FromResult(new AppSettings
                {
                    GameDirectory = root.Value<string>("gameDirectory"),
                    MicrosoftClientId = root.Value<string>("microsoftClientId")
                });
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

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Directory.CreateDirectory(_paths.DataRoot);

            JObject root = new JObject
            {
                ["gameDirectory"] = settings.GameDirectory,
                ["microsoftClientId"] = settings.MicrosoftClientId
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

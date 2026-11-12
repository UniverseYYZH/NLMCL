using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLMCL.Abstractions;

namespace NLMCL.Core.Installing
{
    /// <summary>把下载中心的未完成安装任务写到数据目录，应用重启后还能继续。</summary>
    public sealed class JsonDownloadJobStore : IDownloadJobStore
    {
        private readonly string _path;

        public JsonDownloadJobStore(IAppPaths paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            _path = Path.Combine(paths.DataRoot, "download-job.json");
        }

        public Task<DownloadJob?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!File.Exists(_path))
                    {
                        return null;
                    }

                    string json = File.ReadAllText(_path);
                    DownloadJob? job = JsonConvert.DeserializeObject<DownloadJob>(json);

                    return string.IsNullOrWhiteSpace(job?.VersionId) ? null : job;
                }
                catch (IOException)
                {
                    return null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }, cancellationToken);
        }

        public Task SaveAsync(DownloadJob job, CancellationToken cancellationToken)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                File.WriteAllText(temporary, JsonConvert.SerializeObject(job, Formatting.Indented));

                try
                {
                    if (File.Exists(_path))
                    {
                        File.Replace(temporary, _path, null);
                    }
                    else
                    {
                        File.Move(temporary, _path);
                    }
                }
                catch (IOException)
                {
                    File.Copy(temporary, _path, true);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
            }, cancellationToken);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(_path))
                    {
                        File.Delete(_path);
                    }
                }
                catch (IOException)
                {
                }
            }, cancellationToken);
        }
    }
}

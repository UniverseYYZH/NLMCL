using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class HttpFileDownloaderResumeTests : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _baseUrl;
        private readonly string _workDir;
        private readonly byte[] _payload;

        public HttpFileDownloaderResumeTests()
        {
            int port = FreePort();
            _baseUrl = "http://127.0.0.1:" + port + "/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();

            _payload = new byte[8 * 1024 * 1024];
            for (int i = 0; i < _payload.Length; i++)
            {
                _payload[i] = (byte)(i % 251);
            }

            _workDir = Path.Combine(Path.GetTempPath(), "nlmcl-resume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);

            _ = Task.Run(ServeAsync);
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                Directory.Delete(_workDir, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public async Task Cancelling_then_resuming_reuses_partial_parts()
        {
            HttpFileDownloader downloader = new HttpFileDownloader();
            string target = Path.Combine(_workDir, "payload.bin");
            string partsDirectory = target + ".parts";

            using CancellationTokenSource cancel = new CancellationTokenSource();
            Task first = downloader.DownloadAsync(_baseUrl + "payload.bin", target, cancel.Token);

            await WaitUntilAsync(
                () => Directory.Exists(partsDirectory)
                    && Directory.GetFiles(partsDirectory, "*.part").Any(file => new FileInfo(file).Length > 0),
                TimeSpan.FromSeconds(5));

            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            long[] partialSizes = Directory.GetFiles(partsDirectory, "*.part")
                .Select(file => new FileInfo(file).Length)
                .ToArray();
            Assert.NotEmpty(partialSizes);
            Assert.Contains(partialSizes, size => size > 0);

            await downloader.DownloadAsync(_baseUrl + "payload.bin", target, CancellationToken.None);

            Assert.Equal(_payload, File.ReadAllBytes(target));
            Assert.False(Directory.Exists(partsDirectory));
        }

        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(20).ConfigureAwait(false);
            }

            Assert.True(condition(), "等待分块文件超时。");
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    await ServeOneAsync(context).ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                }
                catch (IOException)
                {
                }
                finally
                {
                    try
                    {
                        context.Response.Close();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
        }

        private async Task ServeOneAsync(HttpListenerContext context)
        {
            context.Response.AddHeader("Accept-Ranges", "bytes");
            context.Response.ContentType = "application/octet-stream";

            long start = 0;
            long end = _payload.Length - 1;
            string? range = context.Request.Headers["Range"];

            if (!string.IsNullOrEmpty(range) && range!.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = range.Substring("bytes=".Length).Split('-');

                if (parts.Length == 2)
                {
                    long.TryParse(parts[0], out start);
                    if (!long.TryParse(parts[1], out end))
                    {
                        end = _payload.Length - 1;
                    }

                    end = Math.Min(end, _payload.Length - 1);
                }

                context.Response.StatusCode = 206;
                context.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + _payload.Length);
            }

            long count = Math.Max(0, end - start + 1);
            context.Response.ContentLength64 = count;

            if (string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            byte[] buffer = new byte[64 * 1024];
            long offset = start;

            while (offset <= end)
            {
                int take = (int)Math.Min(buffer.Length, end - offset + 1);
                Buffer.BlockCopy(_payload, (int)offset, buffer, 0, take);
                await context.Response.OutputStream.WriteAsync(buffer, 0, take).ConfigureAwait(false);
                await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
                offset += take;
                await Task.Delay(5).ConfigureAwait(false);
            }
        }
    }
}

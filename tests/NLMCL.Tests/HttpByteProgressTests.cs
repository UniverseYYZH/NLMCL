using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;
using NLMCL.Core.Installing;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class HttpByteProgressTests : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _baseUrl;
        private readonly string _workDir;
        private readonly byte[] _payload;

        public HttpByteProgressTests()
        {
            int port = FreePort();
            _baseUrl = "http://127.0.0.1:" + port + "/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();

            _payload = new byte[512 * 1024];
            for (int i = 0; i < _payload.Length; i++)
            {
                _payload[i] = (byte)(i % 251);
            }

            _workDir = Path.Combine(Path.GetTempPath(), "nlmcl-byte-" + Guid.NewGuid().ToString("N"));
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
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength64 = _payload.Length;

                    if (!string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        await context.Response.OutputStream
                            .WriteAsync(_payload, 0, _payload.Length)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        [Fact]
        public async Task Reports_received_and_total_bytes()
        {
            List<FileDownloadProgress> reports = new List<FileDownloadProgress>();
            HttpFileDownloader downloader = new HttpFileDownloader();
            string target = Path.Combine(_workDir, "payload.bin");

            await downloader.DownloadAsync(
                _baseUrl + "payload.bin",
                target,
                new InlineProgress(reports.Add),
                CancellationToken.None);

            FileDownloadProgress last = reports[reports.Count - 1];

            Assert.Equal(_payload.Length, new FileInfo(target).Length);
            Assert.Equal(_payload.Length, last.Received);
            Assert.Equal(_payload.Length, last.Total);
            Assert.Contains(reports, item => item.Received > 0 && item.Total == _payload.Length);
        }

        private sealed class InlineProgress : IProgress<FileDownloadProgress>
        {
            private readonly Action<FileDownloadProgress> _apply;

            public InlineProgress(Action<FileDownloadProgress> apply)
            {
                _apply = apply;
            }

            public void Report(FileDownloadProgress value) => _apply(value);
        }
    }
}

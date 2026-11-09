using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NLMCL.Abstractions;
using NLMCL.Core.Pcl.Vb;
using Xunit;
namespace NLMCL.Tests
{
    /// <summary>
    /// 用本地 HttpListener 验证 PCL 的 ModNet 真的能下载——不访问外网。
    /// 这是 M3-A 的验收：下载栈（含 UA/Referer 设置、镜像前缀处理、重试）跑通了。
    /// </summary>
    public class PclDownloadServiceTests : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _baseUrl;
        private readonly string _workDir;

        public PclDownloadServiceTests()
        {
            int port = GetFreePort();
            _baseUrl = "http://127.0.0.1:" + port + "/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();

            _workDir = Path.Combine(Path.GetTempPath(), "nlmcl-dl-" + Guid.NewGuid().ToString("N"));
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
                Directory.Delete(_workDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static int GetFreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("NLMCL download payload 0123456789");

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
                    return; // 监听已停止
                }

                try
                {
                    string path = context.Request.Url.AbsolutePath;
                    if (path == "/payload.bin")
                    {
                        context.Response.ContentType = "application/octet-stream";
                        context.Response.ContentLength64 = Payload.Length;
                        await context.Response.OutputStream.WriteAsync(Payload, 0, Payload.Length).ConfigureAwait(false);
                    }
                    else if (path == "/manifest.json")
                    {
                        byte[] body = Encoding.UTF8.GetBytes("{\"latest\":{\"release\":\"1.21.1\"}}");
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = body.Length;
                        await context.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        [Fact]
        public async Task Downloads_file_through_pcl_modnet()
        {
            string target = Path.Combine(_workDir, "nested", "payload.bin");
            PclDownloadService service = new PclDownloadService();

            await service.DownloadFileAsync(_baseUrl + "payload.bin", target, CancellationToken.None);

            Assert.True(File.Exists(target));
            Assert.Equal(Payload, File.ReadAllBytes(target));
        }

        [Fact]
        public void Downloads_file_with_byte_progress_through_pcl_loader()
        {
            string target = Path.Combine(_workDir, "nested", "payload-loader.bin");
            List<FileDownloadProgress> reports = new List<FileDownloadProgress>();
            Exception? failure = null;

            // LoaderBase.State 会通过 WPF Dispatcher 触发状态事件，测试里必须给一个 STA + Application。
            Thread thread = new Thread(() =>
            {
                try
                {
                    Application app = Application.Current ?? new Application();
                    PclBootstrap.InitializeOnUiThread();

                    PclDownloadService service = new PclDownloadService();
                    ((IByteProgressFileDownloader)service)
                        .DownloadAsync(
                            _baseUrl + "payload.bin",
                            target,
                            new InlineProgress(reports.Add),
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    if (Application.Current == app)
                    {
                        app.Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.True(File.Exists(target));
            Assert.Equal(Payload, File.ReadAllBytes(target));
            Assert.NotEmpty(reports);
            Assert.Equal(Payload.Length, reports[reports.Count - 1].Received);
            Assert.Equal(Payload.Length, reports[reports.Count - 1].Total);
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

        [Fact]
        public async Task Reads_text_through_pcl_modnet()
        {
            PclDownloadService service = new PclDownloadService();

            string text = await service.GetStringAsync(_baseUrl + "manifest.json", CancellationToken.None);

            Assert.Contains("1.21.1", text);
        }

        [Fact]
        public async Task Honors_cancellation_before_start()
        {
            PclDownloadService service = new PclDownloadService();
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.DownloadFileAsync(_baseUrl + "payload.bin",
                    Path.Combine(_workDir, "never.bin"), cts.Token));
        }
    }
}

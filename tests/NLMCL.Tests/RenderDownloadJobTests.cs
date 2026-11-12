using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class RenderDownloadJobTests
    {
        private sealed class FixedJobStore : IDownloadJobStore
        {
            public DownloadJob? Job { get; set; }

            public Task<DownloadJob?> LoadAsync(CancellationToken cancellationToken)
                => Task.FromResult(Job);

            public Task SaveAsync(DownloadJob job, CancellationToken cancellationToken)
            {
                Job = job;
                return Task.CompletedTask;
            }

            public Task ClearAsync(CancellationToken cancellationToken)
            {
                Job = null;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void Pending_download_banner_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-jobshot-" + Guid.NewGuid().ToString("N"), "roaming");

            Thread thread = new Thread(() =>
            {
                try
                {
                    FixedJobStore jobs = new FixedJobStore
                    {
                        Job = new DownloadJob
                        {
                            VersionId = "1.21.1",
                            Loader = "Fabric",
                            LoaderVersion = "0.16.9",
                            TargetDirectory = Path.Combine(roaming, ".minecraft"),
                            Parallelism = 6
                        }
                    };

                    DownloadViewModel download = new DownloadViewModel(
                        new TestSupport.EmptyProvider(),
                        new TestSupport.NoopInstaller(),
                        new AppPaths(roaming),
                        jobs: jobs);

                    download.LoadPendingJobAsync().GetAwaiter().GetResult();
                    Assert.True(download.HasPendingJob);

                    MainViewModel vm = TestSupport.CreateMain(
                        new PclVersionCatalog(), new AppPaths(roaming), download: download);
                    vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                        .First(i => i.Section == ShellSection.Download));

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };
                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    output = Path.Combine(dir, "ui-download-pending.png");

                    using (FileStream stream = File.Create(output))
                    {
                        encoder.Save(stream);
                    }

                    window.Close();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "渲染线程超时。");

            Assert.Null(failure);
            Assert.NotNull(output);
            Assert.True(new FileInfo(output!).Length > 10_000, "渲染结果过小，可能是空白图。");

            try
            {
                Directory.Delete(Path.GetDirectoryName(roaming)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void Download_queue_panel_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-queueshot-" + Guid.NewGuid().ToString("N"), "roaming");

            Thread thread = new Thread(() =>
            {
                try
                {
                    DownloadQueue queue = new DownloadQueue();
                    queue.Items.Add(new DownloadQueueItem("安装 1.21.1", () => { })
                    {
                        IsActive = true,
                        Status = "进行中"
                    });
                    queue.Items.Add(new DownloadQueueItem("安装 Sodium", () => { }));

                    DownloadViewModel download = TestSupport.CreateDownload(new AppPaths(roaming), queue);

                    MainViewModel vm = TestSupport.CreateMain(
                        new PclVersionCatalog(), new AppPaths(roaming), download: download);
                    vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                        .First(i => i.Section == ShellSection.Download));

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };
                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    output = Path.Combine(dir, "ui-download-queue.png");

                    using (FileStream stream = File.Create(output))
                    {
                        encoder.Save(stream);
                    }

                    window.Close();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "渲染线程超时。");

            Assert.Null(failure);
            Assert.NotNull(output);
            Assert.True(new FileInfo(output!).Length > 10_000, "渲染结果过小，可能是空白图。");

            try
            {
                Directory.Delete(Path.GetDirectoryName(roaming)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

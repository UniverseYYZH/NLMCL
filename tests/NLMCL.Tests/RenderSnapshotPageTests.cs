using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class RenderSnapshotPageTests
    {
        [Fact]
        public void Snapshot_page_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;

            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-snap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.SelectSection(vm.NavigationGroups[0].Items.First(i => i.Section == ShellSection.Snapshot));

                    const int width = 1060;
                    const int height = 700;

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
                    output = Path.Combine(dir, "ui-snapshots.png");

                    using (FileStream stream = File.Create(output))
                    {
                        encoder.Save(stream);
                    }

                    Assert.True(new FileInfo(output).Length > 5000);
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
            Assert.NotNull(output);
        }
    }
}
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using NLMCL.Core.Servers;
using Xunit;

namespace NLMCL.Tests
{
    public class RenderNingliPageTests
    {
        [Fact]
        public void Ningli_page_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-ningli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    AppPaths paths = new AppPaths(roaming);
                    ServerProfileStore profiles = new ServerProfileStore(paths);
                    MainViewModel vm = TestSupport.CreateMain(
                        new PclVersionCatalog(), paths, profileStore: profiles);

                    vm.LoadServerProfilesAsync().GetAwaiter().GetResult();
                    vm.RefreshJoinChecks();
                    Assert.Equal(2, vm.VisibleServerProfileCount);

                    vm.SelectSection(ShellSection.Ningli);
                    Assert.True(vm.IsNingliSection);

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm)
                    {
                        Width = width,
                        Height = height
                    };

                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(
                        width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    output = Path.Combine(dir, "ui-ningli.png");

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
            Assert.True(new FileInfo(output!).Length > 20_000);
        }
    }
}

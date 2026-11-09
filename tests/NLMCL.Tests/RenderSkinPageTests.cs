using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class RenderSkinPageTests
    {
        [Fact]
        public void Skin_page_renders_with_local_library()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-skinpage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            try
            {
                AppPaths paths = new AppPaths(roaming);
                Directory.CreateDirectory(paths.SkinDirectory);

                string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "steve.png");
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(paths.SkinDirectory, "steve-local.png"), overwrite: true);
                }

                SkinViewModel skin = new SkinViewModel(new TestSupport.NoopSkinService(), paths: paths);

                Thread thread = new Thread(() =>
                {
                    try
                    {
                        MainViewModel vm = TestSupport.CreateMain(
                            new PclVersionCatalog(), paths, skin: skin);
                        vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                            .First(i => i.Section == ShellSection.Skin));

                        const int width = 1060;
                        const int height = 720;

                        NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm)
                        {
                            Width = width,
                            Height = height
                        };

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
                        output = Path.Combine(dir, "ui-skin.png");

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
                Assert.True(new FileInfo(output!).Length > 10_000, "皮肤页渲染过小，可能是空白。");
            }
            finally
            {
                try { Directory.Delete(roaming, true); } catch (IOException) { }
            }
        }
    }
}

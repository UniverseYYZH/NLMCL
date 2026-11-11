using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    public class RenderWorldPageTests
    {
        private sealed class OneVersionCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion("1.21.1-fabric", "1.21.1", VersionKind.Release, null)
                });
            }
        }

        [Fact]
        public void World_page_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-world-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    AppPaths paths = new AppPaths(roaming);
                    MainViewModel vm = TestSupport.CreateMain(new OneVersionCatalog(), paths);
                    vm.RefreshAsync().GetAwaiter().GetResult();

                    Assert.Single(vm.Versions);
                    vm.SelectedVersion = vm.Versions[0];
                    vm.SelectSection(ShellSection.World);
                    Assert.True(vm.IsWorldSection);

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
                    output = Path.Combine(dir, "ui-world.png");

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

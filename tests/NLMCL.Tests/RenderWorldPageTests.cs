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
        private sealed class TwoVersionCatalog : IVersionCatalog
        {
            public Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
                string minecraftDirectory, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<MinecraftVersion>>(new[]
                {
                    new MinecraftVersion("1.21.1-fabric", "1.21.1", VersionKind.Release, null, hasClientJar: true),
                    new MinecraftVersion("1.20.1-fabric", "1.20.1", VersionKind.Release, null, hasClientJar: false)
                });
            }
        }

        [Fact]
        public void World_page_renders_clean_and_warning_preflight_states()
        {
            Exception? failure = null;
            string? cleanOutput = null;
            string? warningOutput = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-world-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    AppPaths paths = new AppPaths(roaming);
                    MainViewModel vm = TestSupport.CreateMain(new TwoVersionCatalog(), paths);
                    vm.RefreshAsync().GetAwaiter().GetResult();

                    Assert.Equal(2, vm.Versions.Count);

                    const int width = 1060;
                    const int height = 760;

                    string RenderTo(string fileName)
                    {
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
                        string file = Path.Combine(dir, fileName);

                        using (FileStream stream = File.Create(file))
                        {
                            encoder.Save(stream);
                        }

                        window.Close();
                        return file;
                    }

                    VersionItem clean = vm.Versions[0];
                    vm.SelectVersion(clean);
                    vm.SelectSection(ShellSection.World);
                    vm.LoadInstanceSettingsAsync(clean).GetAwaiter().GetResult();
                    vm.RefreshLaunchPreflightAsync().GetAwaiter().GetResult();

                    Assert.True(vm.IsWorldSection);
                    Assert.False(vm.HasLaunchPreflightWarnings);
                    Assert.Contains("未发现", vm.LaunchPreflightSummary);
                    cleanOutput = RenderTo("ui-world.png");

                    VersionItem warning = vm.Versions[1];
                    vm.SelectVersion(warning);
                    vm.LoadInstanceSettingsAsync(warning).GetAwaiter().GetResult();
                    vm.RefreshLaunchPreflightAsync().GetAwaiter().GetResult();

                    Assert.True(vm.HasLaunchPreflightWarnings);
                    Assert.Contains("缺少客户端 JAR", string.Join("\n", vm.LaunchPreflightWarnings));
                    warningOutput = RenderTo("ui-world-warning.png");
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
            Assert.NotNull(cleanOutput);
            Assert.NotNull(warningOutput);
            Assert.True(new FileInfo(cleanOutput!).Length > 20_000);
            Assert.True(new FileInfo(warningOutput!).Length > 20_000);
        }
    }
}

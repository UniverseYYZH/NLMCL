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
using NLMCL.App.Views;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// Mod 管理页的离屏渲染：确认虚拟化列表和模组信息行真正显示。
    /// </summary>
    public class RenderModsPageTests
    {
        [Fact]
        public void Mods_page_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;

            Thread thread = new Thread(() =>
            {
                try
                {
                    ModsViewModel mods = new ModsViewModel(new EmptyModManager());
                    mods.Mods.Add(new ModEntry("fabric-api.jar", "fabric-api.jar", 2_400_000)
                    {
                        Name = "Fabric API",
                        Version = "0.100.0",
                        Loader = "fabric"
                    });
                    mods.Mods.Add(new ModEntry("sodium.jar", "sodium.jar", 1_200_000)
                    {
                        Name = "Sodium",
                        Version = "0.6.0",
                        Loader = "fabric",
                        Enabled = true
                    });
                    mods.Mods.Add(new ModEntry("OptiFine_1.21.1.jar", "OptiFine_1.21.1.jar", 2_000_000)
                    {
                        Name = "OptiFine",
                        Version = "HD U I6",
                        Loader = "forge",
                        Enabled = true
                    });

                    mods.RefreshRendererDiagnostics();

                    const int width = 960;
                    const int height = 620;

                    Window window = new Window
                    {
                        Width = width,
                        Height = height,
                        Content = new ModPage { DataContext = mods }
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
                    output = Path.Combine(dir, "ui-mods.png");

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
            thread.Join();

            Assert.Null(failure);
            Assert.True(File.Exists(output), "没有生成渲染图：" + output);
        }

        private sealed class EmptyModManager : IModManager
        {
            public Task<IReadOnlyList<ModEntry>> ListAsync(string modsDirectory, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModEntry>>(Array.Empty<ModEntry>());

            public Task EnableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DisableAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task DeleteAsync(ModEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.Abstractions;
using NLMCL.App.ViewModels;
using NLMCL.App.Views;
using NLMCL.Core;
using NLMCL.Core.Servers;
using NLMCL.Core.Pcl;
using Xunit;

namespace NLMCL.Tests
{
    public class RenderMultiplayerSnapshotTests
    {
        [Fact]
        public void MultiplayerPage_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-lan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    AppPaths paths = new AppPaths(roaming);

                    // 注入服务器档案，让"主按钮 + 就绪检查"这张卡片也被渲染到快照里
                    ServerProfileStore profiles = new ServerProfileStore(paths);
                    MainViewModel vm = TestSupport.CreateMain(
                        new PclVersionCatalog(), paths, profileStore: profiles);

                    vm.LoadServerProfilesAsync().GetAwaiter().GetResult();
                    vm.RefreshJoinChecks();
                    Assert.Equal(2, vm.VisibleServerProfileCount);

                    vm.SelectSection(vm.NavigationGroups[0].Items[2]);
                    Assert.True(vm.IsMultiplayerSection);

                    vm.MyPort = "54321";
                    vm.RefreshMyAddress();
                    vm.ServerAddress = "10.21.3.9:54321";
                    vm.StatusText = "已复制：192.168.1.7:54321";

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
                    output = Path.Combine(dir, "ui-multiplayer.png");

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
                Directory.Delete(roaming, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void MultiplayerHub_lan_tab_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-lan-hub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    AppPaths paths = new AppPaths(roaming);
                    MainViewModel vm = TestSupport.CreateMain(
                        new PclVersionCatalog(),
                        paths,
                        lanScan: new LanScanViewModel(new EmptyLanScanner(), new TestSupport.FakeClipboard()));

                    vm.SelectSection(vm.NavigationGroups[0].Items[2]);
                    Assert.True(vm.IsMultiplayerSection);
                    Assert.NotNull(vm.LanScan);

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };
                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    MultiplayerHubPage? hub = FindChild<MultiplayerHubPage>(root);
                    Assert.NotNull(hub);
                    hub!.ShowLan();
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    output = Path.Combine(dir, "ui-multiplayer-lan.png");

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
                Directory.Delete(roaming, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                T? nested = FindChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private sealed class EmptyLanScanner : ILanScanner
        {
            public Task<IReadOnlyList<LanRoom>> ScanAsync(
                TimeSpan timeout, IProgress<LanRoom>? progress, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<LanRoom>>(Array.Empty<LanRoom>());
        }
    }
}

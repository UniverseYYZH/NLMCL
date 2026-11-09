using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class RenderHighContrastPageTests
    {
        [Fact]
        public void World_page_renders_with_high_contrast_palette()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-hc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            try
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        MainViewModel vm = TestSupport.CreateMain(
                            new PclVersionCatalog(), new AppPaths(roaming));
                        vm.SelectSection(vm.NavigationGroups[0].Items[0]);

                        const int width = 1060;
                        const int height = 700;

                        NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm)
                        {
                            Width = width,
                            Height = height
                        };

                        ApplyOverlay(window);

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
                        output = Path.Combine(dir, "ui-highcontrast.png");

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
                Assert.True(new FileInfo(output!).Length > 10_000, "高对比度渲染过小，可能是空白。");
            }
            finally
            {
                try { Directory.Delete(roaming, true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// 复刻 AppearanceService 对逻辑树的覆盖：给每个带 Design.xaml 的页面追加 HighContrast.xaml。
        /// 页面各自合并了 Design，只在窗口级加覆盖字典会被页面的本地字典遮住。
        /// </summary>
        private static void ApplyOverlay(FrameworkElement element)
        {
            bool hasDesign = element.Resources.MergedDictionaries.Any(dictionary =>
                dictionary.Source?.OriginalString?.IndexOf(
                    "Design.xaml", StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasDesign)
            {
                element.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/NLMCL;component/Themes/HighContrast.xaml", UriKind.Relative)
                });
            }

            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                if (child is FrameworkElement childElement)
                {
                    ApplyOverlay(childElement);
                }
            }
        }
    }
}

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
    public sealed class RenderSettingsPageTests
    {
        [Fact]
        public void Settings_page_renders_with_new_cards()
        {
            Exception? failure = null;
            string? output = null;

            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-set-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                        .First(i => i.Section == ShellSection.Settings));

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };
                    FrameworkElement settingsView = (FrameworkElement)window.FindName("SettingsView");
                    Assert.Same(vm.Settings, settingsView.DataContext);
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
                    output = Path.Combine(dir, "ui-settings.png");

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

        [Fact]
        public void Settings_page_renders_in_dark_theme()
        {
            Exception? failure = null;
            string? output = null;

            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-dark-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                        .First(i => i.Section == ShellSection.Settings));

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };

                    FrameworkElement root = (FrameworkElement)window.Content;
                    ApplyThemeOverlay(window.Resources, "Dark.xaml");
                    ApplyThemeOverlay(root, "Dark.xaml");

                    // 截图只渲染 Window.Content，窗口自身的 Background 不会进入位图；
                    // 用主题 Bg 填底，避免透明区域被离屏渲染成白色，误判成主题破功。
                    if (root is System.Windows.Controls.Panel panel)
                    {
                        panel.Background = (Brush)root.FindResource("Bg");
                    }
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    output = Path.Combine(dir, "ui-settings-dark.png");

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

        [Fact]
        public void Settings_page_renders_in_high_contrast()
        {
            Exception? failure = null;
            string? output = null;

            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-contrast-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                        .First(i => i.Section == ShellSection.Settings));

                    const int width = 1060;
                    const int height = 760;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };

                    FrameworkElement root = (FrameworkElement)window.Content;
                    ApplyThemeOverlay(window.Resources, "HighContrast.xaml");
                    ApplyThemeOverlay(root, "HighContrast.xaml");

                    if (root is System.Windows.Controls.Panel panel)
                    {
                        panel.Background = (Brush)root.FindResource("Bg");
                    }
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    output = Path.Combine(dir, "ui-settings-high-contrast.png");

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

        [Fact]
        public void High_contrast_palette_is_not_pure_black_or_white()
        {
            ResourceDictionary dictionary = new ResourceDictionary
            {
                Source = new Uri("/NLMCL;component/Themes/HighContrast.xaml", UriKind.Relative)
            };

            SolidColorBrush background = Assert.IsType<SolidColorBrush>(dictionary["Bg"]);
            SolidColorBrush card = Assert.IsType<SolidColorBrush>(dictionary["Card"]);
            SolidColorBrush border = Assert.IsType<SolidColorBrush>(dictionary["BorderColor"]);

            Assert.NotEqual(Colors.Black, background.Color);
            Assert.NotEqual(Colors.Black, card.Color);
            Assert.NotEqual(Colors.White, border.Color);
        }

        [Fact]
        public void Settings_page_renders_at_minimum_window_size()
        {
            Exception? failure = null;

            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-set-narrow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.SelectSection(vm.NavigationGroups.SelectMany(g => g.Items)
                        .First(i => i.Section == ShellSection.Settings));

                    const int width = 900;
                    const int height = 560;

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = width, Height = height };
                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);

                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                    Directory.CreateDirectory(dir);
                    string output = Path.Combine(dir, "ui-settings-narrow.png");

                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
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
        }
        /// <summary>
        /// 复刻 AppearanceService：页面各自合并了 Design.xaml，主题覆盖层也必须追加到页面资源，
        /// 只在窗口级加 Dark.xaml 会被页面本地字典遮住。
        /// </summary>
        private static void ApplyThemeOverlay(ResourceDictionary resources, string themeFile)
        {
            bool hasDesign = resources.MergedDictionaries.Any(dictionary =>
                dictionary.Source?.OriginalString?.IndexOf(
                    "Design.xaml", StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasDesign)
            {
                resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/NLMCL;component/Themes/" + themeFile, UriKind.Relative)
                });
            }
        }

        private static void ApplyThemeOverlay(FrameworkElement element, string themeFile)
        {
            ApplyThemeOverlay(element.Resources, themeFile);

            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                if (child is FrameworkElement childElement)
                {
                    ApplyThemeOverlay(childElement, themeFile);
                }
            }
        }
    }
}
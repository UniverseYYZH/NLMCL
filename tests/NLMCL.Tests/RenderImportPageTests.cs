using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 「导入资源」页的离屏渲染：用来确认页面真的显示了内容（不是空白）。
    /// 产物在 tests\NLMCL.Tests\bin\Debug\net48\artifacts\ui-import.png。
    /// </summary>
    public class RenderImportPageTests
    {
        [Fact]
        public void Import_page_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;

            Thread thread = new Thread(() =>
            {
                try
                {
                    string roaming = Path.Combine(
                        Path.GetTempPath(), "nlmcl-import-" + Guid.NewGuid().ToString("N"), "roaming");

                    ResourceViewModel resource = new ResourceViewModel(
                        new TestSupport.NoopResourceCatalog(),
                        new TestSupport.NoopResourceInstaller());

                    MainViewModel vm = TestSupport.CreateMain(
                        new TestSupport.EmptyCatalog(), new AppPaths(roaming),
                        resource: resource);

                    // 先搜一次，让结果列表真正渲染（Run 绑定这类问题只有有数据时才暴露）
                    resource.SearchText = "sodium";
                    resource.SearchAsync().GetAwaiter().GetResult();

                    vm.SelectSection(vm.NavigationGroups[1].Items[1]);
                    Assert.True(vm.IsImportSection);

                    const int width = 1060;
                    const int height = 700;

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
                    output = Path.Combine(dir, "ui-import.png");

                    using (FileStream stream = File.Create(output))
                    {
                        encoder.Save(stream);
                    }
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
    }
}

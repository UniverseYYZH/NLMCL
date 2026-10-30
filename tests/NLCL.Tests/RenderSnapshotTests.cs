using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLCL.App.ViewModels;
using NLCL.Core;
using NLCL.Core.Logging;
using NLCL.Core.Pcl;
using Xunit;

namespace NLCL.Tests
{
    /// <summary>
    /// 端到端渲染：造一个假的 .minecraft，用<b>真实的</b> PclVersionCatalog 扫描，
    /// 再把主窗口离屏渲染成 PNG。不需要桌面会话，也不 mock 任何解析逻辑。
    /// </summary>
    public class RenderSnapshotTests
    {
        private const string Vanilla1211 =
            @"{
                ""id"": ""1.21.1"", ""type"": ""release"",
                ""releaseTime"": ""2024-08-08T14:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"", ""libraries"": []
              }";

        private static string BuildFixture()
        {
            string roaming = Path.Combine(Path.GetTempPath(), "nlcl-shot-" + Guid.NewGuid().ToString("N"), "roaming");
            string versions = Path.Combine(roaming, ".minecraft", "versions");

            void Add(string name, string json, bool jar)
            {
                string dir = Path.Combine(versions, name);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name + ".json"), json);
                if (jar)
                {
                    File.WriteAllBytes(Path.Combine(dir, name + ".jar"), new byte[] { 0x50, 0x4B, 3, 4 });
                }
            }

            Add("26.4-snapshot-5", @"{
                ""id"": ""26.4-snapshot-5"", ""type"": ""snapshot"",
                ""releaseTime"": ""2026-10-20T09:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"", ""libraries"": []
              }", jar: false);

            Add("fabric-loader-0.19.5-1.21.11", @"{
                ""id"": ""fabric-loader-0.19.5-1.21.11"", ""inheritsFrom"": ""1.21.11"",
                ""type"": ""release"", ""releaseTime"": ""2026-10-19T09:00:00+00:00"",
                ""mainClass"": ""net.fabricmc.loader.impl.launch.knot.KnotClient"",
                ""libraries"": [{ ""name"": ""net.fabricmc:fabric-loader:0.19.5"" }]
              }", jar: false);

            Add("1.21.11", @"{
                ""id"": ""1.21.11"", ""type"": ""release"",
                ""releaseTime"": ""2026-10-18T09:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"", ""libraries"": []
              }", jar: true);

            Add("1.21.1", Vanilla1211, jar: true);

            Add("b1.7.3", @"{
                ""id"": ""b1.7.3"", ""type"": ""old_beta"",
                ""releaseTime"": ""2011-07-08T09:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.Minecraft"", ""libraries"": []
              }", jar: true);

            return roaming;
        }

        [Fact]
        public void DownloadPage_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = BuildFixture();

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(
                        new PclVersionCatalog(), new AppPaths(roaming));

                    // 切到「版本下载」分区
                    vm.SelectSection(vm.NavigationGroups[1].Items[0]);
                    Assert.True(vm.IsDownloadSection);
                    Assert.False(vm.IsVersionsSection);

                    const int width = 1060;
                    const int height = 700;

                    NLCL.App.MainWindow window = new NLCL.App.MainWindow(vm) { Width = width, Height = height };

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
                    output = Path.Combine(dir, "ui-download.png");

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
                Directory.Delete(Path.GetDirectoryName(roaming)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void LaunchConsole_renders_offscreen_to_png()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = BuildFixture();

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));

                    // 切到「启动与日志」分区
                    vm.SelectSection(vm.NavigationGroups[0].Items[1]);
                    Assert.True(vm.IsLaunchSection);

                    // 塞几行有代表性的日志，覆盖三种标签 + 普通行
                    vm.Console.VersionId = "1.21.1";
                    vm.Console.StatusText = "运行中（PID 12044）";

                    vm.Console.ErrorCount = 3;
                    vm.Console.Lines.Add(new ConsoleLine("[Render thread/INFO] Backend library: LWJGL version 3.3.3", LogTag.Normal));
                    vm.Console.Lines.Add(new ConsoleLine("[Render thread/INFO] Minecraft 1.21.1 started", LogTag.Normal));
                    vm.Console.Lines.Add(new ConsoleLine(
                        "java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main has been compiled by a more recent version",
                        LogTag.JavaIncompatible));
                    vm.Console.Lines.Add(new ConsoleLine(
                        "Mod Resolution encountered an error: missing or unsupported mandatory dependencies",
                        LogTag.ModConflict));
                    vm.Console.Lines.Add(new ConsoleLine(
                        "Failed to load texture minecraft:textures/block/stone.png", LogTag.ResourceFailure));

                    const int width = 1060;
                    const int height = 700;

                    NLCL.App.MainWindow window = new NLCL.App.MainWindow(vm) { Width = width, Height = height };

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
                    output = Path.Combine(dir, "ui-console.png");

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
                Directory.Delete(Path.GetDirectoryName(roaming)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void MainWindow_renders_from_real_pcl_scan()
        {
            Exception? failure = null;
            string? output = null;
            string roaming = BuildFixture();

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.RefreshAsync().GetAwaiter().GetResult();

                    Assert.Equal(5, vm.Versions.Count);
                    Assert.Equal("共 5 个版本", vm.StatusText);

                    // 选中一个版本：快照里要能看到高亮与"启动目标"
                    vm.SelectVersion(vm.Versions.First(v => v.Name == "1.21.1"));

                    const int width = 1060;
                    const int height = 700;

                    NLCL.App.MainWindow window = new NLCL.App.MainWindow(vm)
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
                    output = Path.Combine(dir, "ui-m1.png");

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
                Directory.Delete(Path.GetDirectoryName(roaming)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

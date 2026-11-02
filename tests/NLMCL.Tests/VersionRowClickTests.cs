using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NLMCL.App.ViewModels;
using NLMCL.Core;
using NLMCL.Core.Pcl;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 真点一下版本卡片：验证模板里的 Button 确实绑上了命令、点了会选中。
    /// 这类问题只能靠渲染后的可视树来查——XAML 看起来"对"但绑定断了是很常见的。
    /// </summary>
    public class VersionRowClickTests
    {
        private static string BuildFixture()
        {
            string roaming = Path.Combine(Path.GetTempPath(), "nlmcl-click-" + Guid.NewGuid().ToString("N"));
            string versionFolder = Path.Combine(roaming, ".minecraft", "versions", "1.21.1");
            Directory.CreateDirectory(versionFolder);

            File.WriteAllText(Path.Combine(versionFolder, "1.21.1.json"), @"{
                ""id"": ""1.21.1"",
                ""type"": ""release"",
                ""releaseTime"": ""2024-08-08T14:00:00+00:00"",
                ""mainClass"": ""net.minecraft.client.main.Main"",
                ""javaVersion"": { ""majorVersion"": 21 },
                ""libraries"": []
            }", Encoding.UTF8);

            File.WriteAllBytes(Path.Combine(versionFolder, "1.21.1.jar"), new byte[] { 1, 2, 3 });
            return roaming;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is T match)
                {
                    yield return match;
                }

                foreach (T nested in Descendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        [Fact]
        public void Clicking_a_version_card_selects_it()
        {
            Exception? failure = null;
            string roaming = BuildFixture();

            Thread thread = new Thread(() =>
            {
                try
                {
                    MainViewModel vm = TestSupport.CreateMain(new PclVersionCatalog(), new AppPaths(roaming));
                    vm.RefreshAsync().GetAwaiter().GetResult();
                    Assert.Single(vm.Versions);

                    // 先确认命令本身存在（编译期引用，不存在的话这里就编不过）
                    Assert.NotNull(vm.SelectVersionCommand);

                    NLMCL.App.MainWindow window = new NLMCL.App.MainWindow(vm) { Width = 1060, Height = 700 };
                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(1060, 700));
                    root.Arrange(new Rect(0, 0, 1060, 700));
                    root.UpdateLayout();

                    // 容器生成可能被推迟到下一个调度周期：抽干 dispatcher 再找
                    for (int pass = 0; pass < 3; pass++)
                    {
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                            () => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                        root.UpdateLayout();
                    }

                    // 找出行按钮：DataContext 是 VersionItem 的那个 Button
                    Button? card = null;
                    foreach (Button button in Descendants<Button>(root))
                    {
                        if (button.DataContext is VersionItem)
                        {
                            card = button;
                            break;
                        }
                    }

                    List<Button> allButtons = new List<Button>(Descendants<Button>(root));
                    List<ItemsControl> allItems = new List<ItemsControl>(Descendants<ItemsControl>(root));

                    string diagnostics =
                        "buttons=" + allButtons.Count +
                        " | itemsControls=" + allItems.Count +
                        " | itemCounts=" + string.Join(",", allItems.ConvertAll(c => c.Items.Count)) +
                        " | buttonContexts=" + string.Join(",", allButtons.ConvertAll(b => b.DataContext == null ? "null" : b.DataContext.GetType().Name)) +
                        " | versionTemplateRefs=" + string.Join(",", allItems.ConvertAll(c => c.ItemTemplate == null ? "null" : "set"));

                    Assert.True(card != null, "可视树里找不到版本行按钮：" + diagnostics);

                    // 命令必须绑上——绑不上就是点不动的根因
                    Assert.NotNull(card!.Command);
                    Assert.IsType<VersionItem>(card.CommandParameter);

                    // 真的执行一次（等价于点击）
                    card.Command.Execute(card.CommandParameter);

                    Assert.NotNull(vm.SelectedVersion);
                    Assert.True(vm.HasSelection);
                    Assert.True(vm.Versions[0].IsSelected);

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

            try
            {
                Directory.Delete(roaming, recursive: true);
            }
            catch (IOException)
            {
            }

            Assert.Null(failure);
        }
    }
}

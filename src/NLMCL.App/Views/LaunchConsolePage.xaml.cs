using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NLMCL.App.Views
{
    /// <summary>
    /// 启动与日志页。除了绑定之外只做一件事：新行到达时自动滚到底部。
    /// </summary>
    public partial class LaunchConsolePage : UserControl
    {
        private bool _followTail = true;

        public LaunchConsolePage()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnLinesChanged;
            }

            if (e.NewValue is ViewModels.LaunchConsoleViewModel vm)
            {
                _followTail = true;
                ((INotifyCollectionChanged)vm.Lines).CollectionChanged += OnLinesChanged;
            }
        }

        private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || !_followTail)
            {
                return;
            }

            // 日志是高频追加，滚到底用优先级最低的调度，避免卡住 UI 线程。
            Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (LogList.Items.Count == 0)
                    {
                        return;
                    }

                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LogList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 只在用户自己滚动时更新跟随状态；内容变高导致的 Extent 变化不改变用户意图。
            if (e.ExtentHeightChange != 0)
            {
                return;
            }

            _followTail = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 8;
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.LaunchConsoleViewModel vm || vm.Lines.Count == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(System.Environment.NewLine, vm.Lines.Select(line => line.Text)));
                vm.StatusText = "已复制 " + vm.Lines.Count + " 行日志";
            }
            catch (Exception ex)
            {
                vm.StatusText = "复制日志失败：" + ex.Message;
            }
        }

        private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.LaunchConsoleViewModel vm)
            {
                return;
            }

            string? path = await vm.ExportDiagnosticPackageAsync();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                Clipboard.SetText(path);
            }
            catch (Exception)
            {
                // 剪贴板被其他程序占用不影响诊断包本身
            }

            try
            {
                Process.Start(new ProcessStartInfo(
                    "explorer.exe", "/select,\"" + path + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                vm.StatusText = "诊断包已生成，但无法打开目录：" + ex.Message;
            }
        }
    }
}

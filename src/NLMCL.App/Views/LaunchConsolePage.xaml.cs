using System.Collections.Specialized;
using System.Windows.Controls;

namespace NLMCL.App.Views
{
    /// <summary>
    /// 启动与日志页。除了绑定之外只做一件事：新行到达时自动滚到底部。
    /// </summary>
    public partial class LaunchConsolePage : UserControl
    {
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
                ((INotifyCollectionChanged)vm.Lines).CollectionChanged += OnLinesChanged;
            }
        }

        private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
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
    }
}

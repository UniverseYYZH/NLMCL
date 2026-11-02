using System.Windows;
using NLMCL.App.ViewModels;

namespace NLMCL.App
{
    /// <summary>
    /// 纯壳：只做三件事——加载 XAML、注入 ViewModel、首次加载时触发扫描。
    /// 任何业务判断都不要写在这里（否则 MVVM 的边界就破了）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            _viewModel.InitializeCommand.Execute(null);
        }
    }
}

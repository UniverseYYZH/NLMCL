using System.Windows;
using System.Windows.Controls;
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

        /// <summary>
        /// PasswordBox.Password 不是依赖属性，没法直接绑定；
        /// 这里只做一次单向搬运，业务判断仍然全在 ViewModel 里。
        /// </summary>
        private void ExternalPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox box)
            {
                _viewModel.ExternalPassword = box.Password;
            }
        }
    }
}

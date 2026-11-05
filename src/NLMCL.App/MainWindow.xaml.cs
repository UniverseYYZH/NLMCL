using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.ComponentModel;
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

            // 状态栏改成"左下角滑入提示"：订阅 StatusText 变化，界面不必知道业务何时说话。
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.StatusText))
            {
                ShowStatusToast(_viewModel.StatusText);
            }
        }

        /// <summary>
        /// 状态提示：左下角从左侧滑入，几秒后淡出。
        /// PCL 的做法是不占布局、不打断操作，这里保持一致。
        /// </summary>
        internal void ShowStatusToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || StatusToast == null)
            {
                return;
            }

            _toastTimer?.Stop();

            ToastText.Text = message;
            StatusToast.Visibility = System.Windows.Visibility.Visible;

            DoubleAnimation slideIn = new DoubleAnimation(-48, 0, System.TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, System.TimeSpan.FromMilliseconds(160));

            ToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
            StatusToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            _toastTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(3.2)
            };

            _toastTimer.Tick += (sender, args) =>
            {
                _toastTimer.Stop();

                DoubleAnimation fadeOut = new DoubleAnimation(1, 0, System.TimeSpan.FromMilliseconds(220));
                fadeOut.Completed += (s2, a2) => StatusToast.Visibility = System.Windows.Visibility.Collapsed;

                StatusToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };

            _toastTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer? _toastTimer;

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

        /// <summary>点遮罩空白处关闭弹层；点卡片内部不关（事件源是卡片里的控件）。</summary>
        private void AccountOverlayClicked(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, sender))
            {
                _viewModel.IsAccountPopupOpen = false;
            }
        }

        private void CloseAccountOverlay(object sender, RoutedEventArgs e)
        {
            _viewModel.IsAccountPopupOpen = false;
        }

        /// <summary>点遮罩空白处关闭快照面板。</summary>
        private void SnapshotOverlayClicked(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, sender))
            {
                _viewModel.CloseSnapshotsCommand.Execute(null);
            }
        }

        private void CloseSnapshotOverlay(object sender, RoutedEventArgs e)
        {
            _viewModel.CloseSnapshotsCommand.Execute(null);
        }
    }
}

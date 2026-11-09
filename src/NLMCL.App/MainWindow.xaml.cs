using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.ComponentModel;
using NLMCL.App.ViewModels;
using Forms = System.Windows.Forms;

namespace NLMCL.App
{
    /// <summary>
    /// 纯壳：只做三件事——加载 XAML、注入 ViewModel、首次加载时触发扫描。
    /// 任何业务判断都不要写在这里（否则 MVVM 的边界就破了）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        private Forms.NotifyIcon? _trayIcon;

        private bool _exitRequested;

        public MainWindow(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = _viewModel;
            SettingsView.DataContext = _viewModel.Settings;
            Loaded += OnLoaded;
            Closing += OnWindowClosing;

            // 状态栏改成"左下角滑入提示"：订阅 StatusText 变化，界面不必知道业务何时说话。
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel.IsSettingsSection
                && e.Key == Key.F
                && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SettingsView.FocusSearch();
                e.Handled = true;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {            if (e.PropertyName == nameof(MainViewModel.StatusText))
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

            if (_viewModel.ReduceMotion)
            {
                ToastShift.X = 0;
                StatusToast.Opacity = 1;
            }
            else
            {
                DoubleAnimation slideIn = new DoubleAnimation(-48, 0, System.TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, System.TimeSpan.FromMilliseconds(160));

                ToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
                StatusToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }

            _toastTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(3.2)
            };

            _toastTimer.Tick += (sender, args) =>
            {
                _toastTimer.Stop();

                if (_viewModel.ReduceMotion)
                {
                    StatusToast.BeginAnimation(UIElement.OpacityProperty, null);
                    StatusToast.Visibility = System.Windows.Visibility.Collapsed;
                    return;
                }

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

        /// <summary>账号卡整块可点：子元素（头像/文字）上的点击也要能冒泡到这里。</summary>
        private void AccountCardClicked(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ToggleAccountPopup();
            e.Handled = true;
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

        private void VersionDetailsOverlayClicked(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, sender))
            {
                _viewModel.CloseVersionDetailsCommand.Execute(null);
            }
        }

        private void CloseVersionDetailsOverlay(object sender, RoutedEventArgs e)
        {
            _viewModel.CloseVersionDetailsCommand.Execute(null);
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (_exitRequested)
            {
                DisposeTrayIcon();
                return;
            }

            if (_viewModel.Console.IsRunning)
            {
                MessageBoxResult result = MessageBox.Show(
                    this,
                    "游戏仍在运行。关闭启动器不会结束游戏，确定继续吗？",
                    "游戏仍在运行",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            string behavior = _viewModel.SelectedCloseBehavior;

            if (behavior == "最小化到托盘")
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            if (behavior == "每次询问")
            {
                MessageBoxResult result = MessageBox.Show(
                    this,
                    "要退出 NLMCL 吗？\n\n是：退出启动器\n否：最小化到托盘\n取消：留在当前窗口",
                    "关闭 NLMCL",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }
            }

            DisposeTrayIcon();
        }

        private void HideToTray()
        {
            EnsureTrayIcon();
            ShowInTaskbar = false;
            Hide();

            _trayIcon?.ShowBalloonTip(
                2000, "NLMCL", "已最小化到托盘；双击图标恢复。", Forms.ToolTipIcon.Info);
        }

        private void EnsureTrayIcon()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
                return;
            }

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (_, __) => RestoreFromTray());
            menu.Items.Add("退出 NLMCL", null, (_, __) =>
            {
                _exitRequested = true;
                Close();
            });

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "NLMCL",
                Visible = true,
                ContextMenuStrip = menu
            };

            _trayIcon.DoubleClick += (_, __) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon == null)
            {
                return;
            }

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>
    /// 「皮肤」页：3D 预览支持拖动旋转 + 轻微自动旋转。
    ///
    /// 自动旋转用 <see cref="DispatcherTimer"/> 每 125ms 转 0.8°（约 6°/秒）；
    /// 拖动时暂停，松手后继续。
    /// </summary>
    public partial class SkinPage : UserControl
    {
        private readonly DispatcherTimer _timer;

        private SkinViewModel? _viewModel;
        private bool _dragging;
        private double _lastX;

        public SkinPage()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(125)
            };

            _timer.Tick += (sender, args) =>
            {
                if (!_dragging && IsVisible && _viewModel != null)
                {
                    _viewModel.AutoRotateStep();
                }
            };

            DataContextChanged += (sender, args) => BindViewModel();
            Loaded += (sender, args) => UpdateTimer();
            Unloaded += (sender, args) => _timer.Stop();
            IsVisibleChanged += (sender, args) => UpdateTimer();
        }

        private void BindViewModel()
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as SkinViewModel;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            UpdateTimer();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SkinViewModel.IsAutoRotating))
            {
                UpdateTimer();
            }
        }

        /// <summary>
        /// 页面被 Visibility 隐藏时不会触发 Unloaded，必须自己停表；
        /// 自动旋转关闭时也停，避免在后台空转。
        /// </summary>
        private void UpdateTimer()
        {
            if (IsLoaded && IsVisible && _viewModel?.IsAutoRotating == true)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _lastX = e.GetPosition(this).X;

            if (sender is IInputElement element)
            {
                element.CaptureMouse();
            }
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;

            if (sender is IInputElement element)
            {
                element.ReleaseMouseCapture();
            }
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || DataContext is not SkinViewModel vm)
            {
                return;
            }

            double x = e.GetPosition(this).X;
            double delta = x - _lastX;
            _lastX = x;

            if (Math.Abs(delta) < 0.5)
            {
                return;
            }

            vm.RotateBy(delta * 0.8);
        }
    }
}

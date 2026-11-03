using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>「皮肤」页：3D 预览支持拖动旋转。</summary>
    public partial class SkinPage : UserControl
    {
        private bool _dragging;
        private double _lastX;

        public SkinPage()
        {
            InitializeComponent();
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

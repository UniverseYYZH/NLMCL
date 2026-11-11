using System;
using System.Windows;
using System.Windows.Controls;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>世界详情页：只展示当前选中实例的真实配置与模组，不保存额外状态。</summary>
    public partial class WorldPage : UserControl
    {
        public WorldPage()
        {
            InitializeComponent();
        }

        private void CopyInstancePath_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            string? path = vm.SelectedVersion?.InstancePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                Clipboard.SetText(path);
                vm.StatusText = "已复制实例路径：" + path;
            }
            catch (Exception ex)
            {
                vm.StatusText = "复制实例路径失败：" + ex.Message;
            }
        }

        private void OpenLaunchSettings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectSection(ShellSection.Versions);
                vm.StatusText = "启动设置在世界库右侧的「启动设置 / 当前实例」中。";
            }
        }

        private void GoMultiplayer_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectSection(ShellSection.Multiplayer);
            }
        }

        private void GoDownload_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectSection(ShellSection.Download);
            }
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>
    /// F 下载中心：把原「版本下载 / 导入资源 / Mod 管理」三个页面收进同一导航项。
    /// </summary>
    public partial class DownloadHubPage : UserControl
    {
        public DownloadHubPage()
        {
            InitializeComponent();
            Loaded += (_, __) => ShowTab("download");
        }

        private MainViewModel? Vm => DataContext as MainViewModel;

        /// <summary>供截图/冒烟测试直接打开资源标签。</summary>
        public void OpenResourceTab() => ShowTab("resource");

        /// <summary>供截图/冒烟测试直接打开 Mod 标签。</summary>
        public void OpenModsTab() => ShowTab("mods");

        private void DownloadTab_Click(object sender, RoutedEventArgs e) => ShowTab("download");

        private void ResourceTab_Click(object sender, RoutedEventArgs e) => ShowTab("resource");

        private void ModsTab_Click(object sender, RoutedEventArgs e) => ShowTab("mods");

        private void ShowTab(string tab)
        {
            bool download = tab == "download";
            bool resource = tab == "resource";
            bool mods = tab == "mods";

            DownloadView.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
            ResourceView.Visibility = resource ? Visibility.Visible : Visibility.Collapsed;
            ModView.Visibility = mods ? Visibility.Visible : Visibility.Collapsed;

            DownloadTab.Tag = download ? "active" : null;
            ResourceTab.Tag = resource ? "active" : null;
            ModsTab.Tag = mods ? "active" : null;

            if (resource)
            {
                Vm?.ActivateResourceTab();
            }
            else if (mods)
            {
                Vm?.ActivateModsTab();
            }
        }
    }
}

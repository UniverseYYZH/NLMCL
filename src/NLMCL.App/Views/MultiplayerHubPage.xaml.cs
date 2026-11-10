using System.Windows;
using System.Windows.Controls;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>「联机」页：服务器房间与局域网扫描共用一个入口。</summary>
    public partial class MultiplayerHubPage : UserControl
    {
        public MultiplayerHubPage()
        {
            InitializeComponent();
        }

        /// <summary>切到服务器房间。</summary>
        public void ShowServers() => SetMode(showLan: false);

        /// <summary>切到局域网扫描。</summary>
        public void ShowLan() => SetMode(showLan: true);

        private void SetMode(bool showLan)
        {
            ServersView.Visibility = showLan ? Visibility.Collapsed : Visibility.Visible;
            LanView.Visibility = showLan ? Visibility.Visible : Visibility.Collapsed;

            ServersTab.Tag = showLan ? null : "active";
            LanTab.Tag = showLan ? "active" : null;
        }

        private void ServersTab_Click(object sender, RoutedEventArgs e) => ShowServers();

        private void LanTab_Click(object sender, RoutedEventArgs e) => ShowLan();

        private async void ScanLan_Click(object sender, RoutedEventArgs e)
        {
            ShowLan();

            if (DataContext is MainViewModel viewModel && viewModel.LanScan is { IsScanning: false } lanScan)
            {
                await lanScan.ScanAsync();
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NLMCL.App.Services;
using NLMCL.App.ViewModels;

namespace NLMCL.App.Views
{
    /// <summary>「局域网扫描」页的壳。</summary>
    public partial class LanScanPage : UserControl
    {
        private string? _qrFile;

        public LanScanPage()
        {
            InitializeComponent();
        }

        private void ShowQrCode(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element
                || element.Tag is not string address
                || string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            ShowQrCodeForAddress(address);
        }

        /// <summary>供二维码按钮与 UI 测试使用。</summary>
        public void ShowQrCodeForAddress(string address)
        {
            QrAddressBox.Text = address ?? string.Empty;

            if (string.IsNullOrWhiteSpace(address))
            {
                QrImage.Source = null;
                QrAddressText.Text = "输入地址后点「生成二维码」";
                QrOverlay.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                DismissQrCode();

                QrAddressBox.Text = address;
                byte[] png = QrCodeRenderer.CreatePng(address);
                string file = Path.Combine(
                    Path.GetTempPath(),
                    "nlmcl-qr-" + Guid.NewGuid().ToString("N") + ".png");

                File.WriteAllBytes(file, png);
                _qrFile = file;

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(file, UriKind.Absolute);
                image.EndInit();

                QrImage.Source = image;
                QrAddressText.Text = address;
                QrOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                QrAddressText.Text = "二维码生成失败：" + ex.Message;
                QrOverlay.Visibility = Visibility.Visible;
            }
        }

        private void ShowQrFromToolbar(object sender, RoutedEventArgs e)
        {
            string? address = (DataContext as LanScanViewModel)?.Rooms
                .FirstOrDefault()?.Endpoint;

            ShowQrCodeForAddress(address ?? string.Empty);
        }

        private void RegenerateQr(object sender, RoutedEventArgs e)
        {
            ShowQrCodeForAddress(QrAddressBox.Text);
        }

        private void CloseQrCode(object sender, RoutedEventArgs e)
        {
            DismissQrCode();
        }

        /// <summary>关闭二维码弹层并删除临时文件。</summary>
        public void DismissQrCode()
        {
            QrOverlay.Visibility = Visibility.Collapsed;
            QrImage.Source = null;

            if (_qrFile != null)
            {
                try
                {
                    File.Delete(_qrFile);
                }
                catch (IOException)
                {
                }

                _qrFile = null;
            }
        }
    }
}

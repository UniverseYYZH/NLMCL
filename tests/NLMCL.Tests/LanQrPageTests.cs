using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NLMCL.App.Views;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class LanQrPageTests
    {
        [Fact]
        public void Qr_overlay_shows_and_dismisses()
        {
            Exception? failure = null;

            Thread thread = new Thread(() =>
            {
                try
                {
                    LanScanPage page = new LanScanPage();
                    Assert.NotNull(page.FindName("QrToolbarButton"));
                    page.ShowQrCodeForAddress("192.168.1.23:25565");

                    FrameworkElement overlay = (FrameworkElement)page.FindName("QrOverlay");
                    Image image = (Image)page.FindName("QrImage");

                    Assert.Equal(Visibility.Visible, overlay.Visibility);
                    Assert.NotNull(image.Source);

                    page.DismissQrCode();

                    Assert.Equal(Visibility.Collapsed, overlay.Visibility);
                    Assert.Null(image.Source);

                    page.ShowQrCodeForAddress(string.Empty);
                    Assert.Equal(Visibility.Visible, overlay.Visibility);
                    Assert.Null(image.Source);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
        }
    }
}

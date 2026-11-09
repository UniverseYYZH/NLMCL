using NLMCL.App.Services;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class QrCodeRendererTests
    {
        [Fact]
        public void Lan_endpoint_renders_to_png()
        {
            byte[] png = QrCodeRenderer.CreatePng("192.168.1.23:25565");

            Assert.True(png.Length > 500);
            Assert.Equal(0x89, png[0]);
            Assert.Equal((byte)'P', png[1]);
            Assert.Equal((byte)'N', png[2]);
            Assert.Equal((byte)'G', png[3]);
        }
    }
}

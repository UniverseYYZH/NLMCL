using System;
using System.IO;
using QRCoder;

namespace NLMCL.App.Services
{
    /// <summary>把文本渲染成二维码 PNG。仅用于局域网地址分享。</summary>
    public static class QrCodeRenderer
    {
        public static byte[] CreatePng(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("二维码内容不能为空。", nameof(text));
            }

            QRCodeGenerator generator = new QRCodeGenerator();
            QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);

            using (QRCode code = new QRCode(data))
            using (System.Drawing.Bitmap bitmap = code.GetGraphic(8))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }
    }
}

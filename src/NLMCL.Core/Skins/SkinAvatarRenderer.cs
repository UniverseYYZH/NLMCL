using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NLMCL.Core.Skins
{
    /// <summary>
    /// 头像渲染：取皮肤头部的正面（8,8,8,8）叠加帽子层（40,8,8,8），放大成正方形 PNG。
    /// 侧栏账号卡片用它替代原来的 "?"。
    /// </summary>
    public static class SkinAvatarRenderer
    {
        /// <summary>输出 8 倍放大（64×64），最近邻保持像素感。</summary>
        public static void RenderHead(string skinPath, string outputPath, int scale = 8)
        {
            byte[] png = RenderHeadPng(skinPath, scale);

            string? directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputPath, png);
        }

        /// <summary>渲染头像并直接返回 PNG 字节，避免 GDI+ 直接写文件时的路径/锁问题。</summary>
        public static byte[] RenderHeadPng(string skinPath, int scale = 8)
        {
            if (!File.Exists(skinPath))
            {
                throw new FileNotFoundException("找不到皮肤文件。", skinPath);
            }

            if (scale < 1)
            {
                scale = 1;
            }

            using (Bitmap source = new Bitmap(skinPath))
            using (Bitmap avatar = new Bitmap(8 * scale, 8 * scale, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(avatar))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.SmoothingMode = SmoothingMode.None;

                // 头部正面
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, avatar.Width, avatar.Height),
                    new Rectangle(8, 8, 8, 8),
                    GraphicsUnit.Pixel);

                // 帽子层（有些皮肤没有，画了也无副作用：透明像素不覆盖）
                if (source.Width >= 64 && source.Height >= 16)
                {
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, avatar.Width, avatar.Height),
                        new Rectangle(40, 8, 8, 8),
                        GraphicsUnit.Pixel);
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    avatar.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }
    }
}

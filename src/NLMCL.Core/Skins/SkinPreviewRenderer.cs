using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NLMCL.Core.Skins
{
    /// <summary>
    /// 把皮肤贴图拼成一张"纸娃娃"正面立绘。
    ///
    /// 两种贴图格式都要支持：
    /// - **64×64（现代）**：左右手臂/腿各有独立贴图，且各部位有第二层（帽子、衣服、袖子、裤腿）
    /// - **64×32（旧版）**：只有右臂/右腿贴图，左臂/左腿靠**水平镜像**
    ///
    /// 画布 16×32 单位：头 0–8、身体 8–20、腿 20–32。
    /// </summary>
    public static class SkinPreviewRenderer
    {
        public const int UnitWidth = 16;

        public const int UnitHeight = 32;

        public static void RenderPaperDoll(string skinPath, string outputPath, int scale = 8)
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
            using (Bitmap canvas = new Bitmap(UnitWidth * scale, UnitHeight * scale, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.SmoothingMode = SmoothingMode.None;

                bool legacy = source.Height < 64;

                // 腿（20–32）
                Draw(graphics, source, scale, 4, 20, 4, 12, 4, 20);    // 右腿
                DrawMirrored(graphics, source, scale, 8, 20, 4, 12, 4, 20); // 左腿 = 右腿镜像

                if (!legacy)
                {
                    Draw(graphics, source, scale, 4, 20, 4, 12, 20, 52);      // 左腿专用贴图
                    DrawMirrored(graphics, source, scale, 8, 20, 4, 12, 4, 52); // 左腿第二层（镜像）
                    Draw(graphics, source, scale, 4, 20, 4, 12, 4, 36);       // 右腿第二层
                }

                // 手臂（8–20）
                Draw(graphics, source, scale, 0, 8, 4, 12, 44, 20);      // 右臂
                DrawMirrored(graphics, source, scale, 12, 8, 4, 12, 44, 20); // 左臂 = 右臂镜像

                if (!legacy)
                {
                    Draw(graphics, source, scale, 0, 8, 4, 12, 36, 52);        // 左臂专用贴图
                    DrawMirrored(graphics, source, scale, 12, 8, 4, 12, 44, 36); // 左臂第二层（镜像）
                    Draw(graphics, source, scale, 0, 8, 4, 12, 44, 36);        // 右臂第二层
                }

                // 身体（8–20）
                Draw(graphics, source, scale, 4, 8, 8, 12, 20, 20);

                if (!legacy)
                {
                    Draw(graphics, source, scale, 4, 8, 8, 12, 20, 36); // 身体第二层
                }

                // 头（0–8）
                Draw(graphics, source, scale, 4, 0, 8, 8, 8, 8);
                Draw(graphics, source, scale, 4, 0, 8, 8, 40, 8);    // 帽子层

                string? directory = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                canvas.Save(outputPath, ImageFormat.Png);
            }
        }

        private static void Draw(
            Graphics graphics, Bitmap source, int scale,
            int dx, int dy, int dw, int dh,
            int sx, int sy)
        {
            if (sx < 0 || sy < 0 || sx + dw > source.Width || sy + dh > source.Height || dw <= 0 || dh <= 0)
            {
                return;
            }

            graphics.DrawImage(
                source,
                new Rectangle(dx * scale, dy * scale, dw * scale, dh * scale),
                new Rectangle(sx, sy, dw, dh),
                GraphicsUnit.Pixel);
        }

        /// <summary>水平镜像绘制（旧版皮肤的左侧肢体）。</summary>
        private static void DrawMirrored(
            Graphics graphics, Bitmap source, int scale,
            int dx, int dy, int dw, int dh,
            int sx, int sy)
        {
            if (sx < 0 || sy < 0 || sx + dw > source.Width || sy + dh > source.Height || dw <= 0 || dh <= 0)
            {
                return;
            }

            using (Bitmap part = new Bitmap(dw, dh, PixelFormat.Format32bppArgb))
            {
                using (Graphics partGraphics = Graphics.FromImage(part))
                {
                    partGraphics.DrawImage(
                        source,
                        new Rectangle(0, 0, dw, dh),
                        new Rectangle(sx, sy, dw, dh),
                        GraphicsUnit.Pixel);
                }

                part.RotateFlip(RotateFlipType.RotateNoneFlipX);

                graphics.DrawImage(
                    part,
                    new Rectangle(dx * scale, dy * scale, dw * scale, dh * scale),
                    new Rectangle(0, 0, dw, dh),
                    GraphicsUnit.Pixel);
            }
        }
    }
}

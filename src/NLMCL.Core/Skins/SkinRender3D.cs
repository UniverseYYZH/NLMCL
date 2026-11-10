using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NLMCL.Core.Skins
{
    /// <summary>
    /// 软件 3D 皮肤预览：把皮肤贴图贴到 6 个盒子上，做透视投影 + 纹理采样 + 画家算法 + alpha 混合。
    ///
    /// 为什么不用 WPF Viewport3D：多层皮肤（衣服/袖子/裤腿）是半透明的，
    /// WPF 3D 的透明排序经常穿帮；自己光栅化能精确控制绘制顺序，也便于测试。
    ///
    /// 模型坐标（Minecraft 像素）：脚底 y=0，身高 32；x 向右，z 朝向观察者。
    /// </summary>
    public static class SkinRender3D
    {
        private const int Supersample = 2;

        private struct Rect
        {
            public Rect(int x, int y, int w, int h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public int X;

            public int Y;

            public int W;

            public int H;
        }

        private sealed class Face
        {
            public Point3[] Corners = new Point3[4];

            public Point2[] Uvs = new Point2[4];

            public Rect Texture;

            public Point3 Normal;

            public double Depth;

            public bool FlipU;
        }

        private struct Point3
        {
            public Point3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X;

            public double Y;

            public double Z;
        }

        private struct Point2
        {
            public Point2(double u, double v)
            {
                U = u;
                V = v;
            }

            public double U;

            public double V;
        }

        public static void Render(
            string skinPath, string outputPath, int size = 384,
            double yawDegrees = 28, double pitchDegrees = 8,
            bool slim = false)
        {
            byte[] png = RenderPng(skinPath, size, yawDegrees, pitchDegrees, slim);
            string? directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputPath, png);
        }

        /// <summary>
        /// 渲染成 PNG 字节，不写临时文件。皮肤页每帧自动旋转都走这里，
        /// 由 WPF 的图片转换器在内存中解码，避免临时目录堆积几十万张图。
        /// </summary>
        public static byte[] RenderPng(
            string skinPath, int size = 384,
            double yawDegrees = 28, double pitchDegrees = 8,
            bool slim = false)
        {
            if (!File.Exists(skinPath))
            {
                throw new FileNotFoundException("找不到皮肤文件。", skinPath);
            }

            size = Math.Max(64, size);

            using (Bitmap skin = new Bitmap(skinPath))
            {
                bool legacy = skin.Height < 64;
                int[] pixels = ReadPixels(skin, out int skinWidth, out int skinHeight);

                List<Face> faces = BuildFaces(legacy, slim);

                double yaw = yawDegrees * Math.PI / 180.0;
                double pitch = pitchDegrees * Math.PI / 180.0;

                List<Face> projected = Project(faces, yaw, pitch);
                projected.Sort((a, b) => a.Depth.CompareTo(b.Depth));

                int big = size * Supersample;
                int[] canvas = new int[big * big]; // ARGB，0 = 透明

                foreach (Face face in projected)
                {
                    Rasterize(face, canvas, big, pixels, skinWidth, skinHeight, legacy);
                }

                using (Bitmap result = Downscale(canvas, big, size))
                using (MemoryStream stream = new MemoryStream())
                {
                    result.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        // ---------- 几何 ----------

        private static List<Face> BuildFaces(bool legacy, bool slim)
        {
            List<Face> faces = new List<Face>();

            // 腿（y 0..12）
            AddLimb(faces, legacy, slim, -2, 6, 4, 12, 4,
                front: new Rect(4, 20, 4, 12), back: new Rect(12, 20, 4, 12),
                right: new Rect(0, 20, 4, 12), left: new Rect(8, 20, 4, 12),
                top: new Rect(4, 16, 4, 4), bottom: new Rect(8, 16, 4, 4),
                mirrorU: false);

            AddLimb(faces, legacy, slim, 2, 6, 4, 12, 4,
                front: new Rect(20, 52, 4, 12), back: new Rect(28, 52, 4, 12),
                right: new Rect(16, 52, 4, 12), left: new Rect(24, 52, 4, 12),
                top: new Rect(20, 48, 4, 4), bottom: new Rect(24, 48, 4, 4),
                mirrorU: legacy);

            // 身体（y 12..24）
            AddBox(faces, 0, 18, 0, 8, 12, 4,
                front: new Rect(20, 20, 8, 12), back: new Rect(32, 20, 8, 12),
                right: new Rect(16, 20, 4, 12), left: new Rect(28, 20, 4, 12),
                top: new Rect(20, 16, 8, 4), bottom: new Rect(28, 16, 8, 4));

            // 手臂（y 12..24）；纤细皮肤手臂宽 3
            int armWidth = slim ? 3 : 4;
            int armX = 4 + armWidth / 2 + 1;

            AddLimb(faces, legacy, slim, -armX, 18, armWidth, 12, 4,
                front: new Rect(44, 20, armWidth, 12),
                back: new Rect(slim ? 51 : 52, 20, armWidth, 12),
                right: new Rect(40, 20, 4, 12),
                left: new Rect(slim ? 47 : 48, 20, 4, 12),
                top: new Rect(44, 16, armWidth, 4),
                bottom: new Rect(slim ? 47 : 48, 16, armWidth, 4),
                mirrorU: false);

            AddLimb(faces, legacy, slim, armX, 18, armWidth, 12, 4,
                front: new Rect(36, 52, armWidth, 12),
                back: new Rect(slim ? 43 : 44, 52, armWidth, 12),
                right: new Rect(32, 52, 4, 12),
                left: new Rect(slim ? 39 : 40, 52, 4, 12),
                top: new Rect(36, 48, armWidth, 4),
                bottom: new Rect(slim ? 39 : 40, 48, armWidth, 4),
                mirrorU: legacy);

            // 头（y 24..32）
            AddBox(faces, 0, 28, 0, 8, 8, 8,
                front: new Rect(8, 8, 8, 8), back: new Rect(24, 8, 8, 8),
                right: new Rect(0, 8, 8, 8), left: new Rect(16, 8, 8, 8),
                top: new Rect(8, 0, 8, 8), bottom: new Rect(16, 0, 8, 8));

            // 帽子层（略放大 0.4 像素，避免 z-fighting）
            AddBox(faces, 0, 28, 0, 8.8, 8.8, 8.8,
                front: new Rect(40, 8, 8, 8), back: new Rect(56, 8, 8, 8),
                right: new Rect(32, 8, 8, 8), left: new Rect(48, 8, 8, 8),
                top: new Rect(40, 0, 8, 8), bottom: new Rect(48, 0, 8, 8));

            return faces;
        }

        private static void AddLimb(
            List<Face> faces, bool legacy, bool slim, double cx, double cy,
            double width, double height, double depth,
            Rect front, Rect back, Rect right, Rect left, Rect top, Rect bottom,
            bool mirrorU)
        {
            // 旧版 64×32 没有左肢贴图：左侧肢体把右侧贴图水平镜像
            AddBox(faces, cx, cy, 0, width, height, depth, front, back, right, left, top, bottom,
                flipU: mirrorU || legacy);
        }


        private static void AddBox(
            List<Face> faces, double cx, double cy, double cz,
            double width, double height, double depth,
            Rect front, Rect back, Rect right, Rect left, Rect top, Rect bottom,
            bool flipU = false)
        {
            double hw = width / 2, hh = height / 2, hd = depth / 2;

            // front (+z)
            faces.Add(MakeFace(
                new[]
                {
                    new Point3(cx - hw, cy - hh, cz + hd), new Point3(cx + hw, cy - hh, cz + hd),
                    new Point3(cx + hw, cy + hh, cz + hd), new Point3(cx - hw, cy + hh, cz + hd)
                },
                front, new Point3(0, 0, 1), flipU));

            // back (-z)
            faces.Add(MakeFace(
                new[]
                {
                    new Point3(cx + hw, cy - hh, cz - hd), new Point3(cx - hw, cy - hh, cz - hd),
                    new Point3(cx - hw, cy + hh, cz - hd), new Point3(cx + hw, cy + hh, cz - hd)
                },
                back, new Point3(0, 0, -1), flipU));

            // right（角色的右侧 = -x）
            faces.Add(MakeFace(
                new[]
                {
                    new Point3(cx - hw, cy - hh, cz + hd), new Point3(cx - hw, cy - hh, cz - hd),
                    new Point3(cx - hw, cy + hh, cz - hd), new Point3(cx - hw, cy + hh, cz + hd)
                },
                right, new Point3(-1, 0, 0), flipU));

            // left（+x）
            faces.Add(MakeFace(
                new[]
                {
                    new Point3(cx + hw, cy - hh, cz - hd), new Point3(cx + hw, cy - hh, cz + hd),
                    new Point3(cx + hw, cy + hh, cz + hd), new Point3(cx + hw, cy + hh, cz - hd)
                },
                left, new Point3(1, 0, 0), flipU));

            // top (+y)
            faces.Add(MakeFace(
                new[]
                {
                    new Point3(cx - hw, cy + hh, cz + hd), new Point3(cx + hw, cy + hh, cz + hd),
                    new Point3(cx + hw, cy + hh, cz - hd), new Point3(cx - hw, cy + hh, cz - hd)
                },
                top, new Point3(0, 1, 0), flipU));

            // bottom (-y)
            faces.Add(MakeFace(
                new[]
                {
                    new Point3(cx - hw, cy - hh, cz - hd), new Point3(cx + hw, cy - hh, cz - hd),
                    new Point3(cx + hw, cy - hh, cz + hd), new Point3(cx - hw, cy - hh, cz + hd)
                },
                bottom, new Point3(0, -1, 0), flipU));
        }

        private static Face MakeFace(Point3[] corners, Rect texture, Point3 normal, bool flipU = false)
        {
            // UV：左下 (u0,v1)、右下 (u1,v1)、右上 (u1,v0)、左上 (u0,v0)
            return new Face
            {
                Corners = corners,
                Texture = texture,
                Normal = normal,
                FlipU = flipU,
                Uvs = new[]
                {
                    new Point2(0, 1), new Point2(1, 1), new Point2(1, 0), new Point2(0, 0)
                }
            };
        }

        // ---------- 投影与光栅化 ----------

        private static List<Face> Project(List<Face> faces, double yaw, double pitch)
        {
            List<Face> result = new List<Face>(faces.Count);

            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);

            const double cameraZ = 78;
            const double fov = 34 * Math.PI / 180;
            double focal = 1.0 / Math.Tan(fov / 2);

            foreach (Face face in faces)
            {
                Face copy = new Face
                {
                    Texture = face.Texture,
                    Normal = face.Normal,
                    Uvs = face.Uvs,
                    Corners = new Point3[4]
                };

                double sumZ = 0;

                for (int i = 0; i < 4; i++)
                {
                    Point3 p = face.Corners[i];

                    // 以身体中心 (0,16,0) 为旋转原点
                    double x = p.X;
                    double y = p.Y - 16;
                    double z = p.Z;

                    // yaw 绕 Y
                    double rx = x * cosY + z * sinY;
                    double rz = -x * sinY + z * cosY;

                    // pitch 绕 X
                    double ry = y * cosP - rz * sinP;
                    double rz2 = y * sinP + rz * cosP;

                    double depth = rz2 + cameraZ;
                    double scale = focal / depth;

                    copy.Corners[i] = new Point3(rx * scale, -ry * scale, depth);
                    sumZ += depth;
                }

                copy.Depth = sumZ / 4;
                result.Add(copy);
            }

            return result;
        }

        private static void Rasterize(
            Face face, int[] canvas, int size, int[] skin, int skinWidth, int skinHeight, bool legacy)
        {
            // 屏幕坐标：中心 + 缩放（NDC 的 -1..1 映射到 size）
            double half = size / 2.0;

            double[] xs = new double[4];
            double[] ys = new double[4];

            for (int i = 0; i < 4; i++)
            {
                xs[i] = half + face.Corners[i].X * half * 0.9;
                ys[i] = half + face.Corners[i].Y * half * 0.9;
            }

            double minX = Math.Max(0, Math.Floor(Math.Min(Math.Min(xs[0], xs[1]), Math.Min(xs[2], xs[3]))));
            double maxX = Math.Min(size - 1, Math.Ceiling(Math.Max(Math.Max(xs[0], xs[1]), Math.Max(xs[2], xs[3]))));
            double minY = Math.Max(0, Math.Floor(Math.Min(Math.Min(ys[0], ys[1]), Math.Min(ys[2], ys[3]))));
            double maxY = Math.Min(size - 1, Math.Ceiling(Math.Max(Math.Max(ys[0], ys[1]), Math.Max(ys[2], ys[3]))));

            if (maxX <= minX || maxY <= minY)
            {
                return;
            }

            // 光照：法线绕 Y 旋转后的朝向决定明暗
            int shade = 200 + (int)(face.Normal.Z * 30);

            // 三角形 0-1-2 和 0-2-3
            RasterizeTriangle(face, xs, ys, 0, 1, 2, canvas, size, skin, skinWidth, skinHeight, shade);
            RasterizeTriangle(face, xs, ys, 0, 2, 3, canvas, size, skin, skinWidth, skinHeight, shade);
        }

        private static void RasterizeTriangle(
            Face face, double[] xs, double[] ys, int a, int b, int c,
            int[] canvas, int size, int[] skin, int skinWidth, int skinHeight, int shade)
        {
            double x0 = xs[a], y0 = ys[a];
            double x1 = xs[b], y1 = ys[b];
            double x2 = xs[c], y2 = ys[c];

            double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);

            if (Math.Abs(area) < 1e-9)
            {
                return;
            }

            int minX = (int)Math.Max(0, Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
            int maxX = (int)Math.Min(size - 1, Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int minY = (int)Math.Max(0, Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
            int maxY = (int)Math.Min(size - 1, Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));

            for (int py = minY; py <= maxY; py++)
            {
                double fy = py + 0.5;

                for (int px = minX; px <= maxX; px++)
                {
                    double fx = px + 0.5;

                    double w0 = ((x1 - fx) * (y2 - fy) - (x2 - fx) * (y1 - fy)) / area;
                    double w1 = ((x2 - fx) * (y0 - fy) - (x0 - fx) * (y2 - fy)) / area;
                    double w2 = 1 - w0 - w1;

                    if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6)
                    {
                        continue;
                    }

                    double u = face.Uvs[a].U * w0 + face.Uvs[b].U * w1 + face.Uvs[c].U * w2;

                    if (face.FlipU)
                    {
                        u = 1 - u;
                    }
                    double v = face.Uvs[a].V * w0 + face.Uvs[b].V * w1 + face.Uvs[c].V * w2;

                    int tx = face.Texture.X + (int)Math.Floor(u * face.Texture.W);
                    int ty = face.Texture.Y + (int)Math.Floor(v * face.Texture.H);

                    tx = Math.Max(0, Math.Min(skinWidth - 1, tx));
                    ty = Math.Max(0, Math.Min(skinHeight - 1, ty));

                    int source = skin[ty * skinWidth + tx];
                    int alpha = (source >> 24) & 0xFF;

                    if (alpha == 0)
                    {
                        continue;
                    }

                    // 阴影 + 源 alpha
                    int r = Math.Min(255, ((source >> 16) & 0xFF) * shade / 230);
                    int g = Math.Min(255, ((source >> 8) & 0xFF) * shade / 230);
                    int bl = Math.Min(255, (source & 0xFF) * shade / 230);

                    Blend(canvas, py * size + px, Color.FromArgb(alpha, r, g, bl));
                }
            }
        }

        private static void Blend(int[] canvas, int index, Color source)
        {
            int sa = source.A;

            if (sa >= 255)
            {
                canvas[index] = (255 << 24) | (source.R << 16) | (source.G << 8) | source.B;
                return;
            }

            Color destination = Color.FromArgb(canvas[index]);

            int da = destination.A;
            int outA = sa + da * (255 - sa) / 255;

            if (outA == 0)
            {
                canvas[index] = 0;
                return;
            }

            int r = (source.R * sa + destination.R * da * (255 - sa) / 255) / outA;
            int g = (source.G * sa + destination.G * da * (255 - sa) / 255) / outA;
            int b = (source.B * sa + destination.B * da * (255 - sa) / 255) / outA;

            canvas[index] = (outA << 24) | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
        }

        // ---------- 位图读写 ----------

        private static int[] ReadPixels(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.Width;
            height = bitmap.Height;
            int[] pixels = new int[width * height];

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                for (int y = 0; y < height; y++)
                {
                    IntPtr row = IntPtr.Add(data.Scan0, y * data.Stride);

                    for (int x = 0; x < width; x++)
                    {
                        pixels[y * width + x] = System.Runtime.InteropServices.Marshal.ReadInt32(row, x * 4);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return pixels;
        }

        private static Bitmap Downscale(int[] canvas, int big, int size)
        {
            Bitmap bitmap = new Bitmap(big, big, PixelFormat.Format32bppArgb);

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, big, big), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                for (int y = 0; y < big; y++)
                {
                    IntPtr row = IntPtr.Add(data.Scan0, y * data.Stride);

                    for (int x = 0; x < big; x++)
                    {
                        System.Runtime.InteropServices.Marshal.WriteInt32(row, x * 4, canvas[y * big + x]);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(bitmap, new Rectangle(0, 0, size, size));
            }

            bitmap.Dispose();
            return result;
        }
    }
}

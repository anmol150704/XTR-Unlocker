using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconGenerator;

public static class Program
{
    public static void Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var assetsDir = Path.Combine(repoRoot, "desktop", "src", "Assets");
        if (!Directory.Exists(assetsDir))
        {
            throw new DirectoryNotFoundException($"Assets directory not found at: {assetsDir}");
        }

        Console.WriteLine($"Generating modern Apple Liquid XTR visual identity into: {assetsDir}");

        // 1. Render high-resolution master square icon (1024x1024)
        using var master1024 = RenderMasterIcon(1024);

        // 2. Save icon_256.png
        using (var bmp256 = ResizeBitmap(master1024, 256, 256))
        {
            bmp256.Save(Path.Combine(assetsDir, "icon_256.png"), ImageFormat.Png);
        }

        // 3. Generate multi-resolution icon.ico
        int[] icoSizes = [256, 128, 64, 48, 32, 24, 16];
        var icoStreams = new List<byte[]>();
        foreach (var s in icoSizes)
        {
            using var resized = ResizeBitmap(master1024, s, s);
            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            icoStreams.Add(ms.ToArray());
        }
        SaveIcoFile(icoSizes, icoStreams, Path.Combine(assetsDir, "icon.ico"));

        // 4. Generate Square44x44Logo variants
        var square44Scales = new (string Suffix, int Size)[]
        {
            ("scale-100", 44),
            ("scale-125", 55),
            ("scale-150", 66),
            ("scale-200", 88),
            ("scale-400", 176),
            ("targetsize-16", 16),
            ("targetsize-24", 24),
            ("targetsize-32", 32),
            ("targetsize-48", 48),
            ("targetsize-256", 256),
            ("altform-unplated_targetsize-16", 16),
            ("altform-unplated_targetsize-24", 24),
            ("altform-unplated_targetsize-32", 32),
            ("altform-unplated_targetsize-48", 48),
            ("altform-unplated_targetsize-256", 256),
            ("altform-lightunplated_targetsize-16", 16),
            ("altform-lightunplated_targetsize-24", 24),
            ("altform-lightunplated_targetsize-32", 32),
            ("altform-lightunplated_targetsize-48", 48),
            ("altform-lightunplated_targetsize-256", 256)
        };
        foreach (var (suffix, size) in square44Scales)
        {
            using var b = ResizeBitmap(master1024, size, size);
            b.Save(Path.Combine(assetsDir, $"Square44x44Logo.{suffix}.png"), ImageFormat.Png);
        }

        // 5. Generate Square150x150Logo variants
        var square150Scales = new (string Suffix, int Size)[]
        {
            ("scale-100", 150),
            ("scale-125", 188),
            ("scale-150", 225),
            ("scale-200", 300),
            ("scale-400", 600)
        };
        foreach (var (suffix, size) in square150Scales)
        {
            using var b = ResizeBitmap(master1024, size, size);
            b.Save(Path.Combine(assetsDir, $"Square150x150Logo.{suffix}.png"), ImageFormat.Png);
        }

        // 6. Generate StoreLogo variants
        var storeScales = new (string Suffix, int Size)[]
        {
            ("scale-100", 50),
            ("scale-125", 63),
            ("scale-150", 75),
            ("scale-200", 100),
            ("scale-400", 200)
        };
        foreach (var (suffix, size) in storeScales)
        {
            using var b = ResizeBitmap(master1024, size, size);
            b.Save(Path.Combine(assetsDir, $"StoreLogo.{suffix}.png"), ImageFormat.Png);
        }

        // 7. Generate BadgeLogo variants (monochrome/clean symbol)
        var badgeScales = new (string Suffix, int Size)[]
        {
            ("scale-100", 24),
            ("scale-125", 30),
            ("scale-150", 36),
            ("scale-200", 48),
            ("scale-400", 96)
        };
        foreach (var (suffix, size) in badgeScales)
        {
            using var b = ResizeBitmap(master1024, size, size);
            b.Save(Path.Combine(assetsDir, $"BadgeLogo.{suffix}.png"), ImageFormat.Png);
        }

        // 8. Generate LargeTile and SmallTile
        var largeTileScales = new (string Suffix, int Size)[]
        {
            ("scale-100", 310),
            ("scale-125", 388),
            ("scale-150", 465),
            ("scale-200", 620),
            ("scale-400", 1240)
        };
        foreach (var (suffix, size) in largeTileScales)
        {
            using var b = ResizeBitmap(master1024, size, size);
            b.Save(Path.Combine(assetsDir, $"LargeTile.{suffix}.png"), ImageFormat.Png);
        }

        var smallTileScales = new (string Suffix, int Size)[]
        {
            ("scale-100", 71),
            ("scale-125", 89),
            ("scale-150", 107),
            ("scale-200", 142),
            ("scale-400", 284)
        };
        foreach (var (suffix, size) in smallTileScales)
        {
            using var b = ResizeBitmap(master1024, size, size);
            b.Save(Path.Combine(assetsDir, $"SmallTile.{suffix}.png"), ImageFormat.Png);
        }

        // 9. Generate Wide310x150Logo and SplashScreen variants
        var wideScales = new (string Suffix, int W, int H)[]
        {
            ("scale-100", 310, 150),
            ("scale-125", 388, 188),
            ("scale-150", 465, 225),
            ("scale-200", 620, 300),
            ("scale-400", 1240, 600)
        };
        foreach (var (suffix, w, h) in wideScales)
        {
            using var b = RenderWideBanner(master1024, w, h, "XTR UNLOCKER");
            b.Save(Path.Combine(assetsDir, $"Wide310x150Logo.{suffix}.png"), ImageFormat.Png);
        }

        var splashScales = new (string Suffix, int W, int H)[]
        {
            ("scale-100", 620, 300),
            ("scale-125", 775, 375),
            ("scale-150", 930, 450),
            ("scale-200", 1240, 600),
            ("scale-400", 2480, 1200)
        };
        foreach (var (suffix, w, h) in splashScales)
        {
            using var b = RenderWideBanner(master1024, w, h, "XTR UNLOCKER STUDIO");
            b.Save(Path.Combine(assetsDir, $"SplashScreen.{suffix}.png"), ImageFormat.Png);
        }

        // Clean up test file if it exists
        var testPath = Path.Combine(assetsDir, "icon_test.png");
        if (File.Exists(testPath)) File.Delete(testPath);

        Console.WriteLine("All visual assets generated successfully!");
    }

    private static Bitmap RenderMasterIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        float pad = size * 0.07f;
        float w = size - (pad * 2f);
        float h = w;
        float r = w * 0.22f; // Smooth Apple squircle radius

        using var path = CreateRoundedRect(pad, pad, w, h, r);

        // 1. Soft ambient drop shadow
        for (int i = 8; i >= 1; i--)
        {
            int alpha = 14 / i;
            using var shadowPen = new Pen(Color.FromArgb(alpha, 0, 0, 0), i * 3f * (size / 512f));
            g.DrawPath(shadowPen, path);
        }

        // 2. Titanium obsidian dark glass background
        using (var bgBrush = new LinearGradientBrush(
            new PointF(pad, pad),
            new PointF(pad + w, pad + h),
            Color.FromArgb(255, 20, 24, 34),
            Color.FromArgb(255, 8, 10, 15)))
        {
            g.FillPath(bgBrush, path);
        }

        // 3. Radial emerald core illumination
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(pad + (w * 0.15f), pad + (h * 0.15f), w * 0.7f, h * 0.7f);
            using var pgb = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(70, 52, 199, 89),
                SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) }
            };
            g.FillPath(pgb, glowPath);
        }

        // 4. Emerald glass border rim with subtle top highlight
        using (var rimBrush = new LinearGradientBrush(
            new PointF(pad, pad),
            new PointF(pad + w, pad + h),
            Color.FromArgb(220, 52, 199, 89),
            Color.FromArgb(60, 255, 255, 255)))
        {
            using var rimPen = new Pen(rimBrush, size * 0.022f);
            g.DrawPath(rimPen, path);
        }

        // 5. Central Glyph: Precision XTR Speed Bolt & Geometric Chevrons
        float cx = size * 0.5f;
        float cy = size * 0.5f;
        float s = size / 512.0f;

        // Aerodynamic Multiplier Wings (X chevron geometry)
        using (var leftWing = new GraphicsPath())
        {
            var p1 = new PointF(cx - (130 * s), cy - (85 * s));
            var p2 = new PointF(cx - (85 * s), cy - (85 * s));
            var p3 = new PointF(cx - (50 * s), cy - (35 * s));
            var p4 = new PointF(cx - (95 * s), cy - (35 * s));
            leftWing.AddPolygon(new[] { p1, p2, p3, p4 });
            using var wingBrush1 = new SolidBrush(Color.FromArgb(200, 52, 199, 89));
            g.FillPath(wingBrush1, leftWing);
        }

        using (var rightWing = new GraphicsPath())
        {
            var p1 = new PointF(cx + (95 * s), cy + (35 * s));
            var p2 = new PointF(cx + (50 * s), cy + (35 * s));
            var p3 = new PointF(cx + (85 * s), cy + (85 * s));
            var p4 = new PointF(cx + (130 * s), cy + (85 * s));
            rightWing.AddPolygon(new[] { p1, p2, p3, p4 });
            using var wingBrush2 = new SolidBrush(Color.FromArgb(200, 0, 229, 255));
            g.FillPath(wingBrush2, rightWing);
        }

        // Stylized High-Voltage Lightning Bolt
        using (var boltPath = new GraphicsPath())
        {
            var boltPoints = new PointF[]
            {
                new(cx + (18 * s), cy - (145 * s)),
                new(cx - (80 * s), cy - (8 * s)),
                new(cx - (6 * s), cy - (8 * s)),
                new(cx - (42 * s), cy + (150 * s)),
                new(cx + (90 * s), cy + (8 * s)),
                new(cx + (18 * s), cy + (8 * s))
            };
            boltPath.AddPolygon(boltPoints);

            // Radiant ambient glow around bolt
            for (int gIdx = 5; gIdx >= 1; gIdx--)
            {
                int alpha = 40 / gIdx;
                using var glowPen = new Pen(Color.FromArgb(alpha, 52, 199, 89), gIdx * 7f * s);
                g.DrawPath(glowPen, boltPath);
            }

            // High-voltage Emerald to Electric Cyan gradient fill
            using (var boltBrush = new LinearGradientBrush(
                new PointF(cx - 80 * s, cy - 145 * s),
                new PointF(cx + 90 * s, cy + 150 * s),
                Color.FromArgb(255, 52, 199, 89),
                Color.FromArgb(255, 0, 229, 255)))
            {
                g.FillPath(boltBrush, boltPath);
            }

            // Glass specular inner stroke
            using var boltHighlight = new Pen(Color.FromArgb(220, 255, 255, 255), 2.2f * s);
            g.DrawPath(boltHighlight, boltPath);
        }

        return bmp;
    }

    private static Bitmap RenderWideBanner(Bitmap master, int width, int height, string text)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        // Dark titanium background
        using (var bg = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(width, height),
            Color.FromArgb(255, 14, 17, 24),
            Color.FromArgb(255, 8, 10, 14)))
        {
            g.FillRectangle(bg, 0, 0, width, height);
        }

        // Draw centered icon scaled appropriately
        int iconSize = (int)(height * 0.65f);
        int iconY = (height - iconSize) / 2;
        int iconX = (int)(width * 0.12f);

        using (var iconResized = ResizeBitmap(master, iconSize, iconSize))
        {
            g.DrawImage(iconResized, iconX, iconY, iconSize, iconSize);
        }

        // Draw sleek typography
        float fontSize = height * 0.18f;
        using var font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(245, 245, 247));
        float textX = iconX + iconSize + (width * 0.05f);
        float textY = (height - (fontSize * 1.4f)) / 2;

        g.DrawString(text, font, textBrush, textX, textY);

        return bmp;
    }

    private static Bitmap ResizeBitmap(Bitmap src, int width, int height)
    {
        var dst = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(src, 0, 0, width, height);
        return dst;
    }

    private static GraphicsPath CreateRoundedRect(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SaveIcoFile(int[] sizes, List<byte[]> pngImages, string outputPath)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR header
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type 1 = ICO
        bw.Write((ushort)sizes.Length); // number of images

        int offset = 6 + (16 * sizes.Length);

        // ICONDIRENTRY list
        for (int i = 0; i < sizes.Length; i++)
        {
            byte w = (byte)(sizes[i] == 256 ? 0 : sizes[i]);
            byte h = (byte)(sizes[i] == 256 ? 0 : sizes[i]);
            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // color planes
            bw.Write((ushort)32); // bits per pixel
            bw.Write((uint)pngImages[i].Length); // bytes in resource
            bw.Write((uint)offset); // offset of resource
            offset += pngImages[i].Length;
        }

        // Image byte data
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngImages[i]);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "desktop", "src", "Assets")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return @"C:\Users\Asus\Documents\Codex\2026-10-24\how\work\XTR-Unlocker";
    }
}

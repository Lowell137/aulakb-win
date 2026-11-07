# Draws Assets\AppIcon.ico and Assets\AppIcon-32.png: a black keycap lit from below by RGB,
# carrying an original blade-cut "A" (gaming-keyboard style; deliberately NOT the Aula logo,
# AulaKb is unofficial). Rendered 8x supersampled per size. Run from the repo root:
#   powershell -ExecutionPolicy Bypass -File make-icon.ps1 [preview-png-dir]
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class AulaIcon
{
    public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    static Color Hsl(double h, double s, double l, int a = 255)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        Func<double, double> f = t =>
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            t = (t + 1) % 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        };
        return Color.FromArgb(a, (int)(f(h + 1 / 3.0) * 255), (int)(f(h) * 255), (int)(f(h - 1 / 3.0) * 255));
    }

    static ColorBlend Rainbow(double light)
    {
        double[] hues = { 190, 250, 300, 345, 25, 50 };
        var cb = new ColorBlend(hues.Length);
        for (int i = 0; i < hues.Length; i++)
        {
            cb.Colors[i] = Hsl(hues[i], 1.0, light);
            cb.Positions[i] = i / (float)(hues.Length - 1);
        }
        return cb;
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad, Math.Min(r.Width, r.Height) / 2) * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static RectangleF R(float l, float t, float r, float b) { return RectangleF.FromLTRB(l, t, r, b); }

    // Blade "A" in unit space: flat-cut apex leaning right, sharp feet, wedge crossbar.
    static readonly PointF[] Legs =
    {
        new PointF(0.15f, 0.79f), new PointF(0.45f, 0.19f), new PointF(0.645f, 0.19f),
        new PointF(0.85f, 0.79f), new PointF(0.675f, 0.70f), new PointF(0.53f, 0.40f),
        new PointF(0.385f, 0.70f),
    };
    static readonly PointF[] Bar =
    {
        new PointF(0.40f, 0.545f), new PointF(0.665f, 0.60f), new PointF(0.39f, 0.65f),
    };

    static void Glyph(Graphics g, float x, float y, float s, Brush b)
    {
        foreach (var poly in new[] { Legs, Bar })
        {
            var pts = new PointF[poly.Length];
            for (int i = 0; i < poly.Length; i++) pts[i] = new PointF(x + poly[i].X * s, y + poly[i].Y * s);
            g.FillPolygon(b, pts);
        }
    }

    public static byte[] Render(int size)
    {
        const int S = 8;
        bool small = size <= 24;
        using (var hi = new Bitmap(size * S, size * S, PixelFormat.Format32bppPArgb))
        {
            using (var g = Graphics.FromImage(hi))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.ScaleTransform(size * S, size * S); // draw in unit space

                float m = small ? 0.0f : 0.05f;
                var cap = R(m, m, 1 - m, 1 - m);
                float rad = small ? 0.19f : 0.20f;

                if (size >= 48)
                    for (int i = 5; i >= 1; i--)
                        using (var sp = Round(R(cap.Left - 0.006f * i, cap.Top + 0.02f, cap.Right + 0.006f * i, cap.Bottom + 0.01f * i), rad + 0.01f * i))
                        using (var sb = new SolidBrush(Color.FromArgb(12, 0, 0, 0)))
                            g.FillPath(sb, sp);

                // keycap skirt: lit by the RGB LED underneath
                using (var cp = Round(cap, rad))
                {
                    using (var rb = new LinearGradientBrush(R(cap.Left - 0.001f, 0, cap.Right + 0.001f, 1), Color.Red, Color.Blue, 0f))
                    {
                        rb.InterpolationColors = Rainbow(0.55);
                        g.FillPath(rb, cp);
                    }
                    // light falls off towards the top of the skirt
                    using (var dark = new LinearGradientBrush(R(0, cap.Top - 0.001f, 1, cap.Bottom + 0.001f), Color.FromArgb(235, 14, 15, 18), Color.FromArgb(0, 14, 15, 18), 90f))
                    {
                        var blend = new Blend(3);
                        blend.Factors = new[] { 0f, 0.15f, 1f };
                        blend.Positions = new[] { 0f, 0.55f, 1f };
                        dark.Blend = blend;
                        g.FillPath(dark, cp);
                    }
                }

                // keycap top face
                var face = small ? R(0.07f, 0.04f, 0.93f, 0.80f) : R(cap.Left + 0.07f, cap.Top + 0.04f, cap.Right - 0.07f, cap.Bottom - 0.15f);
                using (var fp = Round(face, rad * 0.75f))
                {
                    using (var fb = new LinearGradientBrush(R(0, face.Top - 0.001f, 1, face.Bottom + 0.001f), Color.FromArgb(255, 50, 53, 61), Color.FromArgb(255, 22, 23, 27), 90f))
                        g.FillPath(fb, fp);
                    if (!small)
                        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 0.008f))
                            g.DrawPath(pen, fp);
                }

                // blade A
                float gs = face.Width * (small ? 1.06f : 1.0f);
                float gx = face.Left + (face.Width - gs) / 2;
                float gy = face.Top + (face.Height - gs) / 2 + gs * 0.02f;
                using (var ab = new LinearGradientBrush(R(0, gy + 0.2f * gs, 1, gy + 0.74f * gs), Color.White, Color.FromArgb(255, 205, 210, 220), 90f))
                    Glyph(g, gx, gy, gs, ab);
            }

            using (var lo = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var gl = Graphics.FromImage(lo))
                using (var ia = new ImageAttributes())
                {
                    gl.CompositingMode = CompositingMode.SourceCopy;
                    gl.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gl.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    gl.DrawImage(hi, new Rectangle(0, 0, size, size), 0, 0, hi.Width, hi.Height, GraphicsUnit.Pixel, ia);
                }
                using (var ms = new MemoryStream())
                {
                    lo.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }
    }

    public static void WriteIco(string path, string pngDir)
    {
        var frames = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
        {
            frames[i] = Render(Sizes[i]);
            if (!string.IsNullOrEmpty(pngDir)) File.WriteAllBytes(Path.Combine(pngDir, "AppIcon-" + Sizes[i] + ".png"), frames[i]);
        }
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write((byte)(Sizes[i] & 0xFF)); w.Write((byte)(Sizes[i] & 0xFF));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)frames[i].Length); w.Write((uint)offset);
                offset += frames[i].Length;
            }
            foreach (var f in frames) w.Write(f);
        }
    }
}
'@

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ico = Join-Path $root "src\AulaKb.App\Assets\AppIcon.ico"
$pngDir = if ($args.Count -gt 0) { $args[0] } else { $null }
[AulaIcon]::WriteIco($ico, $pngDir)Write-Host "written $ico ($([AulaIcon]::Sizes -join ', ') px)"

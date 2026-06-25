#nullable enable
// PdfSharp 6.x implementation of AbstractGraphics.
// Key differences from PdfSharp 1.5:
//   - No GDI+ dependency (XImage.FromGdiPlusImage removed)
//   - XFont requires IFontResolver on Linux (no system font access)
//   - XGraphicsPath is built incrementally (no PointF[]/byte[] constructor)
//   - XColor replaces System.Drawing.Color throughout
//   - XMatrix constructor arg order matches GDI+ Matrix convention
using Maps.Utilities;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

namespace Maps.Graphics
{
    // Font resolver for Linux: maps common Windows font names to available
    // system TTF files. Set once at startup via GlobalFontSettings.FontResolver.
    internal sealed class LinuxFontResolver : IFontResolver
    {
        private static readonly string FontDir =
            "/usr/share/fonts/truetype/liberation/";

        private static readonly string FallbackDir =
            "/usr/share/fonts/truetype/dejavu/";

        // Map (faceName from ResolveTypeface) → file path
        private static readonly Dictionary<string, string> s_fontMap = BuildFontMap();

        private static Dictionary<string, string> BuildFontMap()
        {
            var d = FontDir;
            var f = FallbackDir;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Sans-serif
                { "LiberationSans",             d + "LiberationSans-Regular.ttf" },
                { "LiberationSans-Bold",         d + "LiberationSans-Bold.ttf" },
                { "LiberationSans-Italic",       d + "LiberationSans-Italic.ttf" },
                { "LiberationSans-BoldItalic",   d + "LiberationSans-BoldItalic.ttf" },
                // Serif
                { "LiberationSerif",             d + "LiberationSerif-Regular.ttf" },
                { "LiberationSerif-Bold",        d + "LiberationSerif-Bold.ttf" },
                { "LiberationSerif-Italic",      d + "LiberationSerif-Italic.ttf" },
                { "LiberationSerif-BoldItalic",  d + "LiberationSerif-BoldItalic.ttf" },
                // Mono
                { "LiberationMono",              d + "LiberationMono-Regular.ttf" },
                // DejaVu fallbacks
                { "DejaVuSans",                  f + "DejaVuSans.ttf" },
                { "DejaVuSans-Bold",             f + "DejaVuSans-Bold.ttf" },
                { "DejaVuSans-Oblique",          f + "DejaVuSans-Oblique.ttf" },
                { "DejaVuSans-BoldOblique",      f + "DejaVuSans-BoldOblique.ttf" },
            };
        }

        // Map a Windows font family name + style to a face name in our map.
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            string base_ = familyName.Split(',')[0].Trim();

            // Classify as sans / serif / mono, then pick Liberation variant.
            string category;
            if (IsSerif(base_))       category = "Serif";
            else if (IsMono(base_))   category = "Mono";
            else                      category = "Sans";

            string face = "Liberation" + category;
            if (bold && italic && category != "Mono") face += "-BoldItalic";
            else if (bold)                            face += "-Bold";
            else if (italic && category != "Mono")    face += "-Italic";

            return s_fontMap.ContainsKey(face)
                ? new FontResolverInfo(face)
                : new FontResolverInfo("DejaVuSans");
        }

        public byte[]? GetFont(string faceName)
        {
            if (s_fontMap.TryGetValue(faceName, out string? path) && File.Exists(path))
                return File.ReadAllBytes(path);
            // Fallback to DejaVu Sans
            string fallback = FallbackDir + "DejaVuSans.ttf";
            return File.Exists(fallback) ? File.ReadAllBytes(fallback) : null;
        }

        private static bool IsSerif(string name) =>
            name.Contains("Serif", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Antiqua", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Roman", StringComparison.OrdinalIgnoreCase);

        private static bool IsMono(string name) =>
            name.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fixed", StringComparison.OrdinalIgnoreCase);

        // Ensure the font resolver is registered before any XFont is created.
        public static void EnsureRegistered()
        {
            if (GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.FontResolver = new LinuxFontResolver();
        }
    }

    internal sealed class PdfSharpGraphics : AbstractGraphics
    {
        private readonly XGraphics _g;
        private XSolidBrush _brush = new XSolidBrush(XColor.FromArgb(0, 0, 0));
        private XPen _pen = new XPen(XColor.FromArgb(0, 0, 0));

        public PdfSharpGraphics(XGraphics g)
        {
            LinuxFontResolver.EnsureRegistered();
            _g = g;
        }

        // ── Colour helpers ────────────────────────────────────────────────────

        private static XColor ToXColor(Color c) =>
            XColor.FromArgb(c.A, c.R, c.G, c.B);

        private void ApplyBrush(AbstractBrush brush)
        {
            _brush.Color = ToXColor(brush.Color);
        }

        private void ApplyPen(AbstractPen pen)
        {
            _pen.Color = ToXColor(pen.Color);
            _pen.Width = pen.Width;
            _pen.DashStyle = pen.DashStyle switch
            {
                DashStyle.Solid      => XDashStyle.Solid,
                DashStyle.Dot        => XDashStyle.Dot,
                DashStyle.Dash       => XDashStyle.Dash,
                DashStyle.DashDot    => XDashStyle.DashDot,
                DashStyle.DashDotDot => XDashStyle.DashDotDot,
                DashStyle.Custom     => XDashStyle.Custom,
                _                    => XDashStyle.Solid
            };
            if (pen.CustomDashPattern != null)
                _pen.DashPattern = pen.CustomDashPattern.Select(f => (double)f).ToArray();
        }

        private void ApplyPenBrush(AbstractPen pen, AbstractBrush brush)
        {
            ApplyPen(pen); ApplyBrush(brush);
        }

        // ── Font helpers ──────────────────────────────────────────────────────

        private readonly Dictionary<string, XFont> _fontCache = new();

        private XFont GetXFont(AbstractFont font)
        {
            string key = $"{font.Families}|{font.Size}|{font.Style}";
            if (_fontCache.TryGetValue(key, out var cached)) return cached;

            string family = font.Families.Split(',')[0].Trim();
            XFontStyleEx xstyle = (font.Bold && font.Italic) ? XFontStyleEx.BoldItalic
                                : font.Bold                  ? XFontStyleEx.Bold
                                : font.Italic                ? XFontStyleEx.Italic
                                                             : XFontStyleEx.Regular;
            var xfont = new XFont(family, font.Size, xstyle);
            _fontCache[key] = xfont;
            return xfont;
        }

        // ── Path helpers ──────────────────────────────────────────────────────

        // Converts AbstractPath (GDI+ PathPointType byte flags) to XGraphicsPath.
        // PathPointType: 0=Start, 1=Line, 3=Bezier, 0x80=CloseSubpath
        private static XGraphicsPath ToXPath(AbstractPath path)
        {
            var xp = new XGraphicsPath();
            var pts = path.Points;
            var types = path.Types;

            for (int i = 0; i < pts.Length; i++)
            {
                byte type = i < types.Length ? types[i] : (byte)1;
                switch (type & 0x07)
                {
                    case 0: // MoveTo
                        xp.StartFigure();
                        break;
                    case 1: // LineTo
                        if (i > 0)
                        {
                            int prev = FindLastMove(types, i);
                            xp.AddLine(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y);
                        }
                        break;
                    case 3: // BezierTo (4 points: p0, cp1, cp2, p3)
                        if (i + 2 < pts.Length)
                        {
                            xp.AddBezier(
                                pts[i - 1].X, pts[i - 1].Y,
                                pts[i].X,     pts[i].Y,
                                pts[i + 1].X, pts[i + 1].Y,
                                pts[i + 2].X, pts[i + 2].Y);
                            i += 2;
                        }
                        break;
                }
                if ((type & 0x80) != 0)
                    xp.CloseFigure();
            }
            return xp;
        }

        private static int FindLastMove(byte[] types, int before)
        {
            for (int i = before - 1; i >= 0; i--)
                if ((types[i] & 0x07) == 0) return i;
            return 0;
        }

        // ── AbstractMatrix → XMatrix ──────────────────────────────────────────

        private static XMatrix ToXMatrix(AbstractMatrix m) =>
            new XMatrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);

        // ── Interface implementation ──────────────────────────────────────────

        public System.Drawing.Graphics? Graphics => null; // PdfSharp 6.x has no GDI backing
        public bool SupportsWingdings => false; // Wingdings not available on Linux

        private SmoothingMode _smoothingMode = SmoothingMode.AntiAlias;
        public SmoothingMode SmoothingMode
        {
            get => _smoothingMode;
            set
            {
                _smoothingMode = value;
                // XSmoothingMode values match SmoothingMode values numerically
                _g.SmoothingMode = (XSmoothingMode)(int)value;
            }
        }

        public void ScaleTransform(float scaleXY) => _g.ScaleTransform(scaleXY);
        public void ScaleTransform(float sx, float sy) => _g.ScaleTransform(sx, sy);
        public void TranslateTransform(float dx, float dy) => _g.TranslateTransform(dx, dy);
        public void RotateTransform(float angle) => _g.RotateTransform(angle);
        public void MultiplyTransform(AbstractMatrix m) => _g.MultiplyTransform(ToXMatrix(m));

        public void IntersectClip(AbstractPath path) =>
            _g.IntersectClip(ToXPath(path));
        public void IntersectClip(RectangleF rect) =>
            _g.IntersectClip(new XRect(rect.X, rect.Y, rect.Width, rect.Height));

        public void DrawLine(AbstractPen pen, float x1, float y1, float x2, float y2)
        { ApplyPen(pen); _g.DrawLine(_pen, x1, y1, x2, y2); }
        public void DrawLine(AbstractPen pen, PointF pt1, PointF pt2)
        { ApplyPen(pen); _g.DrawLine(_pen, pt1.X, pt1.Y, pt2.X, pt2.Y); }
        public void DrawLines(AbstractPen pen, PointF[] points)
        { ApplyPen(pen); _g.DrawLines(_pen, points.Select(p => new XPoint(p.X, p.Y)).ToArray()); }
        public void DrawPath(AbstractPen pen, AbstractPath path)
        { ApplyPen(pen); var xp = ToXPath(path); _g.DrawPath(_pen, xp); }
        public void DrawPath(AbstractBrush brush, AbstractPath path)
        { ApplyBrush(brush); var xp = ToXPath(path); _g.DrawPath(_brush, xp); }
        public void DrawCurve(AbstractPen pen, PointF[] points, float tension = 0.5f)
        { ApplyPen(pen); _g.DrawCurve(_pen, points.Select(p => new XPoint(p.X, p.Y)).ToArray(), tension); }
        public void DrawClosedCurve(AbstractPen pen, PointF[] points, float tension = 0.5f)
        { ApplyPen(pen); _g.DrawClosedCurve(_pen, points.Select(p => new XPoint(p.X, p.Y)).ToArray(), tension); }
        public void DrawClosedCurve(AbstractBrush brush, PointF[] points, float tension = 0.5f)
        { ApplyBrush(brush); _g.DrawClosedCurve(_brush, points.Select(p => new XPoint(p.X, p.Y)).ToArray(), XFillMode.Alternate, tension); }
        public void DrawRectangle(AbstractPen pen, float x, float y, float width, float height)
        { ApplyPen(pen); _g.DrawRectangle(_pen, x, y, width, height); }
        public void DrawRectangle(AbstractPen pen, RectangleF rect)
        { ApplyPen(pen); _g.DrawRectangle(_pen, rect.X, rect.Y, rect.Width, rect.Height); }
        public void DrawRectangle(AbstractBrush brush, float x, float y, float width, float height)
        { ApplyBrush(brush); _g.DrawRectangle(_brush, x, y, width, height); }
        public void DrawRectangle(AbstractBrush brush, RectangleF rect)
        { ApplyBrush(brush); _g.DrawRectangle(_brush, rect.X, rect.Y, rect.Width, rect.Height); }
        public void DrawEllipse(AbstractPen pen, float x, float y, float width, float height)
        { ApplyPen(pen); _g.DrawEllipse(_pen, x, y, width, height); }
        public void DrawEllipse(AbstractBrush brush, float x, float y, float width, float height)
        { ApplyBrush(brush); _g.DrawEllipse(_brush, x, y, width, height); }
        public void DrawEllipse(AbstractPen pen, AbstractBrush brush, float x, float y, float width, float height)
        { ApplyPenBrush(pen, brush); _g.DrawEllipse(_pen, _brush, x, y, width, height); }
        public void DrawArc(AbstractPen pen, float x, float y, float width, float height, float startAngle, float sweepAngle)
        { ApplyPen(pen); _g.DrawArc(_pen, x, y, width, height, startAngle, sweepAngle); }

        // ── Image rendering ───────────────────────────────────────────────────

        public void DrawImage(AbstractImage image, float x, float y, float width, float height)
        {
            using var xImage = LoadXImage(image.SKBitmap);
            _g.DrawImage(xImage, x, y, width, height);
        }

        public void DrawImageAlpha(float alpha, AbstractImage image, RectangleF targetRect)
        {
            if (alpha <= 0) return;
            alpha = Math.Clamp(alpha, 0f, 1f);
            alpha = (float)Math.Round(alpha * 16) / 16;
            if (alpha >= 1f)
            {
                DrawImage(image, targetRect.X, targetRect.Y, targetRect.Width, targetRect.Height);
                return;
            }
            // Composite alpha using SkiaSharp, then load as XImage
            using var xImage = LoadXImageWithAlpha(image.SKBitmap, alpha);
            _g.DrawImage(xImage, targetRect.X, targetRect.Y, targetRect.Width, targetRect.Height);
        }

        private static XImage LoadXImage(SKBitmap bitmap)
        {
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(encoded.ToArray());
            return XImage.FromStream(ms);
        }

        private static XImage LoadXImageWithAlpha(SKBitmap src, float alpha)
        {
            using var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(dst);
            byte a = (byte)(alpha * 255);
            using var paint = new SKPaint { Color = new SKColor(255, 255, 255, a) };
            canvas.DrawBitmap(src, 0, 0, paint);
            using var encoded = dst.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(encoded.ToArray());
            return XImage.FromStream(ms);
        }

        // ── Text ─────────────────────────────────────────────────────────────

        public SizeF MeasureString(string text, AbstractFont font)
        {
            var xfont = GetXFont(font);
            var size = _g.MeasureString(text, xfont);
            return new SizeF((float)size.Width, (float)size.Height);
        }

        public void DrawString(string s, AbstractFont font, AbstractBrush brush, float x, float y, StringAlignment alignment)
        {
            ApplyBrush(brush);
            var xfont = GetXFont(font);
            _g.DrawString(s, xfont, _brush, x, y, ToXStringFormat(alignment));
        }

        private static readonly Dictionary<StringAlignment, XStringFormat> s_formats = new()
        {
            [StringAlignment.Baseline]   = new XStringFormat { Alignment = XStringAlignment.Near,   LineAlignment = XLineAlignment.BaseLine },
            [StringAlignment.Centered]   = new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Center },
            [StringAlignment.TopLeft]    = new XStringFormat { Alignment = XStringAlignment.Near,   LineAlignment = XLineAlignment.Near },
            [StringAlignment.TopCenter]  = new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Near },
            [StringAlignment.TopRight]   = new XStringFormat { Alignment = XStringAlignment.Far,    LineAlignment = XLineAlignment.Near },
            [StringAlignment.CenterLeft] = new XStringFormat { Alignment = XStringAlignment.Near,   LineAlignment = XLineAlignment.Center },
        };

        private static XStringFormat ToXStringFormat(StringAlignment a) =>
            s_formats.TryGetValue(a, out var fmt) ? fmt : s_formats[StringAlignment.TopLeft];

        // ── State save/restore ────────────────────────────────────────────────

        public AbstractGraphicsState Save() => new PdfState(this, _g.Save());

        public void Restore(AbstractGraphicsState state)
        {
            if (state is PdfState s) _g.Restore(s.SavedState);
        }

        private sealed class PdfState : AbstractGraphicsState
        {
            public readonly XGraphicsState SavedState;
            public PdfState(AbstractGraphics g, XGraphicsState s) : base(g) { SavedState = s; }
        }

        // ── Disposal ──────────────────────────────────────────────────────────

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _g.Dispose();
            _disposed = true;
        }
    }
}

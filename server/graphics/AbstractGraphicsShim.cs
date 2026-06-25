// AbstractGraphics interface and shared types for the rendering pipeline.
// System.Drawing value types (Color, PointF, RectangleF, SizeF) are pure structs
// that work correctly on Linux/net8.0 via System.Drawing.Common.
// GDI+ *objects* (Bitmap, Graphics, Font, Brush, Pen, GraphicsPath) are NOT used
// here — those live only in BitmapGraphics.cs (SkiaSharp) and PdfSharpGraphics.cs.
#nullable enable
using SkiaSharp;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Maps.Graphics
{
#pragma warning disable IDE1006 // Naming Styles
    internal interface AbstractGraphics : IDisposable
#pragma warning restore IDE1006 // Naming Styles
    {
        SmoothingMode SmoothingMode { get; set; }
        // Returns the underlying System.Drawing.Graphics for callers that need it;
        // SkiaSharp-backed implementations return null.
        System.Drawing.Graphics? Graphics { get; }
        bool SupportsWingdings { get; }

        void ScaleTransform(float scaleXY);
        void ScaleTransform(float scaleX, float scaleY);
        void TranslateTransform(float dx, float dy);
        void RotateTransform(float angle);
        void MultiplyTransform(AbstractMatrix m);

        void IntersectClip(AbstractPath path);
        void IntersectClip(RectangleF rect);

        void DrawLine(AbstractPen pen, float x1, float y1, float x2, float y2);
        void DrawLine(AbstractPen pen, PointF pt1, PointF pt2);
        void DrawLines(AbstractPen pen, PointF[] points);
        void DrawPath(AbstractPen pen, AbstractPath path);
        void DrawPath(AbstractBrush brush, AbstractPath path);
        void DrawCurve(AbstractPen pen, PointF[] points, float tension = 0.5f);
        void DrawClosedCurve(AbstractPen pen, PointF[] points, float tension = 0.5f);
        void DrawClosedCurve(AbstractBrush brush, PointF[] points, float tension = 0.5f);
        void DrawRectangle(AbstractPen pen, float x, float y, float width, float height);
        void DrawRectangle(AbstractPen pen, RectangleF rect);
        void DrawRectangle(AbstractBrush brush, float x, float y, float width, float height);
        void DrawRectangle(AbstractBrush brush, RectangleF rect);
        void DrawEllipse(AbstractPen pen, float x, float y, float width, float height);
        void DrawEllipse(AbstractBrush brush, float x, float y, float width, float height);
        void DrawEllipse(AbstractPen pen, AbstractBrush brush, float x, float y, float width, float height);
        void DrawArc(AbstractPen pen, float x, float y, float width, float height, float startAngle, float sweepAngle);

        void DrawImage(AbstractImage image, float x, float y, float width, float height);
        void DrawImageAlpha(float alpha, AbstractImage image, RectangleF targetRect);

        SizeF MeasureString(string text, AbstractFont font);
        void DrawString(string s, AbstractFont font, AbstractBrush brush, float x, float y, StringAlignment format);

        AbstractGraphicsState Save();
        void Restore(AbstractGraphicsState state);
    }

    internal abstract class AbstractGraphicsState : IDisposable
    {
        private AbstractGraphics? g;

        protected AbstractGraphicsState(AbstractGraphics graphics) { g = graphics; }

        public void Restore() { g!.Restore(this); g = null; }

        public void Dispose() { g?.Restore(this); g = null; }
    }

    // Stores a 2D affine transform matrix as six floats.
    // GDI+ layout: (x', y') = (x*M11 + y*M21 + OffsetX, x*M12 + y*M22 + OffsetY)
    internal struct AbstractMatrix
    {
        public float M11, M12, M21, M22, OffsetX, OffsetY;

        public AbstractMatrix(float m11, float m12, float m21, float m22, float dx, float dy)
        {
            M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = dx; OffsetY = dy;
        }

        public void Invert()
        {
            float det = M11 * M22 - M12 * M21;
            if (det == 0) return;
            float inv = 1f / det;
            (M11, M12, M21, M22) = (M22 * inv, -M12 * inv, -M21 * inv, M11 * inv);
            float tx = -OffsetX, ty = -OffsetY;
            OffsetX = tx * M11 + ty * M21;
            OffsetY = tx * M12 + ty * M22;
        }

        public void RotatePrepend(float angleDegrees)
        {
            float r = (float)(angleDegrees * Math.PI / 180.0);
            float cos = (float)Math.Cos(r), sin = (float)Math.Sin(r);
            float m11 = M11, m12 = M12, m21 = M21, m22 = M22;
            M11 = cos * m11 + sin * m21; M12 = cos * m12 + sin * m22;
            M21 = -sin * m11 + cos * m21; M22 = -sin * m12 + cos * m22;
        }

        public void ScalePrepend(float sx, float sy)
        {
            M11 *= sx; M12 *= sx;
            M21 *= sy; M22 *= sy;
        }

        public void TranslatePrepend(float dx, float dy)
        {
            OffsetX += dx * M11 + dy * M21;
            OffsetY += dx * M12 + dy * M22;
        }

        public void Prepend(AbstractMatrix m)
        {
            float r11 = m.M11 * M11 + m.M12 * M21;
            float r12 = m.M11 * M12 + m.M12 * M22;
            float r21 = m.M21 * M11 + m.M22 * M21;
            float r22 = m.M21 * M12 + m.M22 * M22;
            float rdx = m.OffsetX * M11 + m.OffsetY * M21 + OffsetX;
            float rdy = m.OffsetX * M12 + m.OffsetY * M22 + OffsetY;
            M11 = r11; M12 = r12; M21 = r21; M22 = r22; OffsetX = rdx; OffsetY = rdy;
        }

        // Convert to SkiaSharp matrix.
        // GDI+ row-vector convention maps to SkiaSharp column-vector:
        //   SKMatrix.ScaleX = M11, SKMatrix.SkewX = M21, SKMatrix.TransX = OffsetX
        //   SKMatrix.SkewY  = M12, SKMatrix.ScaleY = M22, SKMatrix.TransY = OffsetY
        public SKMatrix ToSKMatrix() =>
            new SKMatrix(M11, M21, OffsetX,
                         M12, M22, OffsetY,
                         0,   0,   1);

        public static readonly AbstractMatrix Identity = new AbstractMatrix(1, 0, 0, 1, 0, 0);
    }

    // Image loaded from disk. XImage (PdfSharp) support restored in issue #6.
    internal class AbstractImage
    {
        private readonly string path;
        private string? dataUrl;
        private SKBitmap? bitmap;

        public string Url { get; }

        public string DataUrl
        {
            get
            {
                if (dataUrl == null)
                {
                    string contentType = Utilities.ContentTypes.TypeForPath(path);
                    byte[] bytes = File.ReadAllBytes(path);
                    dataUrl = "data:" + contentType + ";base64," + Convert.ToBase64String(bytes, Base64FormattingOptions.None);
                }
                return dataUrl;
            }
        }

        public SKBitmap SKBitmap
        {
            get
            {
                lock (this)
                {
                    if (bitmap == null)
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        bitmap = SKBitmap.Decode(stream);
                    }
                    return bitmap;
                }
            }
        }

        public AbstractImage(string path, string url)
        {
            this.path = path;
            Url = url;
        }
    }

    internal class AbstractPen
    {
        // System.Drawing.Color is a pure value struct — safe on Linux.
        public Color Color { get; set; }
        public float Width { get; set; }
        public DashStyle DashStyle { get; set; } = DashStyle.Solid;
        public float[]? CustomDashPattern { get; set; }

        public AbstractPen() { }
        public AbstractPen(Color color, float width = 1) { Color = color; Width = width; }
    }

    internal class AbstractBrush
    {
        // System.Drawing.Color is a pure value struct — safe on Linux.
        public Color Color { get; set; }
        public AbstractBrush() { }
        public AbstractBrush(Color color) { Color = color; }
    }

    // Font description: families (comma-separated fallback list), size, and style flags.
    // Does NOT hold a System.Drawing.Font object (throws on Linux).
    // BitmapGraphics creates SKFont on demand; SVGGraphics uses string properties.
    internal class AbstractFont
    {
        public string Families { get; }
        public float Size { get; }

        // FontStyle is a pure enum value — safe on Linux.
        public FontStyle Style { get; }
        public bool Italic    => Style.HasFlag(FontStyle.Italic);
        public bool Bold      => Style.HasFlag(FontStyle.Bold);
        public bool Underline => Style.HasFlag(FontStyle.Underline);
        public bool Strikeout => Style.HasFlag(FontStyle.Strikeout);

        public AbstractFont(string families, float emSize, FontStyle style, GraphicsUnit units)
        {
            Families = families;
            Size = emSize;
            Style = style;
        }

        // Font metrics via SkiaSharp — replaces GDI+ FontFamily.GetCellAscent/GetLineSpacing.
        private SKFontMetrics GetMetrics()
        {
            var skStyle = (Bold && Italic) ? SKFontStyle.BoldItalic
                        : Bold             ? SKFontStyle.Bold
                        : Italic           ? SKFontStyle.Italic
                                           : SKFontStyle.Normal;
            var typeface = SKTypeface.FromFamilyName(Families.Split(',')[0].Trim(), skStyle)
                         ?? SKTypeface.Default;
            using var skFont = new SKFont(typeface, Size);
            skFont.GetFontMetrics(out var metrics);
            return metrics;
        }

        public float GetLineSpacing()
        {
            var m = GetMetrics();
            return -m.Ascent + m.Descent + m.Leading;
        }

        public float GetAscent()
        {
            var m = GetMetrics();
            return -m.Ascent; // Ascent is negative in SkiaSharp
        }
    }

    internal enum StringAlignment
    {
        Baseline,
        Centered,
        TopLeft,
        TopCenter,
        TopRight,
        CenterLeft,
    }

    internal class AbstractPath
    {
        public PointF[] Points { get; set; }
        public byte[] Types { get; set; }

        public AbstractPath(PointF[] points, byte[] types) { Points = points; Types = types; }
    }

    internal enum DashStyle { Solid, Dot, Dash, DashDot, DashDotDot, Custom }

    // Stub; replaced by full PdfSharp 6.x implementation in issue #6.
    internal sealed class PdfSharpGraphics : AbstractGraphics
    {
        internal PdfSharpGraphics(PdfSharp.Drawing.XGraphics gfx) =>
            throw new NotImplementedException("PdfSharpGraphics: ported in issue #6");
        public SmoothingMode SmoothingMode { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public System.Drawing.Graphics? Graphics => null;
        public bool SupportsWingdings => throw new NotImplementedException();
        public void ScaleTransform(float s) => throw new NotImplementedException();
        public void ScaleTransform(float x, float y) => throw new NotImplementedException();
        public void TranslateTransform(float dx, float dy) => throw new NotImplementedException();
        public void RotateTransform(float a) => throw new NotImplementedException();
        public void MultiplyTransform(AbstractMatrix m) => throw new NotImplementedException();
        public void IntersectClip(AbstractPath p) => throw new NotImplementedException();
        public void IntersectClip(RectangleF r) => throw new NotImplementedException();
        public void DrawLine(AbstractPen p, float x1, float y1, float x2, float y2) => throw new NotImplementedException();
        public void DrawLine(AbstractPen p, PointF a, PointF b) => throw new NotImplementedException();
        public void DrawLines(AbstractPen p, PointF[] pts) => throw new NotImplementedException();
        public void DrawPath(AbstractPen p, AbstractPath path) => throw new NotImplementedException();
        public void DrawPath(AbstractBrush b, AbstractPath path) => throw new NotImplementedException();
        public void DrawCurve(AbstractPen p, PointF[] pts, float t = 0.5f) => throw new NotImplementedException();
        public void DrawClosedCurve(AbstractPen p, PointF[] pts, float t = 0.5f) => throw new NotImplementedException();
        public void DrawClosedCurve(AbstractBrush b, PointF[] pts, float t = 0.5f) => throw new NotImplementedException();
        public void DrawRectangle(AbstractPen p, float x, float y, float w, float h) => throw new NotImplementedException();
        public void DrawRectangle(AbstractPen p, RectangleF r) => throw new NotImplementedException();
        public void DrawRectangle(AbstractBrush b, float x, float y, float w, float h) => throw new NotImplementedException();
        public void DrawRectangle(AbstractBrush b, RectangleF r) => throw new NotImplementedException();
        public void DrawEllipse(AbstractPen p, float x, float y, float w, float h) => throw new NotImplementedException();
        public void DrawEllipse(AbstractBrush b, float x, float y, float w, float h) => throw new NotImplementedException();
        public void DrawEllipse(AbstractPen p, AbstractBrush b, float x, float y, float w, float h) => throw new NotImplementedException();
        public void DrawArc(AbstractPen p, float x, float y, float w, float h, float sa, float sw) => throw new NotImplementedException();
        public void DrawImage(AbstractImage img, float x, float y, float w, float h) => throw new NotImplementedException();
        public void DrawImageAlpha(float a, AbstractImage img, RectangleF r) => throw new NotImplementedException();
        public SizeF MeasureString(string t, AbstractFont f) => throw new NotImplementedException();
        public void DrawString(string s, AbstractFont f, AbstractBrush b, float x, float y, StringAlignment a) => throw new NotImplementedException();
        public AbstractGraphicsState Save() => throw new NotImplementedException();
        public void Restore(AbstractGraphicsState s) => throw new NotImplementedException();
        public void Dispose() { }
    }
}

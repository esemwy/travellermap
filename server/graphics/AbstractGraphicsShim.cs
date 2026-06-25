// Replaces AbstractGraphics.cs for the net8.0 build.
// AbstractGraphics.cs depended on PdfSharp 1.5 GDI-only APIs
// (XMatrix.ToGdiMatrix, XImage.FromGdiPlusImage) that do not exist in
// cross-platform PdfSharp 6.x. This shim preserves the same public types
// and interface so the rest of the codebase compiles unchanged.
// AbstractMatrix and AbstractImage are simplified; full SkiaSharp
// implementations replace them in issue #5.
#nullable enable
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

        protected AbstractGraphicsState(AbstractGraphics graphics)
        {
            g = graphics;
        }

        public void Restore()
        {
            g!.Restore(this);
            g = null;
        }

        public void Dispose()
        {
            g?.Restore(this);
            g = null;
        }
    }

    // AbstractMatrix: was backed by PdfSharp XMatrix; simplified to float fields
    // until full SkiaSharp implementation in issue #5.
    internal struct AbstractMatrix
    {
        public float M11, M12, M21, M22, OffsetX, OffsetY;

        public AbstractMatrix(float m11, float m12, float m21, float m22, float dx, float dy)
        {
            M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = dx; OffsetY = dy;
        }

        public void Invert() => throw new NotImplementedException();
        public void RotatePrepend(float angle) => throw new NotImplementedException();
        public void ScalePrepend(float sx, float sy) => throw new NotImplementedException();
        public void TranslatePrepend(float dx, float dy) => throw new NotImplementedException();
        public void Prepend(AbstractMatrix m) => throw new NotImplementedException();

        public static readonly AbstractMatrix Identity = new(1, 0, 0, 1, 0, 0);
    }

    // AbstractImage: XImage property removed (PdfSharp 1.5 GDI-only);
    // restored in issue #6 with PdfSharp 6.x API.
    internal class AbstractImage
    {
        private readonly string path;
        private string? dataUrl;

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

        public AbstractImage(string path, string url)
        {
            this.path = path;
            Url = url;
        }
    }

    internal class AbstractPen
    {
        public Color Color { get; set; }
        public float Width { get; set; }
        public DashStyle DashStyle { get; set; } = DashStyle.Solid;
        public float[]? CustomDashPattern { get; set; }

        public AbstractPen() { }
        public AbstractPen(Color color, float width = 1)
        {
            Color = color;
            Width = width;
        }
    }

    internal class AbstractBrush
    {
        public Color Color { get; set; }
        public AbstractBrush() { }
        public AbstractBrush(Color color)
        {
            Color = color;
        }
    }

    internal class AbstractFont
    {
        public string Families { get; }
        public Font Font { get; set; }
        public FontStyle Style => Font.Style;
        public float Size => Font.Size;
        public bool Italic => Font.Italic;
        public bool Bold => Font.Bold;
        public bool Underline => Font.Underline;
        public bool Strikeout => Font.Strikeout;
        public FontFamily FontFamily => Font.FontFamily;

        public AbstractFont(string families, float emSize, FontStyle style, GraphicsUnit units)
        {
            Families = families;
            foreach (var family in families.Split(','))
            {
                Font = new Font(family, emSize, style, units);
                if (Font.Name == family)
                    return;
            }
            throw new ApplicationException("No matching font family");
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

        public AbstractPath(PointF[] points, byte[] types)
        {
            Points = points;
            Types = types;
        }
    }

    internal enum DashStyle
    {
        Solid,
        Dot,
        Dash,
        DashDot,
        DashDotDot,
        Custom,
    }
}

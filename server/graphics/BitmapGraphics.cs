#nullable enable
using Maps.Utilities;
using SkiaSharp;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Maps.Graphics
{
    // AbstractGraphics implementation backed by SkiaSharp.
    // Replaces the GDI+-based implementation for cross-platform Linux support.
    internal sealed class BitmapGraphics : AbstractGraphics
    {
        private readonly SKCanvas _canvas;
        private bool _disposed;

        // Reused paint objects to avoid per-call allocations.
        private readonly SKPaint _fillPaint = new SKPaint { IsAntialias = true };
        private readonly SKPaint _strokePaint = new SKPaint { IsAntialias = true, IsStroke = true };

        public BitmapGraphics(SKCanvas canvas) { _canvas = canvas; }

        // Kept for API compatibility; SkiaSharp doesn't expose GDI Graphics.
        public System.Drawing.Graphics? Graphics => null;

        public bool SupportsWingdings => false;

        // SkiaSharp antialiasing is controlled per-paint, not globally.
        // Store the mode so callers can read it back.
        private SmoothingMode _smoothingMode = SmoothingMode.AntiAlias;
        public SmoothingMode SmoothingMode
        {
            get => _smoothingMode;
            set
            {
                _smoothingMode = value;
                bool aa = value != SmoothingMode.None && value != SmoothingMode.HighSpeed;
                _fillPaint.IsAntialias = aa;
                _strokePaint.IsAntialias = aa;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static SKColor ToSK(Color c) => new SKColor(c.R, c.G, c.B, c.A);

        private void ApplyBrush(AbstractBrush brush)
        {
            _fillPaint.Color = ToSK(brush.Color);
            _fillPaint.Shader = null;
        }

        private void ApplyPen(AbstractPen pen)
        {
            _strokePaint.Color = ToSK(pen.Color);
            _strokePaint.StrokeWidth = pen.Width;
            _strokePaint.PathEffect = null;

            switch (pen.DashStyle)
            {
                case DashStyle.Dot:
                    _strokePaint.PathEffect = SKPathEffect.CreateDash(
                        new[] { pen.Width, pen.Width }, 0);
                    break;
                case DashStyle.Dash:
                    _strokePaint.PathEffect = SKPathEffect.CreateDash(
                        new[] { pen.Width * 2, pen.Width }, 0);
                    break;
                case DashStyle.DashDot:
                    _strokePaint.PathEffect = SKPathEffect.CreateDash(
                        new[] { pen.Width * 2, pen.Width, pen.Width, pen.Width }, 0);
                    break;
                case DashStyle.DashDotDot:
                    _strokePaint.PathEffect = SKPathEffect.CreateDash(
                        new[] { pen.Width * 2, pen.Width, pen.Width, pen.Width, pen.Width, pen.Width }, 0);
                    break;
                case DashStyle.Custom when pen.CustomDashPattern != null:
                    _strokePaint.PathEffect = SKPathEffect.CreateDash(
                        pen.CustomDashPattern.Select(f => f * pen.Width).ToArray(), 0);
                    break;
            }
        }

        private static SKPath ToSKPath(AbstractPath path)
        {
            var skPath = new SKPath { FillType = SKPathFillType.Winding };
            int i = 0;
            foreach (var pt in path.Points)
            {
                byte type = i < path.Types.Length ? path.Types[i] : (byte)0;
                // PathPointType: 0=Start, 1=Line, 3=Bezier
                switch (type & 0x07)
                {
                    case 0: skPath.MoveTo(pt.X, pt.Y); break;
                    default: skPath.LineTo(pt.X, pt.Y); break;
                }
                i++;
            }
            if (i > 0 && (path.Types[^1] & 0x80) != 0)
                skPath.Close();
            return skPath;
        }

        private SKFont CreateFont(AbstractFont font)
        {
            var skStyle = SKFontStyle.Normal;
            if (font.Bold && font.Italic) skStyle = SKFontStyle.BoldItalic;
            else if (font.Bold)           skStyle = SKFontStyle.Bold;
            else if (font.Italic)         skStyle = SKFontStyle.Italic;

            SKTypeface? typeface = null;
            foreach (var family in font.Families.Split(',').Select(f => f.Trim()))
            {
                var tf = SKTypeface.FromFamilyName(family, skStyle);
                if (tf != null && tf.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
                {
                    typeface = tf;
                    break;
                }
            }
            typeface ??= SKTypeface.FromFamilyName(font.Families.Split(',')[0].Trim(), skStyle)
                      ?? SKTypeface.Default;
            return new SKFont(typeface, font.Size);
        }

        // ── Transforms ───────────────────────────────────────────────────────

        public void ScaleTransform(float scaleXY) => _canvas.Scale(scaleXY, scaleXY);
        public void ScaleTransform(float sx, float sy) => _canvas.Scale(sx, sy);
        public void TranslateTransform(float dx, float dy) => _canvas.Translate(dx, dy);
        public void RotateTransform(float angle) => _canvas.RotateDegrees(angle);

        public void MultiplyTransform(AbstractMatrix m)
        {
            var skm = m.ToSKMatrix();
            _canvas.Concat(ref skm);
        }

        // ── Clipping ─────────────────────────────────────────────────────────

        public void IntersectClip(AbstractPath path)
        {
            using var skPath = ToSKPath(path);
            _canvas.ClipPath(skPath, SKClipOperation.Intersect, antialias: true);
        }

        public void IntersectClip(RectangleF rect) =>
            _canvas.ClipRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
                SKClipOperation.Intersect, antialias: true);

        // ── Lines ────────────────────────────────────────────────────────────

        public void DrawLine(AbstractPen pen, float x1, float y1, float x2, float y2)
        {
            ApplyPen(pen);
            _canvas.DrawLine(x1, y1, x2, y2, _strokePaint);
        }

        public void DrawLine(AbstractPen pen, PointF pt1, PointF pt2)
        {
            ApplyPen(pen);
            _canvas.DrawLine(pt1.X, pt1.Y, pt2.X, pt2.Y, _strokePaint);
        }

        public void DrawLines(AbstractPen pen, PointF[] points)
        {
            if (points.Length < 2) return;
            ApplyPen(pen);
            using var path = new SKPath();
            path.MoveTo(points[0].X, points[0].Y);
            for (int i = 1; i < points.Length; i++)
                path.LineTo(points[i].X, points[i].Y);
            _canvas.DrawPath(path, _strokePaint);
        }

        // ── Paths ────────────────────────────────────────────────────────────

        public void DrawPath(AbstractPen pen, AbstractPath path)
        {
            ApplyPen(pen);
            using var skPath = ToSKPath(path);
            _canvas.DrawPath(skPath, _strokePaint);
        }

        public void DrawPath(AbstractBrush brush, AbstractPath path)
        {
            ApplyBrush(brush);
            using var skPath = ToSKPath(path);
            _canvas.DrawPath(skPath, _fillPaint);
        }

        // ── Curves ───────────────────────────────────────────────────────────

        public void DrawCurve(AbstractPen pen, PointF[] points, float tension = 0.5f)
        {
            if (points.Length < 2) return;
            ApplyPen(pen);
            using var path = CatmullRomPath(points, tension, closed: false);
            _canvas.DrawPath(path, _strokePaint);
        }

        public void DrawClosedCurve(AbstractPen pen, PointF[] points, float tension = 0.5f)
        {
            if (points.Length < 2) return;
            ApplyPen(pen);
            using var path = CatmullRomPath(points, tension, closed: true);
            _canvas.DrawPath(path, _strokePaint);
        }

        public void DrawClosedCurve(AbstractBrush brush, PointF[] points, float tension = 0.5f)
        {
            if (points.Length < 2) return;
            ApplyBrush(brush);
            using var path = CatmullRomPath(points, tension, closed: true);
            _canvas.DrawPath(path, _fillPaint);
        }

        // Catmull-Rom spline approximation via cubic Bezier segments.
        private static SKPath CatmullRomPath(PointF[] pts, float tension, bool closed)
        {
            var path = new SKPath();
            float alpha = tension;

            PointF[] augmented;
            if (closed)
            {
                augmented = new PointF[pts.Length + 3];
                augmented[0] = pts[^1];
                Array.Copy(pts, 0, augmented, 1, pts.Length);
                augmented[^2] = pts[0];
                augmented[^1] = pts[1];
            }
            else
            {
                augmented = new PointF[pts.Length + 2];
                augmented[0] = new PointF(2 * pts[0].X - pts[1].X, 2 * pts[0].Y - pts[1].Y);
                Array.Copy(pts, 0, augmented, 1, pts.Length);
                int last = pts.Length - 1;
                augmented[^1] = new PointF(2 * pts[last].X - pts[last - 1].X, 2 * pts[last].Y - pts[last - 1].Y);
            }

            path.MoveTo(augmented[1].X, augmented[1].Y);

            for (int i = 1; i < augmented.Length - 2; i++)
            {
                var p0 = augmented[i - 1];
                var p1 = augmented[i];
                var p2 = augmented[i + 1];
                var p3 = augmented[i + 2];

                float cp1x = p1.X + alpha * (p2.X - p0.X) / 6f;
                float cp1y = p1.Y + alpha * (p2.Y - p0.Y) / 6f;
                float cp2x = p2.X - alpha * (p3.X - p1.X) / 6f;
                float cp2y = p2.Y - alpha * (p3.Y - p1.Y) / 6f;

                path.CubicTo(cp1x, cp1y, cp2x, cp2y, p2.X, p2.Y);
            }

            if (closed) path.Close();
            return path;
        }

        // ── Rectangles ───────────────────────────────────────────────────────

        public void DrawRectangle(AbstractPen pen, float x, float y, float width, float height)
        {
            ApplyPen(pen);
            _canvas.DrawRect(x, y, width, height, _strokePaint);
        }

        public void DrawRectangle(AbstractPen pen, RectangleF rect)
        {
            ApplyPen(pen);
            _canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), _strokePaint);
        }

        public void DrawRectangle(AbstractBrush brush, float x, float y, float width, float height)
        {
            ApplyBrush(brush);
            _canvas.DrawRect(x, y, width, height, _fillPaint);
        }

        public void DrawRectangle(AbstractBrush brush, RectangleF rect)
        {
            ApplyBrush(brush);
            _canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), _fillPaint);
        }

        // ── Ellipses / Arcs ──────────────────────────────────────────────────

        public void DrawEllipse(AbstractPen pen, float x, float y, float width, float height)
        {
            ApplyPen(pen);
            _canvas.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, _strokePaint);
        }

        public void DrawEllipse(AbstractBrush brush, float x, float y, float width, float height)
        {
            ApplyBrush(brush);
            _canvas.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, _fillPaint);
        }

        public void DrawEllipse(AbstractPen pen, AbstractBrush brush, float x, float y, float width, float height)
        {
            var oval = new SKRect(x, y, x + width, y + height);
            ApplyBrush(brush);
            _canvas.DrawOval(oval, _fillPaint);
            ApplyPen(pen);
            _canvas.DrawOval(oval, _strokePaint);
        }

        public void DrawArc(AbstractPen pen, float x, float y, float width, float height, float startAngle, float sweepAngle)
        {
            ApplyPen(pen);
            using var path = new SKPath();
            path.ArcTo(new SKRect(x, y, x + width, y + height), startAngle, sweepAngle, true);
            _canvas.DrawPath(path, _strokePaint);
        }

        // ── Images ───────────────────────────────────────────────────────────

        public void DrawImage(AbstractImage image, float x, float y, float width, float height)
        {
            var bmp = image.SKBitmap;
            lock (bmp)
            {
                var dest = new SKRect(x, y, x + width, y + height);
                using var paint = new SKPaint { IsAntialias = true };
                _canvas.DrawBitmap(bmp, dest, paint);
            }
        }

        public void DrawImageAlpha(float alpha, AbstractImage image, RectangleF targetRect)
        {
            if (alpha <= 0) return;
            var bmp = image.SKBitmap;
            lock (bmp)
            {
                var dest = new SKRect(targetRect.Left, targetRect.Top, targetRect.Right, targetRect.Bottom);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(alpha, 0f, 1f) * 255))
                };
                _canvas.DrawBitmap(bmp, dest, paint);
            }
        }

        // ── Text ─────────────────────────────────────────────────────────────

        public SizeF MeasureString(string text, AbstractFont font)
        {
            using var skFont = CreateFont(font);
            float width = skFont.MeasureText(text);
            skFont.GetFontMetrics(out var metrics);
            float height = metrics.Descent - metrics.Ascent;
            return new SizeF(width, height);
        }

        public void DrawString(string s, AbstractFont font, AbstractBrush brush, float x, float y, StringAlignment alignment)
        {
            ApplyBrush(brush);
            using var skFont = CreateFont(font);
            using var paint = new SKPaint { Color = _fillPaint.Color, IsAntialias = _fillPaint.IsAntialias };

            skFont.GetFontMetrics(out var metrics);
            float ascent = -metrics.Ascent; // ascent is negative in SkiaSharp

            float textWidth = skFont.MeasureText(s);
            float textHeight = metrics.Descent - metrics.Ascent;

            float drawX = x, drawY = y;

            switch (alignment)
            {
                case StringAlignment.Baseline:
                    drawY = y; // caller passes baseline position
                    break;
                case StringAlignment.Centered:
                    drawX = x - textWidth / 2;
                    drawY = y - textHeight / 2 + ascent;
                    break;
                case StringAlignment.TopLeft:
                    drawY = y + ascent;
                    break;
                case StringAlignment.TopCenter:
                    drawX = x - textWidth / 2;
                    drawY = y + ascent;
                    break;
                case StringAlignment.TopRight:
                    drawX = x - textWidth;
                    drawY = y + ascent;
                    break;
                case StringAlignment.CenterLeft:
                    drawY = y - textHeight / 2 + ascent;
                    break;
            }

            _canvas.DrawText(s, drawX, drawY, skFont, paint);
        }

        // ── State save/restore ───────────────────────────────────────────────

        public AbstractGraphicsState Save()
        {
            int count = _canvas.Save();
            return new CanvasState(this, count);
        }

        public void Restore(AbstractGraphicsState state)
        {
            if (state is CanvasState cs)
                _canvas.RestoreToCount(cs.SaveCount);
        }

        private sealed class CanvasState : AbstractGraphicsState
        {
            public readonly int SaveCount;
            public CanvasState(AbstractGraphics g, int count) : base(g) { SaveCount = count; }
        }

        // ── Disposal ─────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _fillPaint.Dispose();
            _strokePaint.Dispose();
            _disposed = true;
        }
    }
}

#nullable enable
using System;

namespace Maps
{
    // Replaces System.Drawing.Point. Mutable int-pair used as sector/hex coordinates
    // and as Dictionary keys throughout the codebase.
    public struct Point : IEquatable<Point>
    {
        public int X;
        public int Y;

        public Point(int x, int y) { X = x; Y = y; }

        public static readonly Point Empty = new Point(0, 0);

        public bool IsEmpty => X == 0 && Y == 0;

        public bool Equals(Point other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Point other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(Point left, Point right) => left.Equals(right);
        public static bool operator !=(Point left, Point right) => !left.Equals(right);

        public void Offset(int dx, int dy) { X += dx; Y += dy; }
        public void Offset(Point other) { X += other.X; Y += other.Y; }

        // Implicit casts keep handler/admin and rendering code working until issue #5.
        public static implicit operator System.Drawing.Point(Point p)
            => new System.Drawing.Point(p.X, p.Y);
        public static implicit operator Point(System.Drawing.Point p)
            => new Point(p.X, p.Y);
        // PointF implicit: required because C# doesn't chain user-defined conversions.
        public static implicit operator System.Drawing.PointF(Point p)
            => new System.Drawing.PointF(p.X, p.Y);
    }

    // Integer rectangle used for sector bounds.
    // Implicit casts to System.Drawing types keep rendering code working until issue #5.
    public struct Rectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public Rectangle(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public static readonly Rectangle Empty = new Rectangle(0, 0, 0, 0);

        public void Offset(int dx, int dy) { X += dx; Y += dy; }

        public bool Contains(Point p) =>
            p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;

        public static implicit operator System.Drawing.Rectangle(Rectangle r)
            => new System.Drawing.Rectangle(r.X, r.Y, r.Width, r.Height);

        public static implicit operator System.Drawing.RectangleF(Rectangle r)
            => new System.Drawing.RectangleF(r.X, r.Y, r.Width, r.Height);
    }
}

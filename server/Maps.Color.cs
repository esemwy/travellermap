#nullable enable
using System;

namespace Maps
{
    // Stores a color as a CSS/HTML string (e.g. "red", "#E32736") for data model
    // use. Named ColorRef instead of Color to avoid ambiguity with System.Drawing.Color
    // in child namespaces (Maps.Rendering, Maps.Graphics, etc.).
    //
    // Implicit casts to/from System.Drawing.Color keep rendering code working
    // without changes until the SkiaSharp port in issue #5.
    public readonly struct ColorRef : IEquatable<ColorRef>
    {
        public ColorRef(string html)
        {
            // Normalize through ColorTranslator so named colors get their canonical
            // capitalization (e.g. "blue" → "Blue", "#048104" stays "#048104").
            try
            {
                Html = System.Drawing.ColorTranslator.ToHtml(
                    System.Drawing.ColorTranslator.FromHtml(html));
            }
            catch
            {
                Html = html;
            }
        }

        public string Html { get; }

        public static implicit operator System.Drawing.Color(ColorRef c)
            => System.Drawing.ColorTranslator.FromHtml(c.Html);

        public static implicit operator ColorRef(System.Drawing.Color c)
            => new ColorRef(System.Drawing.ColorTranslator.ToHtml(c));

        public bool Equals(ColorRef other) =>
            string.Equals(Html, other.Html, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is ColorRef other && Equals(other);

        public override int GetHashCode() =>
            Html?.ToLowerInvariant().GetHashCode(StringComparison.Ordinal) ?? 0;

        public override string ToString() => Html;

        public static bool operator ==(ColorRef left, ColorRef right) => left.Equals(right);
        public static bool operator !=(ColorRef left, ColorRef right) => !left.Equals(right);
    }
}

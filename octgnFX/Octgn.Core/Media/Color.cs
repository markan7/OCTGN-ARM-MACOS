// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.ComponentModel;
using System.Globalization;

namespace Octgn.Core.Media
{
    /// <summary>
    /// UI-independent ARGB color for the cross-platform build, in place of System.Windows.Media.Color (WPF).
    /// It keeps the parts of the WPF API that OCTGN uses, and the same text form ("#AARRGGBB"), so the JSON
    /// exchanged between clients (for example the game save state) is the same on Windows and elsewhere.
    /// </summary>
    [Serializable]
    [TypeConverter(typeof(ColorTypeConverter))]
    public struct Color : IEquatable<Color>
    {
        public byte A { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public static Color FromArgb(byte a, byte r, byte g, byte b) {
            return new Color { A = a, R = r, G = g, B = b };
        }

        public static Color FromRgb(byte r, byte g, byte b) {
            return FromArgb(255, r, g, b);
        }

        /// <summary>
        /// Parses "#RGB", "#ARGB", "#RRGGBB", "#AARRGGBB" or a color name of <see cref="Colors"/>, like WPF's ColorConverter.
        /// </summary>
        public static Color Parse(string value) {
            if (TryParse(value, out var color)) return color;
            throw new FormatException($"'{value}' is not a valid color.");
        }

        public static bool TryParse(string value, out Color color) {
            color = default(Color);
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();

            if (value[0] != '#')
                return Colors.TryGetNamed(value, out color);

            var hex = value.Substring(1);
            if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
                return false;

            switch (hex.Length) {
                case 3: // RGB
                    color = FromArgb(255, Expand(v >> 8), Expand(v >> 4), Expand(v));
                    return true;
                case 4: // ARGB
                    color = FromArgb(Expand(v >> 12), Expand(v >> 8), Expand(v >> 4), Expand(v));
                    return true;
                case 6: // RRGGBB
                    color = FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                    return true;
                case 8: // AARRGGBB
                    color = FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                    return true;
                default:
                    return false;
            }
        }

        private static byte Expand(uint nibble) {
            nibble &= 0xF;
            return (byte)(nibble << 4 | nibble);
        }

        /// <summary>"#AARRGGBB", as WPF's Color.ToString().</summary>
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", A, R, G, B);
        }

        public bool Equals(Color other) {
            return A == other.A && R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj) {
            return obj is Color other && Equals(other);
        }

        public override int GetHashCode() {
            return A << 24 | R << 16 | G << 8 | B;
        }

        public static bool operator ==(Color left, Color right) {
            return left.Equals(right);
        }

        public static bool operator !=(Color left, Color right) {
            return !left.Equals(right);
        }
    }

    /// <summary>Converts <see cref="Color"/> to and from its "#AARRGGBB" text form (used by Json.NET).</summary>
    public sealed class ColorTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) {
            return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) {
            if (value is string text) return Color.Parse(text);
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) {
            if (destinationType == typeof(string) && value is Color color) return color.ToString();
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

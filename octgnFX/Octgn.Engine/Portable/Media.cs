// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Globalization;

namespace Octgn.Portable
{
    /// <summary>WPF's ColorConverter.ConvertFromString, for <see cref="Octgn.Core.Media.Color"/>.</summary>
    public static class ColorConverter
    {
        /// <summary>A boxed color, or null for null; FormatException if the text is not a color (as WPF).</summary>
        public static object ConvertFromString(string value) {
            if (value == null) return null;
            return Octgn.Core.Media.Color.Parse(value);
        }
    }

    /// <summary>A position on the table, in place of System.Windows.Point.</summary>
    public struct Point : IEquatable<Point>
    {
        public Point(double x, double y) {
            X = x;
            Y = y;
        }

        public double X { get; set; }
        public double Y { get; set; }

        public bool Equals(Point other) {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj) {
            return obj is Point other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(X, Y);
        }

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1}", X, Y);
        }

        public static bool operator ==(Point left, Point right) {
            return left.Equals(right);
        }

        public static bool operator !=(Point left, Point right) {
            return !left.Equals(right);
        }
    }
}

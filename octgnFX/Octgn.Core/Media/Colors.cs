// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;

namespace Octgn.Core.Media
{
    /// <summary>
    /// Named colors, with the values of WPF's System.Windows.Media.Colors (the CSS/X11 colors).
    /// Only the names OCTGN needs are listed; add others from the same table when needed.
    /// </summary>
    public static class Colors
    {
        public static Color Transparent => Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
        public static Color Black => Color.FromRgb(0x00, 0x00, 0x00);
        public static Color White => Color.FromRgb(0xFF, 0xFF, 0xFF);
        public static Color Red => Color.FromRgb(0xFF, 0x00, 0x00);
        public static Color Green => Color.FromRgb(0x00, 0x80, 0x00);
        public static Color Blue => Color.FromRgb(0x00, 0x00, 0xFF);
        public static Color Yellow => Color.FromRgb(0xFF, 0xFF, 0x00);
        public static Color Orange => Color.FromRgb(0xFF, 0xA5, 0x00);
        public static Color Gray => Color.FromRgb(0x80, 0x80, 0x80);
        public static Color DarkGray => Color.FromRgb(0xA9, 0xA9, 0xA9);
        public static Color LightGray => Color.FromRgb(0xD3, 0xD3, 0xD3);
        public static Color DimGray => Color.FromRgb(0x69, 0x69, 0x69);
        public static Color Crimson => Color.FromRgb(0xDC, 0x14, 0x3C);
        public static Color BlueViolet => Color.FromRgb(0x8A, 0x2B, 0xE2);

        private static readonly Dictionary<string, Func<Color>> Named = new Dictionary<string, Func<Color>>(StringComparer.OrdinalIgnoreCase) {
            { nameof(Transparent), () => Transparent },
            { nameof(Black), () => Black },
            { nameof(White), () => White },
            { nameof(Red), () => Red },
            { nameof(Green), () => Green },
            { nameof(Blue), () => Blue },
            { nameof(Yellow), () => Yellow },
            { nameof(Orange), () => Orange },
            { nameof(Gray), () => Gray },
            { nameof(DarkGray), () => DarkGray },
            { nameof(LightGray), () => LightGray },
            { nameof(DimGray), () => DimGray },
            { nameof(Crimson), () => Crimson },
            { nameof(BlueViolet), () => BlueViolet },
        };

        public static bool TryGetNamed(string name, out Color color) {
            if (name != null && Named.TryGetValue(name, out var get)) {
                color = get();
                return true;
            }
            color = default(Color);
            return false;
        }
    }
}

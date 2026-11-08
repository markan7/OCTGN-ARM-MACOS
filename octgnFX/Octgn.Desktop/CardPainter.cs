// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Octgn.Play;
using Card = Octgn.Play.Card;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop
{
    /// <summary>How a card looks wherever it is: picture, rounded corners, highlight, filter, selection, markers.</summary>
    public static class CardPainter
    {
        private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromRgb(0x3d, 0x9b, 0xff));
        private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(0xa0, 0xff, 0xff, 0xff));
        private static readonly IBrush MarkerCountBack = new SolidColorBrush(Color.FromArgb(0xd0, 0x10, 0x10, 0x10));
        private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0));
        private static readonly Typeface Font = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        /// <summary>Whether the local player sees the face of the card (the WPF CardControl's IsUp).</summary>
        public static bool IsUp(Card card, bool alwaysUp = false) {
            return alwaysUp || card.FaceUp || card.PeekingPlayers.Contains(Player.LocalPlayer);
        }

        /// <summary>
        /// Draws <paramref name="card"/> in <paramref name="rect"/> (its upright frame), turned by its orientation
        /// around the frame's center when <paramref name="rotate"/> (the table). <paramref name="scale"/>: screen
        /// pixels per card unit, for line widths and text.
        /// </summary>
        public static void Draw(DrawingContext dc, Card card, Rect rect, double scale, bool rotate, bool hovered, bool alwaysUp = false, double opacity = 1) {
            var up = IsUp(card, alwaysUp);
            var angle = rotate ? (int)card.Orientation * 90.0 : 0;
            using (dc.PushTransform(RotationAround(rect.Center, angle)))
            using (dc.PushOpacity(opacity)) {
                var size = card.Size;
                var radius = (up ? size.CornerRadius : size.BackCornerRadius) * rect.Width / Math.Max(1, up ? size.Width : size.BackWidth);
                var frame = new RoundedRect(rect, radius);
                dc.DrawRectangle(Shadow, null, new RoundedRect(rect.Translate(new Vector(1.5, 2)), radius));
                var picture = Pictures.ForCard(card, up, false, out var generic);
                using (dc.PushClip(frame)) {
                    if (picture != null) dc.DrawImage(picture, new Rect(picture.Size), rect);
                    else dc.FillRectangle(Brushes.DimGray, rect);
                    // A card without a picture of its own: its name over the generic front.
                    if (generic || picture == null) DrawName(dc, card.RealName, rect);
                    if (card.FilterColor is Core.Media.Color filter)
                        dc.FillRectangle(new SolidColorBrush(ToAvalonia(filter)), rect);
                }
                if (card.HighlightColor is Core.Media.Color highlight)
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(ToAvalonia(highlight)), Math.Max(2.5, 3 * scale)), frame.Inflate(1, 1));
                if (card.Selected)
                    dc.DrawRectangle(null, new Pen(SelectionBrush, Math.Max(2, 2.5 * scale)), frame.Inflate(2, 2));
                else if (hovered)
                    dc.DrawRectangle(null, new Pen(HoverBrush, 1.5), frame);
                if (card.TargetedBy != null)
                    DrawTarget(dc, rect, card.TargetedBy, scale);
                if (card.Markers.Count > 0)
                    DrawMarkers(dc, card, rect, scale);
            }
        }

        private static readonly IBrush NameBand = new SolidColorBrush(Color.FromArgb(0xd8, 0x18, 0x18, 0x18));

        private static void DrawName(DrawingContext dc, string name, Rect rect) {
            if (string.IsNullOrEmpty(name) || rect.Width < 24) return;
            var size = Math.Max(6, rect.Width * 0.11);
            var text = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, Brushes.White) {
                MaxTextWidth = rect.Width * 0.9,
                MaxLineCount = 3,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var band = new Rect(rect.X, rect.Y + rect.Height * 0.36, rect.Width, text.Height + size * 0.6);
            dc.FillRectangle(NameBand, band);
            dc.DrawText(text, new Point(rect.X + rect.Width * 0.05, band.Y + size * 0.3));
        }

        public static Matrix RotationAround(Point center, double degrees) {
            if (degrees == 0) return Matrix.Identity;
            return Matrix.CreateTranslation(-center.X, -center.Y)
                   * Matrix.CreateRotation(degrees * Math.PI / 180)
                   * Matrix.CreateTranslation(center.X, center.Y);
        }

        private static void DrawTarget(DrawingContext dc, Rect rect, Player by, double scale) {
            var color = ToAvalonia(by.Color);
            var r = Math.Min(rect.Width, rect.Height) * 0.18;
            var c = rect.Center;
            var pen = new Pen(new SolidColorBrush(color), Math.Max(2, r * 0.18));
            dc.DrawEllipse(null, pen, c, r, r);
            dc.DrawEllipse(null, pen, c, r * 0.45, r * 0.45);
        }

        /// <summary>The markers in a column at the left of the card, each with its count (as in WPF).</summary>
        private static void DrawMarkers(DrawingContext dc, Card card, Rect rect, double scale) {
            var side = Math.Max(10, Math.Min(rect.Width * 0.26, 28 * Math.Max(scale, 0.6)));
            var x = rect.X + rect.Width * 0.05;
            var y = rect.Y + rect.Height * 0.08;
            foreach (var marker in card.Markers.ToList()) {
                var picture = Pictures.Get(marker.Model?.Source, 64);
                var box = new Rect(x, y, side, side);
                if (picture != null) dc.DrawImage(picture, new Rect(picture.Size), box);
                else dc.DrawEllipse(Brushes.Gold, new Pen(Brushes.Black, 1), box.Center, side / 2, side / 2);
                if (marker.Count != 1 || (marker.Model != null && marker.Model.ShowCount)) {
                    var text = new FormattedText(marker.Count.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, Font, side * 0.55, Brushes.White);
                    var back = new Rect(box.Right - text.Width * 0.6, box.Bottom - text.Height * 0.8, text.Width + 4, text.Height);
                    dc.DrawRectangle(MarkerCountBack, null, new RoundedRect(back, 3));
                    dc.DrawText(text, new Point(back.X + 2, back.Y));
                }
                y += side * 1.08;
                if (y + side > rect.Bottom) {
                    y = rect.Y + rect.Height * 0.08;
                    x += side * 1.08;
                }
            }
        }

        public static Color ToAvalonia(Core.Media.Color color) {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        /// <summary>A player's color for text on the dark theme: too dark a color (the local player's black) is light gray.</summary>
        public static IBrush TextBrush(Core.Media.Color color) {
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
            return color.A == 0 || luminance < 0.3 ? Brushes.WhiteSmoke : new SolidColorBrush(ToAvalonia(color));
        }
    }
}

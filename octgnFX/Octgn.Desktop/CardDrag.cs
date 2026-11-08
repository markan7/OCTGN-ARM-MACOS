// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Card = Octgn.Play.Card;

namespace Octgn.Desktop
{
    /// <summary>A view cards can be dropped on (the table, a pile, the hand).</summary>
    public interface ICardDropTarget
    {
        /// <summary>Drops the cards at a point of this view.</summary>
        void Drop(CardDrag drag, Point point);
    }

    /// <summary>Cards being dragged: the one under the pointer and the others moving with it (the selection).</summary>
    public sealed class CardDrag
    {
        public CardDrag(Card clicked, IReadOnlyList<Card> cards, Vector grab, bool faceUp, double scale) {
            Clicked = clicked;
            Cards = cards;
            Grab = grab;
            FaceUp = faceUp;
            Scale = scale;
        }

        public Card Clicked { get; }

        public IReadOnlyList<Card> Cards { get; }

        /// <summary>Where the pointer holds the clicked card, in card units from its top left corner.</summary>
        public Vector Grab { get; }

        /// <summary>Whether the cards land face up (Shift held at the start: face down), as in WPF.</summary>
        public bool FaceUp { get; }

        /// <summary>Screen pixels per card unit where the drag started.</summary>
        public double Scale { get; }
    }

    /// <summary>
    /// The layer above the window that shows the dragged cards and finds where they are dropped. The view a
    /// drag starts from keeps the pointer and reports its moves (<see cref="Move"/>, <see cref="Drop"/>).
    /// </summary>
    public sealed class DragLayer : Control
    {
        private readonly List<Control> _targets = new List<Control>();
        private Point _position;
        private Control _hoverTarget;

        public DragLayer() {
            IsHitTestVisible = false;
        }

        public CardDrag Current { get; private set; }

        public void Register(Control target) {
            if (target is ICardDropTarget && !_targets.Contains(target)) _targets.Add(target);
        }

        public void Unregister(Control target) => _targets.Remove(target);

        public void Start(CardDrag drag, Point position) {
            Current = drag;
            _position = position;
            InvalidateVisual();
        }

        public void Move(Point position) {
            if (Current == null) return;
            _position = position;
            _hoverTarget = TargetAt(position, out _);
            InvalidateVisual();
        }

        /// <summary>Drops the cards on the view under <paramref name="position"/> (of this layer); false if there is none.</summary>
        public bool Drop(Point position) {
            var drag = Current;
            Current = null;
            _hoverTarget = null;
            InvalidateVisual();
            if (drag == null) return false;
            var target = TargetAt(position, out var local);
            if (target == null) return false;
            ((ICardDropTarget)target).Drop(drag, local);
            return true;
        }

        public void Cancel() {
            Current = null;
            _hoverTarget = null;
            InvalidateVisual();
        }

        /// <summary>The drop target under a point: the view the pointer is over (hit testing skips what scrolled out of sight).</summary>
        private Control TargetAt(Point position, out Point local) {
            local = default;
            if (!(TopLevel.GetTopLevel(this) is TopLevel root)) return null;
            var windowPoint = this.TranslatePoint(position, root);
            if (windowPoint == null) return null;
            var hit = root.InputHitTest(windowPoint.Value) as Visual;
            for (var visual = hit; visual != null; visual = visual.GetVisualParent()) {
                if (visual is Control control && _targets.Contains(control) && control.IsEffectivelyVisible) {
                    local = this.TranslatePoint(position, control) ?? default;
                    return control;
                }
            }
            return null;
        }

        public override void Render(DrawingContext context) {
            var drag = Current;
            if (drag == null) return;
            if (_hoverTarget != null && this.TranslatePoint(new Point(0, 0), _hoverTarget) is Point origin) {
                var bounds = new Rect(-origin.X, -origin.Y, _hoverTarget.Bounds.Width, _hoverTarget.Bounds.Height);
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x3d, 0x9b, 0xff)), 2), bounds.Deflate(1));
            }
            var scale = drag.Scale;
            var x = _position.X - drag.Grab.X * scale;
            var y = _position.Y - drag.Grab.Y * scale;
            foreach (var card in drag.Cards.Where(c => c != drag.Clicked).Take(6).Reverse())
                DrawCard(context, card, ref x, y, scale, offset: true);
            var start = _position.X - drag.Grab.X * scale;
            DrawCard(context, drag.Clicked, ref start, y, scale, offset: false);
            if (drag.Cards.Count > 1) {
                var text = new FormattedText(drag.Cards.Count.ToString(), System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.White);
                var badge = new Rect(_position.X + 10, _position.Y + 10, text.Width + 12, text.Height + 4);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3d, 0x9b, 0xff)), null, new RoundedRect(badge, 8));
                context.DrawText(text, new Point(badge.X + 6, badge.Y + 2));
            }
        }

        private static void DrawCard(DrawingContext context, Card card, ref double x, double y, double scale, bool offset) {
            var width = card.RealWidth * scale;
            var height = card.RealHeight * scale;
            if (offset) x += width * 0.12;
            CardPainter.Draw(context, card, new Rect(x, y, width, height), scale, rotate: false, hovered: false, opacity: 0.8);
        }
    }
}

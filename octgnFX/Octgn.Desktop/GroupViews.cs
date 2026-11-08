// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Octgn.Core.DataExtensionMethods;
using Octgn.Play;
using Octgn.Play.Gui;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;

namespace Octgn.Desktop
{
    /// <summary>
    /// A group of a player in the player area: the cards it shows, the pointer (drag the cards out, double
    /// click, the game's actions on right click), and the drops on it.
    /// </summary>
    public abstract class GroupView : Control, ICardDropTarget
    {
        private const double DragThreshold = 4;
        protected static readonly Typeface LabelFont = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private readonly HashSet<Card> _watched = new HashSet<Card>();
        private readonly DragLayer _dragLayer;
        private Point _pressPoint;
        private Card _pressedCard;
        private Card _hovered;

        protected GroupView(Group group, DragLayer dragLayer) {
            Group = group;
            _dragLayer = dragLayer;
            ClipToBounds = false;
            group.Cards.CollectionChanged += CardsChanged;
            group.PropertyChanged += GroupChanged;
            foreach (var card in group.Cards) Watch(card);
        }

        public Group Group { get; }

        public Card Hovered => _hovered;

        public event Action<Card> HoveredChanged;

        /// <summary>Screen pixels per card unit.</summary>
        protected abstract double CardScale { get; }

        /// <summary>The card at a point of the view, or null.</summary>
        protected abstract Card CardAt(Point point);

        /// <summary>Where a card of the group is drawn (only for the cards the view shows).</summary>
        protected abstract Rect CardRect(Card card);

        /// <summary>Where a card of the group is drawn in this view, or null if the view does not show it.</summary>
        public Rect? BoundsOf(Card card) {
            if (!Group.Cards.Contains(card)) return null;
            var rect = CardRect(card);
            return CardAt(rect.Center) == card || CardAt(new Point(rect.X + 2, rect.Y + rect.Height / 2)) == card ? rect : (Rect?)null;
        }

        /// <summary>The index of the group a card dropped at <paramref name="point"/> takes.</summary>
        protected abstract int DropIndex(Point point);

        public void Detach() {
            Group.Cards.CollectionChanged -= CardsChanged;
            Group.PropertyChanged -= GroupChanged;
            foreach (var card in _watched.ToList()) Unwatch(card);
        }

        private void Watch(Card card) {
            if (_watched.Add(card)) card.PropertyChanged += CardChanged;
        }

        private void Unwatch(Card card) {
            if (_watched.Remove(card)) card.PropertyChanged -= CardChanged;
        }

        private void CardsChanged(object sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action == NotifyCollectionChangedAction.Reset) {
                foreach (var card in _watched.ToList()) Unwatch(card);
                foreach (var card in Group.Cards) Watch(card);
            } else {
                if (e.OldItems != null) foreach (Card card in e.OldItems) Unwatch(card);
                if (e.NewItems != null) foreach (Card card in e.NewItems) Watch(card);
            }
            if (_hovered != null && !Group.Cards.Contains(_hovered)) SetHovered(null);
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void CardChanged(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

        private void GroupChanged(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

        protected bool IsHovered(Card card) => card == _hovered;

        private void SetHovered(Card card) {
            if (card == _hovered) return;
            _hovered = card;
            HoveredChanged?.Invoke(card);
            InvalidateVisual();
        }

        protected void DrawLabel(DrawingContext context, Rect area) {
            var text = new FormattedText($"{Group.Name} ({Group.Count.ToString(CultureInfo.CurrentCulture)})", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelFont, 12, Brushes.WhiteSmoke) { MaxTextWidth = Math.Max(10, area.Width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(text, new Point(area.X + (area.Width - text.Width) / 2, area.Y));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            _pressPoint = point.Position;
            var card = CardAt(point.Position);
            if (Gestures.IsContextClick(point, e.KeyModifiers)) {
                var items = card != null ? GameActions.CardMenu(card, null) : GameActions.GroupMenu(Group, null);
                if (card != null && items.Count > 0) items.Add(new Separator());
                if (card != null) items.AddRange(GameActions.GroupMenu(Group, null));
                Gestures.OpenMenu(this, items);
                e.Handled = true;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) {
                if (card != null && Group.Def.CardActions.OfType<DataNew.Entities.GroupAction>().Any(a => a.DefaultAction))
                    GameActions.DefaultCardAction(card);
                else
                    GameActions.DefaultGroupAction(Group, null);
                e.Handled = true;
                return;
            }
            if (card != null) {
                if (Gestures.TogglesSelection(e.KeyModifiers)) {
                    if (card.Selected) Selection.Remove(card); else Selection.Add(card);
                } else if (!card.Selected) {
                    Selection.Clear();
                }
                _pressedCard = card;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            var position = e.GetPosition(this);
            if (_dragLayer.Current != null && e.Pointer.Captured == this) {
                _dragLayer.Move(e.GetPosition(_dragLayer));
                return;
            }
            if (_pressedCard != null) {
                var d = position - _pressPoint;
                if (Math.Abs(d.X) + Math.Abs(d.Y) > DragThreshold) StartDrag(e);
                return;
            }
            SetHovered(CardAt(position));
        }

        private void StartDrag(PointerEventArgs e) {
            var card = _pressedCard;
            _pressedCard = null;
            if (!card.TryToManipulate()) return;
            var cards = card.Selected ? Selection.Cards.Where(c => c.Group == Group).ToList() : new List<Card> { card };
            var rect = CardRect(card);
            var scale = CardScale;
            var grab = new Vector((_pressPoint.X - rect.X) / scale, (_pressPoint.Y - rect.Y) / scale);
            var faceUp = (e.KeyModifiers & KeyModifiers.Shift) == 0;
            _dragLayer.Start(new CardDrag(card, cards, grab, faceUp, scale), e.GetPosition(_dragLayer));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (_dragLayer.Current != null && e.Pointer.Captured == this) {
                _dragLayer.Drop(e.GetPosition(_dragLayer));
            } else if (_pressedCard != null && !Gestures.TogglesSelection(e.KeyModifiers)) {
                Selection.Clear();
            }
            _pressedCard = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            if (_dragLayer.Current != null) _dragLayer.Cancel();
            _pressedCard = null;
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            if (e.Pointer.Captured != this) SetHovered(null);
        }

        /// <summary>Moves the cards to the drop place, one after the other (PileControl.OnCardDropped in WPF).</summary>
        public void Drop(CardDrag drag, Point point) {
            if (!Group.TryToManipulate()) return;
            var idx = DropIndex(point);
            foreach (var c in drag.Cards.ToList()) {
                // A card of this group taken from before the drop place: the places after it move up.
                if (c.Group == Group && c.GetIndex() < idx) --idx;
                var doNotIncrement = c.Group == Group && Group.GetCardIndex(c) < idx;
                c.MoveTo(Group, drag.FaceUp, idx, false);
                // Some cards (copies, tokens) are deleted when they change group.
                if (c.Group != Group) doNotIncrement = true;
                if (!doNotIncrement) idx++;
            }
        }
    }

    /// <summary>A pile shown as a stack (view states Pile and Collapsed): its top card, its name and count.</summary>
    public sealed class PileView : GroupView
    {
        private readonly bool _collapsed;

        public PileView(Pile pile, DragLayer dragLayer, bool collapsed) : base(pile, dragLayer) {
            _collapsed = collapsed;
            var size = Program.GameEngine.Definition.DefaultSize();
            var height = collapsed ? 72.0 : 120.0;
            Height = height + 22;
            Width = Math.Max(70, height * size.Width / Math.Max(1, size.Height) + 16);
        }

        private Rect Slot {
            get {
                var size = Program.GameEngine.Definition.DefaultSize();
                var height = Bounds.Height - 22;
                var width = height * size.Width / Math.Max(1, size.Height);
                return new Rect((Bounds.Width - width) / 2, 18, width, height);
            }
        }

        protected override double CardScale => Slot.Height / Math.Max(1, Program.GameEngine.Definition.DefaultSize().Height);

        protected override Card CardAt(Point point) => Group.Count > 0 && Slot.Contains(point) ? Group.Cards[0] : null;

        protected override Rect CardRect(Card card) {
            var slot = Slot;
            var scale = CardScale;
            return new Rect(slot.X, slot.Y, card.RealWidth * scale, card.RealHeight * scale);
        }

        protected override int DropIndex(Point point) => 0;

        public override void Render(DrawingContext context) {
            var slot = Slot;
            DrawLabel(context, new Rect(0, 0, Bounds.Width, 16));
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0xff, 0xff, 0xff)),
                new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xff, 0xff, 0xff)), 1, new DashStyle(new double[] { 3, 3 }, 0)),
                new RoundedRect(slot, 4));
            if (Group.Count == 0) {
                var icon = Pictures.Get(Group.Def.Icon, 96);
                if (icon != null) {
                    var side = Math.Min(slot.Width, slot.Height) * 0.5;
                    context.DrawImage(icon, new Rect(icon.Size), new Rect(slot.Center.X - side / 2, slot.Center.Y - side / 2, side, side));
                }
                return;
            }
            // A few cards under the top one give the pile some depth.
            var depth = Math.Min(3, Group.Count - 1);
            for (var i = depth; i >= 1; i--) {
                var r = slot.Translate(new Vector(i * 1.5, -i * 1.5));
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x2a)), new Pen(Brushes.Black, 0.5), new RoundedRect(r, 4));
            }
            var top = Group.Cards[0];
            CardPainter.Draw(context, top, CardRect(top), CardScale, rotate: false, hovered: IsHovered(top));
        }
    }

    /// <summary>A pile shown spread out (view state Expanded: the hand of most games), cards overlapping when needed.</summary>
    public sealed class FanView : GroupView
    {
        private const double Inset = 6;

        public FanView(Pile pile, DragLayer dragLayer) : base(pile, dragLayer) {
            Height = 142;
            MinWidth = 160;
        }

        protected override double CardScale => (Bounds.Height - 22 - Inset) / Math.Max(1, Program.GameEngine.Definition.DefaultSize().Height);

        /// <summary>The distance between the left sides of two neighbor cards.</summary>
        private double Step {
            get {
                var count = Group.Count;
                if (count <= 1) return 0;
                var cardWidth = Program.GameEngine.Definition.DefaultSize().Width * CardScale;
                var available = Bounds.Width - 2 * Inset - cardWidth;
                return Math.Max(4, Math.Min(cardWidth * 1.04, available / (count - 1)));
            }
        }

        protected override Rect CardRect(Card card) {
            var index = Group.Cards.IndexOf(card);
            var scale = CardScale;
            return new Rect(Inset + index * Step, 18, card.RealWidth * scale, card.RealHeight * scale);
        }

        protected override Card CardAt(Point point) {
            // The last card drawn lies on top.
            for (var i = Group.Count - 1; i >= 0; i--) {
                var card = Group.Cards[i];
                if (CardRect(card).Contains(point)) return card;
            }
            return null;
        }

        protected override int DropIndex(Point point) {
            if (Group.Count == 0) return 0;
            var step = Step;
            if (step <= 0) return point.X < Bounds.Width / 2 ? 0 : Group.Count;
            var cardWidth = Program.GameEngine.Definition.DefaultSize().Width * CardScale;
            var index = (int)Math.Round((point.X - Inset - cardWidth / 2 + step / 2) / step);
            return Math.Clamp(index, 0, Group.Count);
        }

        public override void Render(DrawingContext context) {
            DrawLabel(context, new Rect(0, 0, Bounds.Width, 16));
            var area = new Rect(0, 16, Bounds.Width, Bounds.Height - 16);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x20, 0xff, 0xff, 0xff)), null, new RoundedRect(area, 6));
            var scale = CardScale;
            foreach (var card in Group.Cards.ToList()) {
                var rect = CardRect(card);
                if (IsHovered(card)) rect = rect.Translate(new Vector(0, -4));
                CardPainter.Draw(context, card, rect, scale, rotate: false, hovered: IsHovered(card));
            }
        }
    }
}

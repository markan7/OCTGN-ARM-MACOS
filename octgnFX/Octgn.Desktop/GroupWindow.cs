// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Octgn.Play;
using Octgn.Play.Gui;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;

namespace Octgn.Desktop
{
    /// <summary>
    /// The cards of a pile, face up for the local player (GroupWindow in WPF): all of them, or the top or
    /// bottom ones. The other players see that the pile is looked at (LookAtReq). Right click: the actions.
    /// </summary>
    public sealed class GroupWindow : Window
    {
        private readonly Group _group;
        private readonly PilePosition _position;
        private readonly int _count;
        private readonly int _id;
        private readonly HashSet<Card> _shown;
        private readonly WrapPanel _cards = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        private readonly TextBox _filter = new TextBox { PlaceholderText = "Filter", Width = 200 };
        private bool _notified;
        private bool _shuffleOnClose;

        /// <summary>A pile window opens (tests).</summary>
        public static event Action<GroupWindow> Opening;

        public GroupWindow(Group group, PilePosition position, int count, DragLayer dragLayer) {
            Opening?.Invoke(this);
            _group = group;
            _position = position;
            _count = count;
            _id = Program.GameEngine.GetUniqueId();
            switch (position) {
                case PilePosition.Top:
                    Title = $"{group.FullName}: top {count} cards";
                    _shown = new HashSet<Card>(group.Take(count));
                    break;
                case PilePosition.Bottom:
                    Title = $"{group.FullName}: bottom {count} cards";
                    _shown = new HashSet<Card>(group.Skip(Math.Max(0, group.Count - count)));
                    break;
                default:
                    Title = group.FullName;
                    _shown = null;
                    break;
            }
            Width = 760;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var shuffle = new Button { Content = "Close and shuffle", IsVisible = position == PilePosition.All && group is Pile };
            shuffle.Click += (s, e) => { _shuffleOnClose = true; Close(); };
            var close = new Button { Content = "Close", IsDefault = true };
            close.Click += (s, e) => Close();
            var bar = new DockPanel { Margin = new Thickness(8), LastChildFill = false };
            bar.Children.Add(_filter);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            right.Children.Add(shuffle);
            right.Children.Add(close);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Bottom);
            root.Children.Add(bar);
            root.Children.Add(new ScrollViewer { Content = _cards });
            Content = root;

            _filter.TextChanged += (s, e) => Refresh();
            group.Cards.CollectionChanged += CardsChanged;
            Refresh();

            if (group.Visibility != DataNew.Entities.GroupVisibility.Everybody) SendLookAt(true);
        }

        private void CardsChanged(object sender, NotifyCollectionChangedEventArgs e) => Refresh();

        private void Refresh() {
            var filter = _filter.Text ?? "";
            var cards = _group.Cards.Where(c => _shown == null || _shown.Contains(c))
                .Where(c => filter.Length == 0 || c.RealName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();
            _cards.Children.Clear();
            foreach (var card in cards) _cards.Children.Add(new CardTile(card));
        }

        private void SendLookAt(bool look) {
            _notified = look;
            switch (_position) {
                case PilePosition.All:
                    Program.Client.Rpc.LookAtReq(_id, _group, look);
                    break;
                case PilePosition.Top:
                    Program.Client.Rpc.LookAtTopReq(_id, _group, _count, look);
                    break;
                case PilePosition.Bottom:
                    Program.Client.Rpc.LookAtBottomReq(_id, _group, _count, look);
                    break;
            }
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            _group.Cards.CollectionChanged -= CardsChanged;
            if (Program.GameEngine == null) return;
            if (_notified) SendLookAt(false);
            if (_shuffleOnClose && _group is Pile pile) pile.Shuffle();
        }

        /// <summary>A card of the list, face up: double click for the default action, right click for the others.</summary>
        private sealed class CardTile : Control
        {
            private readonly Card _card;
            private bool _hover;

            public CardTile(Card card) {
                _card = card;
                var size = card.Size;
                Height = 150;
                Width = 150.0 * size.Width / Math.Max(1, size.Height);
                Margin = new Thickness(4);
                card.PropertyChanged += Changed;
                ToolTip.SetTip(this, card.RealName);
            }

            private void Changed(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

            protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
                base.OnDetachedFromVisualTree(e);
                _card.PropertyChanged -= Changed;
            }

            public override void Render(DrawingContext context) {
                CardPainter.Draw(context, _card, new Rect(Bounds.Size), Bounds.Height / Math.Max(1, _card.Size.Height), rotate: false, hovered: _hover, alwaysUp: true);
            }

            protected override void OnPointerEntered(PointerEventArgs e) {
                base.OnPointerEntered(e);
                _hover = true;
                InvalidateVisual();
            }

            protected override void OnPointerExited(PointerEventArgs e) {
                base.OnPointerExited(e);
                _hover = false;
                InvalidateVisual();
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e) {
                base.OnPointerPressed(e);
                var point = e.GetCurrentPoint(this);
                if (Gestures.IsContextClick(point, e.KeyModifiers)) {
                    Selection.Clear();
                    Gestures.OpenMenu(this, GameActions.CardMenu(_card, null));
                } else if (e.ClickCount == 2) {
                    GameActions.DefaultCardAction(_card);
                }
                e.Handled = true;
            }
        }
    }
}

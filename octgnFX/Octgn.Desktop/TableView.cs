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
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Octgn.DataNew.Entities;
using Octgn.Play;
using Octgn.Play.Gui;
using Card = Octgn.Play.Card;
using EnginePoint = Octgn.Portable.Point;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop
{
    /// <summary>
    /// The game table (TableControl in WPF): the cards of the table at their positions, the background and the
    /// game board, with zoom and pan. Table coordinates are the engine's: (0, 0) is the center of the table of
    /// the definition, whose width and height fill the view at the start.
    /// <para>
    /// Mouse: drag a card to move it (with the selection), Shift+drag empty space to select, drag empty space
    /// or scroll to pan, ⌘/Ctrl+scroll or pinch to zoom, double click for the default action, right click for
    /// the game's actions.
    /// </para>
    /// </summary>
    public sealed class TableView : Control, ICardDropTarget
    {
        private const double DragThreshold = 4;
        private readonly Table _table;
        private readonly DragLayer _dragLayer;
        private readonly HashSet<Card> _watched = new HashSet<Card>();

        private double _zoom = 1;
        private Vector _pan;
        private bool _fitted;

        // Pointer state
        private Point _pressPoint;
        private Card _pressedCard;
        private bool _panning;
        private bool _selecting;
        private Rect _selectionRect;
        private Vector _panAtPress;
        private Card _hovered;
        private double _lastPinch = 1;

        public TableView(Table table, DragLayer dragLayer) {
            _table = table;
            _dragLayer = dragLayer;
            ClipToBounds = true;
            Focusable = true;
            _table.Cards.CollectionChanged += CardsChanged;
            foreach (var card in _table.Cards) Watch(card);
            Program.GameEngine.PropertyChanged += EngineChanged;
            Pinch += OnPinch;
            PinchEnded += (s, e) => _lastPinch = 1;
            PointerTouchPadGestureMagnify += OnTouchPadMagnify;
        }

        /// <summary>The card under the pointer, for the preview and the shortcuts (null if none).</summary>
        public Card Hovered => _hovered;

        public event Action<Card> HoveredChanged;

        public double Zoom => _zoom;

        /// <summary>The table point under the pointer, for the actions run at a position.</summary>
        public EnginePoint PointerTablePosition { get; private set; }

        private void Watch(Card card) {
            if (_watched.Add(card)) card.PropertyChanged += CardChanged;
        }

        private void Unwatch(Card card) {
            if (_watched.Remove(card)) card.PropertyChanged -= CardChanged;
        }

        private void CardsChanged(object sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action == NotifyCollectionChangedAction.Reset) {
                foreach (var card in _watched.ToList()) Unwatch(card);
                foreach (var card in _table.Cards) Watch(card);
            } else {
                if (e.OldItems != null) foreach (Card card in e.OldItems) Unwatch(card);
                if (e.NewItems != null) foreach (Card card in e.NewItems) Watch(card);
            }
            if (_hovered != null && !_table.Cards.Contains(_hovered)) SetHovered(null);
            InvalidateVisual();
        }

        private void CardChanged(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

        private void EngineChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == "BoardImage" || e.PropertyName == "GameBoard") InvalidateVisual();
        }

        public void Detach() {
            _table.Cards.CollectionChanged -= CardsChanged;
            foreach (var card in _watched.ToList()) Unwatch(card);
            if (Program.GameEngine != null) Program.GameEngine.PropertyChanged -= EngineChanged;
        }

        // Coordinates

        public Point ToScreen(double x, double y) => new Point(x * _zoom + _pan.X, y * _zoom + _pan.Y);

        public Point ToTable(Point screen) => new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

        /// <summary>Zoom and pan so that the table of the definition fills the view (CenterView in WPF).</summary>
        public void FitView() {
            var def = _table.Definition;
            var width = Math.Max(1, def.Width);
            var height = Math.Max(1, def.Height);
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            _zoom = Math.Min(Bounds.Width / width, Bounds.Height / height);
            _pan = new Vector(Bounds.Width / 2, Bounds.Height / 2);
            _fitted = true;
            InvalidateVisual();
        }

        public void ZoomBy(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

        private void ZoomAt(Point screen, double factor) {
            var zoom = Math.Clamp(_zoom * factor, 0.1, 8);
            var table = ToTable(screen);
            _zoom = zoom;
            _pan = new Vector(screen.X - table.X * zoom, screen.Y - table.Y * zoom);
            InvalidateVisual();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e) {
            base.OnSizeChanged(e);
            if (!_fitted) FitView();
        }

        // Drawing

        private static Rect CardFrame(Card card) => new Rect(card.X, card.Y, card.RealWidth, card.RealHeight);

        public override void Render(DrawingContext context) {
            var bounds = new Rect(Bounds.Size);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x1c, 0x2a, 0x22)), bounds);
            var def = _table.Definition;
            if (!string.IsNullOrEmpty(def.Background)) {
                var background = Pictures.Get(def.Background, 2560);
                if (background != null) DrawBackground(context, background, def.BackgroundStyle, bounds);
            }

            using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y))) {
                // The area of the table of the definition, faintly.
                var area = new Rect(-def.Width / 2.0, -def.Height / 2.0, def.Width, def.Height);
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xff, 0xff, 0xff)), 1 / _zoom, new DashStyle(new double[] { 4, 4 }, 0)), area);

                var engine = Program.GameEngine;
                if (engine?.GameBoard != null && engine.BoardImage != null && Program.GameSettings.ShowBoard) {
                    var board = Pictures.Get(engine.BoardImage, 2560);
                    var b = engine.GameBoard;
                    if (board != null) context.DrawImage(board, new Rect(board.Size), new Rect(b.XPos, b.YPos, b.Width, b.Height));
                }

                foreach (var card in _table.Cards.ToList())
                    CardPainter.Draw(context, card, CardFrame(card), _zoom, rotate: true, hovered: card == _hovered);
            }

            if (_selecting) {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0x3d, 0x9b, 0xff)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x3d, 0x9b, 0xff)), 1), _selectionRect);
            }
        }

        private static void DrawBackground(DrawingContext context, Bitmap image, BackgroundStyle style, Rect bounds) {
            var size = image.Size;
            switch (style) {
                case BackgroundStyle.Tile:
                    for (var x = 0.0; x < bounds.Width; x += size.Width)
                        for (var y = 0.0; y < bounds.Height; y += size.Height)
                            context.DrawImage(image, new Rect(x, y, size.Width, size.Height));
                    break;
                case BackgroundStyle.Stretch:
                    context.DrawImage(image, new Rect(size), bounds);
                    break;
                case BackgroundStyle.Uniform: {
                        var scale = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
                        var w = size.Width * scale;
                        var h = size.Height * scale;
                        context.DrawImage(image, new Rect(size), new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h));
                        break;
                    }
                default: {
                        // UniformToFill: cover the view, centered, cropped.
                        var scale = Math.Max(bounds.Width / size.Width, bounds.Height / size.Height);
                        var w = bounds.Width / scale;
                        var h = bounds.Height / scale;
                        context.DrawImage(image, new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h), bounds);
                        break;
                    }
            }
        }

        // Hit testing

        /// <summary>The topmost card at a point of the view (the last of the table's cards is drawn last).</summary>
        public Card CardAt(Point screen) {
            var p = ToTable(screen);
            for (var i = _table.Cards.Count - 1; i >= 0; i--) {
                var card = _table.Cards[i];
                var frame = CardFrame(card);
                // Undo the card's rotation around its center.
                var angle = -(int)card.Orientation * 90.0;
                var local = CardPainter.RotationAround(frame.Center, angle).Transform(p);
                if (frame.Contains(local)) return card;
            }
            return null;
        }

        private void SetHovered(Card card) {
            if (card == _hovered) return;
            _hovered = card;
            HoveredChanged?.Invoke(card);
            InvalidateVisual();
        }

        // Input

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            Focus();
            var point = e.GetCurrentPoint(this);
            _pressPoint = point.Position;
            var card = CardAt(point.Position);
            UpdatePointerPosition(point.Position);

            if (Gestures.IsContextClick(point, e.KeyModifiers)) {
                ShowMenu(card, point.Position);
                e.Handled = true;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) {
                // Middle button: pan.
                _panning = true;
                _panAtPress = _pan;
                e.Pointer.Capture(this);
                return;
            }

            if (card != null) {
                if (e.ClickCount == 2) {
                    GameActions.DefaultCardAction(card);
                    e.Handled = true;
                    return;
                }
                if (Gestures.TogglesSelection(e.KeyModifiers)) {
                    if (card.Selected) Selection.Remove(card); else Selection.Add(card);
                } else if (!card.Selected) {
                    Selection.Clear();
                }
                _pressedCard = card;
            } else {
                if (e.ClickCount == 2) {
                    GameActions.DefaultGroupAction(_table, PointerTablePosition);
                    e.Handled = true;
                    return;
                }
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0) {
                    _selecting = true;
                    _selectionRect = new Rect(_pressPoint, _pressPoint);
                } else {
                    Selection.Clear();
                    _panning = true;
                    _panAtPress = _pan;
                }
            }
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            var position = e.GetPosition(this);
            UpdatePointerPosition(position);

            if (_dragLayer.Current != null && e.Pointer.Captured == this) {
                _dragLayer.Move(e.GetPosition(_dragLayer));
                return;
            }
            if (_pressedCard != null) {
                if (Distance(position, _pressPoint) > DragThreshold) StartDrag(e);
                return;
            }
            if (_panning) {
                _pan = _panAtPress + (position - _pressPoint);
                InvalidateVisual();
                return;
            }
            if (_selecting) {
                _selectionRect = new Rect(
                    new Point(Math.Min(_pressPoint.X, position.X), Math.Min(_pressPoint.Y, position.Y)),
                    new Point(Math.Max(_pressPoint.X, position.X), Math.Max(_pressPoint.Y, position.Y)));
                InvalidateVisual();
                return;
            }
            SetHovered(CardAt(position));
        }

        private void StartDrag(PointerEventArgs e) {
            var card = _pressedCard;
            _pressedCard = null;
            if (!card.TryToManipulate()) return;
            var cards = card.Selected ? Selection.Cards.Where(c => c.Group == _table).ToList() : new List<Card> { card };
            if (cards.Any(c => c.Anchored || !c.TryToManipulate())) return;
            var grabTable = ToTable(_pressPoint);
            var grab = new Vector(grabTable.X - card.X, grabTable.Y - card.Y);
            var faceUp = (e.KeyModifiers & KeyModifiers.Shift) == 0;
            _dragLayer.Start(new CardDrag(card, cards, grab, faceUp, _zoom), e.GetPosition(_dragLayer));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (_dragLayer.Current != null && e.Pointer.Captured == this) {
                _dragLayer.Drop(e.GetPosition(_dragLayer));
            } else if (_pressedCard != null) {
                // A click on a card: select it.
                if (!Gestures.TogglesSelection(e.KeyModifiers)) {
                    Selection.Clear();
                    if (_pressedCard.Group == _table) Selection.Add(_pressedCard);
                }
            } else if (_selecting) {
                foreach (var card in _table.Cards.Where(c => Intersects(c, _selectionRect)).ToList())
                    if (!card.Selected) Selection.Add(card);
            }
            _pressedCard = null;
            _panning = false;
            _selecting = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            if (_dragLayer.Current != null) _dragLayer.Cancel();
            _pressedCard = null;
            _panning = false;
            _selecting = false;
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            if (e.Pointer.Captured != this) SetHovered(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            var position = e.GetPosition(this);
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) {
                ZoomAt(position, Math.Pow(1.15, e.Delta.Y));
            } else {
                _pan += new Vector(e.Delta.X, e.Delta.Y) * 40;
                InvalidateVisual();
            }
            e.Handled = true;
        }

        private void OnPinch(object sender, PinchEventArgs e) {
            // Touch screens: the scale is relative to the start of the gesture.
            var factor = e.Scale / _lastPinch;
            _lastPinch = e.Scale;
            ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);
            e.Handled = true;
        }

        private void OnTouchPadMagnify(object sender, PointerDeltaEventArgs e) {
            ZoomAt(e.GetPosition(this), 1 + e.Delta.X);
            e.Handled = true;
        }

        private void UpdatePointerPosition(Point screen) {
            var table = ToTable(screen);
            PointerTablePosition = new EnginePoint(table.X, table.Y);
        }

        private bool Intersects(Card card, Rect screenRect) {
            var frame = CardFrame(card);
            var topLeft = ToScreen(frame.X, frame.Y);
            var bottomRight = ToScreen(frame.Right, frame.Bottom);
            return new Rect(topLeft, bottomRight).Intersects(screenRect);
        }

        private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private void ShowMenu(Card card, Point position) {
            var table = ToTable(position);
            var at = new EnginePoint(table.X, table.Y);
            List<Control> items;
            if (card != null) {
                if (!card.Selected) {
                    Selection.Clear();
                }
                items = GameActions.CardMenu(card, at);
            } else {
                items = GameActions.GroupMenu(_table, at);
            }
            Gestures.OpenMenu(this, items);
        }

        // Drop

        public void Drop(CardDrag drag, Point point) {
            var target = ToTable(point);
            var x = target.X - drag.Grab.X;
            var y = target.Y - drag.Grab.Y;
            var cards = drag.Cards;
            if (drag.Clicked.Group == _table) {
                // Cards of the table keep their places relative to the one dragged.
                var dx = x - drag.Clicked.X;
                var dy = y - drag.Clicked.Y;
                Card.MoveCardsToTable(cards.ToArray(), args => {
                    args.X = (int)(args.Card.X + dx);
                    args.Y = (int)(args.Card.Y + dy);
                    args.Index = _table.GetCardIndex(args.Card);
                    args.FaceUp = args.Card.FaceUp;
                }, false);
            } else {
                GameActions.MoveToTable(cards.ToList(), x, y, drag.FaceUp);
            }
            InvalidateVisual();
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Octgn.Core.DataExtensionMethods;
using Octgn.DataNew.Entities;
using Card = Octgn.Play.Card;

namespace Octgn.Desktop
{
    /// <summary>
    /// The questions of the game scripts (Octgn.Scripting.Controls in WPF). Each one is a modal window that
    /// returns once answered: the script waits for it on the game dispatcher, while the window's events run
    /// in a nested event loop (<see cref="GameDispatcher.RunUntil"/>).
    /// </summary>
    public static class Dialogs
    {
        /// <summary>A question opens (tests answer it).</summary>
        public static event Action<Window> Opened;

        /// <summary>Shows <paramref name="dialog"/> over <paramref name="owner"/> and waits until it closes.</summary>
        public static void ShowModal(Window dialog, Window owner) {
            dialog.Opened += (s, e) => Opened?.Invoke(dialog);
            using (var closed = new CancellationTokenSource()) {
                dialog.Closed += (s, e) => closed.Cancel();
                if (owner != null && owner.IsVisible) _ = dialog.ShowDialog(owner);
                else dialog.Show();
                GameDispatcher.RunUntil(closed.Token);
            }
        }

        private static Window Frame(string title, double width = 420) {
            return new Window {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 760,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
        }

        private static StackPanel Body(string prompt) {
            var body = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
            if (!string.IsNullOrEmpty(prompt))
                body.Children.Add(new SelectableTextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            return body;
        }

        private static StackPanel Buttons(params Button[] buttons) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            foreach (var b in buttons) row.Children.Add(b);
            return row;
        }

        private static Button Button(string text, bool isDefault = false, bool isCancel = false) {
            var button = new Button { Content = text, MinWidth = 80, IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault) button.Classes.Add("accent");
            return button;
        }

        public static bool Confirm(Window owner, string message) {
            var window = Frame("Confirmation");
            var body = Body(message);
            var yes = Button("Yes", isDefault: true);
            var no = Button("No", isCancel: true);
            var result = false;
            yes.Click += (s, e) => { result = true; window.Close(); };
            no.Click += (s, e) => window.Close();
            body.Children.Add(Buttons(no, yes));
            window.Content = body;
            ShowModal(window, owner);
            return result;
        }

        /// <summary>A text (null when cancelled); with <paramref name="positiveInteger"/>, only a number of 0 or more is accepted.</summary>
        public static string AskText(Window owner, string title, string question, string defaultValue, bool positiveInteger = false) {
            var window = Frame(title);
            var body = Body(question);
            var box = new TextBox { Text = defaultValue ?? "" };
            var ok = Button("OK", isDefault: true);
            var cancel = Button("Cancel", isCancel: true);
            string result = null;
            ok.Click += (s, e) => {
                if (positiveInteger && (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) || n < 0)) {
                    box.Classes.Add("error");
                    box.BorderBrush = Brushes.IndianRed;
                    return;
                }
                result = box.Text;
                window.Close();
            };
            cancel.Click += (s, e) => window.Close();
            body.Children.Add(box);
            body.Children.Add(Buttons(cancel, ok));
            window.Content = body;
            window.Opened += (s, e) => { box.Focus(); box.SelectAll(); };
            ShowModal(window, owner);
            return result;
        }

        /// <summary>ChoiceDlg: the number of the choice from 1, of the button from -1 down, 0 when closed.</summary>
        public static int AskChoice(Window owner, string title, string question, IList<string> choices, IList<string> colors, IList<string> buttons) {
            var window = Frame(title, 460);
            var body = Body(question);
            var result = 0;
            var list = new StackPanel { Spacing = 4 };
            for (var i = 0; i < choices.Count; i++) {
                var number = i + 1;
                var button = new Button {
                    Content = new TextBlock { Text = choices[i], TextWrapping = TextWrapping.Wrap },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 6),
                };
                if (colors != null && i < colors.Count && colors[i] != null && colors[i] != "None" && Color.TryParse(colors[i], out var color))
                    button.Background = new SolidColorBrush(color);
                button.Click += (s, e) => { result = number; window.Close(); };
                list.Children.Add(button);
            }
            body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 520 });
            if (buttons != null && buttons.Count > 0) {
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
                for (var i = 0; i < buttons.Count; i++) {
                    var number = -(i + 1);
                    var button = Button(buttons[i]);
                    button.Click += (s, e) => { result = number; window.Close(); };
                    row.Children.Add(button);
                }
                body.Children.Add(row);
            }
            window.Content = body;
            ShowModal(window, owner);
            return result;
        }

        /// <summary>A list of items to choose from, with a filter box: the chosen items (null when cancelled).</summary>
        private static List<T> Choose<T>(Window owner, string title, string question, IList<T> items, Func<T, string> label, Func<T, Card> card,
            bool multiple, int? min, int? max, Control extra = null) {
            var window = Frame(title, 520);
            var body = Body(question);
            var filter = new TextBox { PlaceholderText = "Filter" };
            var list = new ListBox {
                SelectionMode = multiple ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single,
                Height = 360,
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<T>((item, _) => ItemRow(label(item), card?.Invoke(item))),
            };
            void Refresh() {
                var text = filter.Text ?? "";
                list.ItemsSource = items.Where(i => label(i).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            }
            filter.TextChanged += (s, e) => Refresh();
            Refresh();
            var ok = Button("OK", isDefault: true);
            var cancel = Button("Cancel", isCancel: true);
            var status = new TextBlock { Foreground = Brushes.IndianRed, VerticalAlignment = VerticalAlignment.Center };
            List<T> result = null;
            void Accept() {
                var chosen = list.SelectedItems?.Cast<T>().ToList() ?? new List<T>();
                if (!multiple && chosen.Count != 1) { status.Text = "Choose one."; return; }
                if (min.HasValue && chosen.Count < min.Value) { status.Text = $"Choose at least {min.Value}."; return; }
                if (max.HasValue && chosen.Count > max.Value) { status.Text = $"Choose at most {max.Value}."; return; }
                result = chosen;
                window.Close();
            }
            ok.Click += (s, e) => Accept();
            list.DoubleTapped += (s, e) => { if (!multiple) Accept(); };
            cancel.Click += (s, e) => window.Close();
            if (items.Count > 8) body.Children.Add(filter);
            body.Children.Add(list);
            if (extra != null) body.Children.Add(extra);
            var row = Buttons(cancel, ok);
            row.Children.Insert(0, status);
            body.Children.Add(row);
            window.Content = body;
            ShowModal(window, owner);
            return result;
        }

        private static Control ItemRow(string text, Card card) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (card != null) {
                var picture = Pictures.ForCard(card, true);
                if (picture != null) row.Children.Add(new Image { Source = picture, Height = 56, Width = 40 });
            }
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        /// <summary>SelectCardsDlg: the index of the chosen card, or null.</summary>
        public static int? SelectCard(Window owner, IList<Card> cards, string question, string title) {
            var indexes = Enumerable.Range(0, cards.Count).ToList();
            var chosen = Choose(owner, title ?? "Choose a card", question, indexes, i => cards[i]?.RealName ?? "?", i => cards[i], false, null, null);
            return chosen?.FirstOrDefault();
        }

        public static Portable.SelectMultiCardsResult SelectMultiCards(Window owner, IList<int> ids, IList<int> ids2, int? min, int? max, string question, string title) {
            var cards = ids.Concat(ids2 ?? Enumerable.Empty<int>()).ToList();
            var chosen = Choose(owner, title ?? "Choose cards", question, cards, id => Card.Find(id)?.RealName ?? "?", id => Card.Find(id), true, min, max);
            if (chosen == null) return null;
            return new Portable.SelectMultiCardsResult {
                allCards = ids.ToList(),
                allCards2 = ids2?.ToList(),
                selectedCards = chosen,
            };
        }

        public static Tuple<GameMarker, int> AskMarker(Window owner) {
            var markers = Program.GameEngine.Definition.Markers.Values.Cast<GameMarker>().ToList();
            var quantity = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 999, Increment = 1, FormatString = "0" };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Quantity", VerticalAlignment = VerticalAlignment.Center }, quantity } };
            var chosen = Choose(owner, "Choose a marker", "Marker to add", markers, m => m.Name, null, false, null, null, row);
            if (chosen == null) return null;
            return Tuple.Create(chosen[0], (int)(quantity.Value ?? 1));
        }

        public static Tuple<DataNew.Entities.Card, int> AskCard(Window owner, Dictionary<string, List<string>> properties, string op, string title) {
            var cards = Program.GameEngine.Definition.AllCards()
                .Where(c => Matches(c, properties, op))
                .OrderBy(c => c.Name)
                .ToList();
            var quantity = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 99, Increment = 1, FormatString = "0" };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Quantity", VerticalAlignment = VerticalAlignment.Center }, quantity } };
            var chosen = Choose(owner, title ?? "Choose a card", null, cards, c => c.Name, null, false, null, null, row);
            if (chosen == null) return null;
            return Tuple.Create(chosen[0], (int)(quantity.Value ?? 1));
        }

        /// <summary>CardDlg's filter: the properties of the card, all of them for "and", any for "or" (none: every card).</summary>
        private static bool Matches(DataNew.Entities.Card card, Dictionary<string, List<string>> properties, string op) {
            if (properties == null || properties.Count == 0) return true;
            bool Has(KeyValuePair<string, List<string>> p) {
                if (string.Equals(p.Key, "Name", StringComparison.OrdinalIgnoreCase))
                    return p.Value.Any(v => string.Equals(v, card.Name, StringComparison.CurrentCultureIgnoreCase));
                var props = card.GetCardProperties();
                return props.Any(kv => string.Equals(kv.Key.Name, p.Key, StringComparison.OrdinalIgnoreCase)
                                        && p.Value.Any(v => string.Equals(v, kv.Value?.ToString(), StringComparison.CurrentCultureIgnoreCase)));
            }
            return string.Equals(op, "or", StringComparison.OrdinalIgnoreCase) ? properties.Any(Has) : properties.All(Has);
        }

        public static Pack AskPack(Window owner) {
            var packs = Program.GameEngine.Definition.Sets().SelectMany(s => s.Packs).OrderBy(p => p.Set.Name).ThenBy(p => p.Name).ToList();
            var chosen = Choose(owner, "Choose a pack", null, packs, p => p.Set.Name + " – " + p.Name, null, false, null, null);
            return chosen?.FirstOrDefault();
        }

        /// <summary>A modal question for a protected pile (PileViewPermissionDialog): (granted, permanent, answered).</summary>
        public static Tuple<bool, bool> AskPileViewPermission(Window owner, string requester, string viewDescription, string pile) {
            var choice = AskChoice(owner, "Pile protection", $"{requester} wants to look at {viewDescription} of {pile}.",
                new[] { "Allow this time", "Always allow", "Refuse" }, new[] { "None", "None", "None" }, Array.Empty<string>());
            switch (choice) {
                case 1: return Tuple.Create(true, false);
                case 2: return Tuple.Create(true, true);
                case 3: return Tuple.Create(false, false);
                default: return null;
            }
        }
    }
}

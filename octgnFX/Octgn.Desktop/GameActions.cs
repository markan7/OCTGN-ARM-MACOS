// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Octgn.Core.DataExtensionMethods;
using Octgn.DataNew.Entities;
using Octgn.Play;
using Octgn.Play.Gui;
using Card = Octgn.Play.Card;
using EnginePoint = Octgn.Portable.Point;
using Group = Octgn.Play.Group;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop
{
    /// <summary>
    /// The actions of the game definition (card and group actions of each group, with their shortcuts) and
    /// the client's own (move to a pile, peek), as the WPF controls run them (GroupControl, TableControl).
    /// </summary>
    public static class GameActions
    {
        private static Scripting.Engine ScriptEngine => Program.GameEngine.ScriptEngine;

        /// <summary>The cards an action on <paramref name="card"/> applies to: the selection if the card is in it.</summary>
        public static List<Card> Targets(Card card) => Selection.ExtendToSelection(card).ToList();

        /// <summary>Double click on a card: the group's default card action, else its default group action.</summary>
        public static void DefaultCardAction(Card card) {
            var group = card.Group;
            if (group == null) return;
            var action = DefaultAction(group.Def.CardActions);
            if (action != null && card.TryToManipulate()) {
                group.KeepControl();
                card.KeepControl();
                RunCardAction(action, Targets(card), null);
                group.ReleaseControl();
                card.ReleaseControl();
                return;
            }
            DefaultGroupAction(group, null);
        }

        /// <summary>Double click on a group: its default group action (Draw for most decks).</summary>
        public static bool DefaultGroupAction(Group group, EnginePoint? position) {
            var action = DefaultAction(group.Def.GroupActions);
            if (action == null || !group.TryToManipulate()) return false;
            group.KeepControl();
            RunGroupAction(action, group, position);
            group.ReleaseControl();
            return true;
        }

        private static GroupAction DefaultAction(IEnumerable<IGroupAction> actions) {
            return actions.OfType<GroupAction>().FirstOrDefault(a => a.DefaultAction);
        }

        public static void RunCardAction(GroupAction action, IList<Card> cards, EnginePoint? position) {
            if (action.Execute == null) return;
            if (action.IsBatchExecutable) ScriptEngine.ExecuteOnBatch(action.Execute, cards, position);
            else ScriptEngine.ExecuteOnCards(action.Execute, cards, position);
        }

        public static void RunGroupAction(GroupAction action, Group group, EnginePoint? position) {
            if (action.Execute == null) return;
            ScriptEngine.ExecuteOnGroup(action.Execute, group, position ?? new EnginePoint(0, 0));
        }

        /// <summary>The context menu of a card: the game's card actions of its group, then moves to the piles.</summary>
        public static List<Control> CardMenu(Card card, EnginePoint? position) {
            var items = new List<Control>();
            var group = card.Group;
            if (group == null) return items;
            var targets = Targets(card);
            items.AddRange(ActionItems(group.Def.CardActions, a => RunCardAction(a, targets, position)));

            if (items.Count > 0) items.Add(new Separator());
            var moveTo = new MenuItem { Header = "Move to" };
            var piles = Player.LocalPlayer.Groups.Where(g => g != group).ToList();
            if (group != Program.GameEngine.Table)
                moveTo.Items.Add(Item("Table", () => MoveToTable(targets, 0, 0)));
            foreach (var pile in piles) {
                var target = pile;
                moveTo.Items.Add(Item(target.Name + " (top)", () => Selection.Do(c => c.MoveTo(target, true, 0, false), card)));
                moveTo.Items.Add(Item(target.Name + " (bottom)", () => Selection.Do(c => c.MoveTo(target, true, target.Count, false), card)));
            }
            items.Add(moveTo);
            if (!card.FaceUp)
                items.Add(Item("Peek", () => Selection.Do(c => c.Peek(), card)));
            return items;
        }

        /// <summary>The context menu of a group (the table, or a pile): the game's group actions.</summary>
        public static List<Control> GroupMenu(Group group, EnginePoint? position) {
            var items = ActionItems(group.Def.GroupActions, a => RunGroupAction(a, group, position));
            if (group is Pile pile) {
                if (items.Count > 0) items.Add(new Separator());
                items.Add(Item("Look at all cards", () => ShowPile(pile, PilePosition.All, 0)));
                items.Add(Item("Look at top 5 cards", () => ShowPile(pile, PilePosition.Top, 5)));
                items.Add(Item("Shuffle", () => pile.Shuffle()));
            }
            return items;
        }

        private static void ShowPile(Pile pile, PilePosition position, int count) {
            if (pile.Owner != Player.LocalPlayer && pile.ProtectionState == GroupProtectionState.True) {
                Program.GameMess.Warning($"Cannot view {pile.FullName} - it is protected.");
                return;
            }
            Program.GameMess.PlayerEvent(Player.LocalPlayer, position == PilePosition.All ? "looks at {0}." : "looks at the top {1} cards of {0}.", pile, count);
            Program.Ui.ShowGroup(pile, position, count);
        }

        private static List<Control> ActionItems(IEnumerable<IGroupAction> actions, Action<GroupAction> run) {
            var items = new List<Control>();
            foreach (var action in actions) {
                switch (action) {
                    case GroupActionSeparator _:
                        items.Add(new Separator());
                        break;
                    case GroupActionSubmenu submenu:
                        var sub = new MenuItem { Header = submenu.Name };
                        foreach (var child in ActionItems(submenu.Children, run)) sub.Items.Add(child);
                        items.Add(sub);
                        break;
                    case GroupAction groupAction:
                        var item = Item(groupAction.Name, () => run(groupAction));
                        if (groupAction.DefaultAction) item.FontWeight = Avalonia.Media.FontWeight.Bold;
                        if (groupAction.Shortcut != null) item.InputGesture = ToAvalonia(groupAction.Shortcut);
                        items.Add(item);
                        break;
                }
            }
            return items;
        }

        private static MenuItem Item(string header, Action click) {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => click();
            return item;
        }

        /// <summary>Moves cards to the table at a position (the first one there, the others next to it), as a drop does.</summary>
        public static void MoveToTable(IList<Card> cards, double x, double y, bool? faceUp = null) {
            var table = Program.GameEngine.Table;
            var curX = x;
            var xOffset = 0.0;
            Card.MoveCardsToTable(cards.ToArray(), args => {
                curX += xOffset;
                args.X = (int)curX;
                args.Y = (int)y;
                var idx = table.GetCardIndex(args.Card);
                args.Index = idx == -1 ? table.Count : idx;
                args.FaceUp = faceUp ?? (args.Card.Group == table ? args.Card.FaceUp : true);
                xOffset = args.Card.RealWidth * 1.05;
            }, false);
        }

        /// <summary>The game's shortcut, as Avalonia reads it ("Ctrl" stands for ⌘ on macOS too: see <see cref="Matches"/>).</summary>
        private static KeyGesture ToAvalonia(string shortcut) {
            try {
                var gesture = Portable.KeyGestureConverter.Parse(shortcut);
                return Enum.TryParse<Key>(gesture.Key, true, out var key) ? new KeyGesture(key, Modifiers(gesture.Modifiers)) : null;
            } catch (NotSupportedException) {
                return null;
            }
        }

        private static KeyModifiers Modifiers(Portable.ModifierKeys modifiers) {
            var result = KeyModifiers.None;
            if (modifiers.HasFlag(Portable.ModifierKeys.Control)) result |= KeyModifiers.Control;
            if (modifiers.HasFlag(Portable.ModifierKeys.Shift)) result |= KeyModifiers.Shift;
            if (modifiers.HasFlag(Portable.ModifierKeys.Alt)) result |= KeyModifiers.Alt;
            if (modifiers.HasFlag(Portable.ModifierKeys.Windows)) result |= KeyModifiers.Meta;
            return result;
        }

        /// <summary>A key of the keyboard matches a shortcut of the game: Ctrl in the game is Ctrl or ⌘ here.</summary>
        public static bool Matches(Portable.KeyGesture gesture, Key key, KeyModifiers modifiers) {
            if (gesture == null || !Enum.TryParse<Key>(gesture.Key, true, out var expected)) return false;
            if (key != expected && !(expected == Key.Return && key == Key.Enter)) return false;
            var wanted = Modifiers(gesture.Modifiers);
            var pressed = modifiers;
            if (pressed.HasFlag(KeyModifiers.Meta) && !wanted.HasFlag(KeyModifiers.Meta)) {
                pressed &= ~KeyModifiers.Meta;
                pressed |= KeyModifiers.Control;
            }
            return pressed == wanted;
        }

        /// <summary>
        /// Runs the game's shortcut for a key: the hovered card's card actions first, then the group actions of
        /// the table and of the local player's piles, then the piles' own shortcuts (move the selection to a
        /// pile, shuffle a pile). True when a shortcut ran.
        /// </summary>
        public static bool RunShortcut(Key key, KeyModifiers modifiers, Card hovered, EnginePoint? tablePosition) {
            if (Program.GameEngine == null || Player.LocalPlayer == null) return false;
            if (hovered?.Group != null) {
                var match = hovered.Group.CardShortcuts.FirstOrDefault(s => Matches(s.Key, key, modifiers));
                if (match != null) {
                    if (!hovered.TryToManipulate()) return true;
                    RunCardAction(match.ActionDef.AsAction(), Targets(hovered), hovered.Group == Program.GameEngine.Table ? tablePosition : null);
                    return true;
                }
            }
            var groups = new List<Group> { Program.GameEngine.Table };
            groups.AddRange(Player.LocalPlayer.Groups);
            foreach (var group in groups) {
                var match = group.GroupShortcuts.FirstOrDefault(s => Matches(s.Key, key, modifiers));
                if (match == null) continue;
                if (group.TryToManipulate())
                    RunGroupAction(match.ActionDef.AsAction(), group, group == Program.GameEngine.Table ? tablePosition : null);
                return true;
            }
            foreach (var group in Player.LocalPlayer.Groups) {
                if (Matches(group.MoveToShortcut, key, modifiers) && hovered != null) {
                    Selection.Do(c => c.MoveTo(group, true, 0, false), hovered);
                    return true;
                }
                if (group is Pile pile && Matches(group.ShufflePileShortcut, key, modifiers)) {
                    pile.Shuffle();
                    return true;
                }
            }
            return false;
        }
    }
}

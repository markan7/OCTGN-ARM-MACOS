// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NUnit.Framework;
using Octgn.Core.DataExtensionMethods;
using Octgn.DataNew.Entities;
using Octgn.Play;
using Octgn.Portable;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;
using Point = Avalonia.Point;
using Dispatcher = Avalonia.Threading.Dispatcher;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop.Test
{
    using EngineProgram = Octgn.Program;

    /// <summary>
    /// The Avalonia table playing a local game of the Basic-Game template: the views of the player's groups,
    /// the mouse (double click, drag and drop, zoom), the questions of the scripts and the game's actions.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class TableTests : UiFixture
    {
        [OneTimeSetUp]
        public async Task OpenTheTable() {
            await Ui(async () => {
                await OpenGame(work => Path.GetDirectoryName(Headless.SampleGame.Prepare(Path.Combine(work, "Game"))), "Alice", 21297);
                await Save("01-lobby");
                await StartGame();
                var game = EngineProgram.GameEngine.Definition;
                var model = game.AllCards().First();
                var deck = new Deck {
                    GameId = game.Id,
                    Sections = new List<ISection> {
                        new Section { Name = "Main Deck", Cards = new List<IMultiCard> { new MultiCard(model, 12) } }
                    }
                };
                EngineProgram.GameEngine.LoadDeck(deck, false);
                await Until("the deck is loaded", () => Pile("Deck").Count == 12);
            });
        }

        [OneTimeTearDown]
        public Task CloseTheTable() => CloseGame();

        [Test, Order(1)]
        public async Task TheTableShowsThePlayersGroups() {
            await Ui(async () => {
                await Save("02-table");
                CollectionAssert.AreEquivalent(new[] { "Hand", "Deck", "Discard" }, Window.PlayerGroupViews.Select(v => v.Group.Name).ToArray());
                // The legacy hand of the template is collapsed: a pile view.
                Assert.IsInstanceOf<PileView>(View("Hand"));
                Assert.IsInstanceOf<PileView>(View("Deck"));
                await Task.CompletedTask;
            });
        }

        [Test, Order(2)]
        public async Task DoubleClickOnTheDeckDraws() {
            await Ui(async () => {
                var deck = View("Deck");
                var top = deck.BoundsOf(Pile("Deck").Cards[0]);
                Assert.IsNotNull(top, "the deck shows its top card");
                await Click(Center(deck, top.Value), count: 2);
                await Until("the default action (Draw) moved a card to the hand", () => Pile("Hand").Count == 1);
                Assert.AreEqual(11, Pile("Deck").Count);
            });
        }

        [Test, Order(3)]
        public async Task ACardDraggedFromTheHandLandsOnTheTable() {
            await Ui(async () => {
                var hand = View("Hand");
                var card = Pile("Hand").Cards[0];
                var from = Center(hand, hand.BoundsOf(card).Value);
                var table = Window.Table;
                var to = table.TranslatePoint(table.ToScreen(-150, -60), Window).Value;
                await Drag(from, to);
                await Until("the card is on the table", () => EngineProgram.GameEngine.Table.Cards.Contains(card));
                Assert.IsTrue(card.FaceUp, "dropped face up (no Shift)");
                // Where it was dropped: the pointer held the middle of the card.
                Assert.That(card.X + card.RealWidth / 2.0, Is.EqualTo(-150).Within(2));
                Assert.That(card.Y + card.RealHeight / 2.0, Is.EqualTo(-60).Within(2));
            });
        }

        [Test, Order(4)]
        public async Task ACardOfTheTableMovesWhenDragged() {
            await Ui(async () => {
                var table = Window.Table;
                var card = EngineProgram.GameEngine.Table.Cards[0];
                var from = table.TranslatePoint(table.ToScreen(card.X + card.RealWidth / 2.0, card.Y + card.RealHeight / 2.0), Window).Value;
                var to = table.TranslatePoint(table.ToScreen(100, 40), Window).Value;
                await Drag(from, to);
                await Until("the card moved", () => Math.Abs(card.X + card.RealWidth / 2.0 - 100) < 2);
            });
        }

        [Test, Order(5)]
        public async Task CtrlWheelZoomsTheTable() {
            await Ui(async () => {
                var table = Window.Table;
                var before = table.Zoom;
                var center = table.TranslatePoint(new Point(table.Bounds.Width / 2, table.Bounds.Height / 2), Window).Value;
                Window.MouseWheel(center, new Vector(0, 2), RawInputModifiers.Control);
                await Task.Delay(20);
                Assert.That(table.Zoom, Is.GreaterThan(before * 1.2));
                table.FitView();
            });
        }

        [Test, Order(6)]
        public async Task AScriptQuestionIsAWindowOfTheTable() {
            await Ui(async () => {
                var asked = new List<string>();
                void Answer(Window dialog) {
                    var box = dialog.GetLogicalDescendants().OfType<TextBox>().First();
                    asked.Add(dialog.Title);
                    box.Text = "3";
                    var ok = dialog.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == "OK");
                    ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                Dialogs.Opened += Answer;
                try {
                    EngineProgram.GameEngine.ScriptEngine.ExecuteOnGroup("drawMany", Pile("Deck"), new Portable.Point(0, 0));
                    await Until("three cards were drawn", () => Pile("Hand").Count == 3);
                } finally {
                    Dialogs.Opened -= Answer;
                }
                Assert.AreEqual(new[] { "Question" }, asked.ToArray());
            });
        }

        [Test, Order(7)]
        public async Task TheGameActionsAreInTheCardMenu() {
            await Ui(async () => {
                var card = EngineProgram.GameEngine.Table.Cards[0];
                var items = GameActions.CardMenu(card, new Portable.Point(card.X, card.Y)).OfType<MenuItem>().ToList();
                Assert.That(items.Select(i => i.Header as string), Is.SupersetOf(new[] { "Rotate Card", "Flip Card Face Up/Down", "Add Marker", "Move to" }));
                items.First(i => (string)i.Header == "Rotate Card").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Until("the card is rotated", () => card.Orientation == CardOrientation.Rot90);
                items.First(i => (string)i.Header == "Add Marker").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Until("the card has a marker", () => card.Markers.Count == 1);
                items.First(i => (string)i.Header == "Add/Remove Highlight").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Until("the card is highlighted", () => card.HighlightColor != null);
            });
        }

        [Test, Order(8)]
        public async Task MoreCardsOnTheTable() {
            await Ui(async () => {
                // Two cards of the hand side by side, one face down (Shift held).
                var hand = View("Hand");
                var table = Window.Table;
                var cards = Pile("Hand").Cards.ToList();
                GameActions.MoveToTable(new[] { cards[0] }, -260, 60, true);
                GameActions.MoveToTable(new[] { cards[1] }, -170, 60, false);
                await Until("both cards are on the table", () => EngineProgram.GameEngine.Table.Count == 3);
                Window.MouseMove(table.TranslatePoint(table.ToScreen(-240, 90), Window).Value);
                await Task.Delay(50);
                await Save("03-table-play");
                Assert.AreEqual(cards[0], table.Hovered, "the card under the pointer is hovered");
            });
        }

        [Test, Order(9)]
        public async Task ThePilesCanBeLookedAt() {
            await Ui(async () => {
                GroupWindow opened = null;
                void Opening(GroupWindow w) => opened = w;
                GroupWindow.Opening += Opening;
                try {
                    // As the scripts' lookAt does (the menu of the deck too).
                    EngineProgram.Ui.ShowGroup(Pile("Deck"), Play.Gui.PilePosition.Top, 3);
                } finally {
                    GroupWindow.Opening -= Opening;
                }
                Assert.IsNotNull(opened);
                await Task.Delay(100);
                await Save(opened, "04-pile-window");
                Assert.AreEqual("Alice's Deck: top 3 cards", opened.Title);
                opened.Close();
            });
        }

        [Test, Order(10)]
        public async Task TheClicksFollowThePlatform() {
            await Ui(async () => {
                var table = Window.Table;
                var card = EngineProgram.GameEngine.Table.Cards[0];
                var center = table.TranslatePoint(table.ToScreen(card.X + card.RealWidth / 2.0, card.Y + card.RealHeight / 2.0), Window).Value;
                // Two points of the card, clicked in turn: two clicks at one point would make a double click.
                var points = new[] { center, center + new Vector(0, 8) };
                var next = 0;
                Task ClickCard(RawInputModifiers modifiers, MouseButton button = MouseButton.Left) => Click(points[next++ % 2], button: button, modifiers: modifiers);
                var menus = new List<ContextMenu>();
                void Opened(ContextMenu menu) => menus.Add(menu);
                var mac = Gestures.MacConventions;
                Gestures.MenuOpened += Opened;
                try {
                    Play.Gui.Selection.Clear();

                    // Windows and Linux: Ctrl + click adds the card to the selection or takes it out.
                    Gestures.MacConventions = false;
                    await ClickCard(RawInputModifiers.Control);
                    Assert.IsTrue(card.Selected, "Ctrl + click selects the card");
                    await ClickCard(RawInputModifiers.Control);
                    Assert.IsFalse(card.Selected, "Ctrl + click again takes it out of the selection");
                    Assert.IsEmpty(menus);
                    await ClickCard(RawInputModifiers.None, MouseButton.Right);
                    Assert.AreEqual(1, menus.Count, "the right button opens the card's menu");
                    menus[0].Close();

                    // macOS: Control + click is the secondary click, ⌘ + click adds to the selection.
                    Gestures.MacConventions = true;
                    await ClickCard(RawInputModifiers.Control);
                    Assert.AreEqual(2, menus.Count, "Control + click opens the card's menu");
                    Assert.That(menus[1].ItemsSource.OfType<MenuItem>().Select(i => i.Header as string), Does.Contain("Rotate Card"));
                    Assert.IsFalse(card.Selected);
                    menus[1].Close();
                    await ClickCard(RawInputModifiers.Meta);
                    Assert.IsTrue(card.Selected, "⌘ + click selects the card");
                    await ClickCard(RawInputModifiers.Meta);
                    Assert.IsFalse(card.Selected, "⌘ + click again takes it out of the selection");

                    // The piles too: Control + click on the deck opens its menu, with the deck's actions.
                    var deck = View("Deck");
                    await Click(Center(deck, deck.BoundsOf(Pile("Deck").Cards[0]).Value), modifiers: RawInputModifiers.Control);
                    Assert.AreEqual(3, menus.Count, "Control + click on the deck opens its menu");
                    Assert.That(menus[2].ItemsSource.OfType<MenuItem>().Select(i => i.Header as string), Does.Contain("Draw"));
                } finally {
                    Gestures.MacConventions = mac;
                    Gestures.MenuOpened -= Opened;
                    foreach (var menu in menus) menu.Close();
                    Play.Gui.Selection.Clear();
                }
            });
        }
    }
}

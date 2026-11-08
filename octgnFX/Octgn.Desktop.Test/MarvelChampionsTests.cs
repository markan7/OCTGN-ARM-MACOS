// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Octgn.DataNew.Entities;
using Octgn.Portable;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop.Test
{
    using EngineProgram = Octgn.Program;

    /// <summary>
    /// Marvel Champions (a third-party game with 1 728 cards and 7 000 lines of scripts) in the table: its
    /// "Load Hero" action asks which deck to load (a choice), then which hero (a list of cards), and sets
    /// the table up. Runs when OCTGN_MARVEL_CHAMPIONS names the game's folder (the one of definition.xml,
    /// 055c536f-adba-4bc2-acbf-9aefb9756046 in github.com/twistedsistem/OCTGN-Marvel-Champions).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class MarvelChampionsTests : UiFixture
    {
        private static readonly string Folder = Environment.GetEnvironmentVariable("OCTGN_MARVEL_CHAMPIONS");
        private readonly List<string> _questions = new List<string>();

        [OneTimeSetUp]
        public async Task OpenTheGame() {
            if (string.IsNullOrEmpty(Folder) || !File.Exists(Path.Combine(Folder, "definition.xml")))
                Assert.Ignore("OCTGN_MARVEL_CHAMPIONS does not name the folder of the game.");
            // The game shows what is new in its version when the table loads (OnTableLoaded: confirm).
            Dialogs.Opened += Answer;
            await Ui(async () => {
                await OpenGame(_ => Folder, "Peter", 21296);
                await StartGame();
            });
        }

        [OneTimeTearDown]
        public async Task CloseTheGame() {
            Dialogs.Opened -= Answer;
            if (Window != null) await CloseGame();
        }

        /// <summary>
        /// Answers the questions of the game: yes to its news, and for Load Hero an out of the box deck, then
        /// the first hero of the list.
        /// </summary>
        private void Answer(Window dialog) {
            _questions.Add(dialog.Title);
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToList();
            var yes = buttons.FirstOrDefault(b => (b.Content as string) == "Yes");
            if (yes != null) {
                yes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            var outOfTheBox = buttons.FirstOrDefault(b => (b.Content as TextBlock)?.Text == "An out of the box deck");
            if (outOfTheBox != null) {
                outOfTheBox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            var list = dialog.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault();
            if (list != null) {
                list.SelectedIndex = 0;
                buttons.First(b => (b.Content as string) == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            dialog.Close();
        }

        [Test, Order(1)]
        public async Task TheTableOfTheGameOpens() {
            await Ui(async () => {
                await Save("mc-01-table");
                // The hand of Marvel Champions is expanded: spread out.
                Assert.IsInstanceOf<FanView>(View("Hand"));
                Assert.IsInstanceOf<PileView>(View("Deck"));
            });
        }

        [Test, Order(2)]
        public async Task LoadHeroAsksAndSetsTheTableUp() {
            await Ui(async () => {
                var table = EngineProgram.GameEngine.Table;
                var loadHero = table.Definition.GroupActions.OfType<GroupAction>().First(a => a.Name == "Load Hero");
                _questions.Clear();
                GameActions.RunGroupAction(loadHero, table, new Portable.Point(0, 0));
                await Until("the hero's cards are on the table and in the hand", () => table.Count > 0 && Pile("Hand").Count > 0);
                // Let the rest of the setup (markers, counters) arrive.
                await Task.Delay(1500);
                await Save("mc-02-hero");
                Assert.That(_questions, Has.Some.EqualTo("Choose One"));
                Assert.That(_questions.Count, Is.GreaterThanOrEqualTo(2), "a choice, then the list of heroes");
                var errors = Window.ChatText.Where(t => t.Contains("Traceback") || t.Contains("Error")).ToList();
                Assert.IsEmpty(errors, "script errors: " + string.Join(" | ", errors));
            });
        }

        [Test, Order(3)]
        public async Task TheHandsCardsCanBePlayed() {
            await Ui(async () => {
                var hand = View("Hand");
                var card = Pile("Hand").Cards.Last();
                var from = Center(hand, hand.BoundsOf(card).Value);
                var to = Window.Table.TranslatePoint(Window.Table.ToScreen(0, 120), Window).Value;
                await Drag(from, to);
                await Until("the card is on the table", () => EngineProgram.GameEngine.Table.Cards.Contains(card));
                Window.MouseMove(to);
                await Task.Delay(50);
                await Save("mc-03-played");
                Assert.AreEqual(Player.LocalPlayer, card.Controller);
            });
        }
    }
}

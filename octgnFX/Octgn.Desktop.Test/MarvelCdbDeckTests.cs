// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
using NUnit.Framework;
using Octgn.Core.DataExtensionMethods;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop.Test
{
    using EngineProgram = Octgn.Program;

    /// <summary>
    /// A deck of MarvelCDB (an .o8d with one section, Cards) loaded as Marvel Champions loads one: "Load Hero",
    /// then "A downloaded deck (.o8d file)", the script asking for the file. The game takes the hero from the
    /// deck and sets the table up; OCTGN's own deck loading is not the way (the game turns it off). The hero is
    /// Echo when the version of the game has it (Fear No Evil, in 0.0.3.98): the deck that the version of
    /// twistedsistem's repository (0.0.3.10, 2022) refused.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class MarvelCdbDeckTests : UiFixture
    {
        private readonly List<string> _questions = new List<string>();
        private readonly List<string> _filters = new List<string>();
        private string _work;
        private string _deck;

        [OneTimeSetUp]
        public async Task OpenTheGame() {
            var folder = MarvelChampionsTests.Folder;
            if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "definition.xml")))
                Assert.Ignore("OCTGN_MARVEL_CHAMPIONS does not name the folder of the game.");
            _work = Path.Combine(Path.GetTempPath(), "octgn-marvelcdb-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_work);
            Dialogs.Opened += Answer;
            DesktopPlayUi.ChooseFileToOpen = filter => {
                _filters.Add(filter);
                return _deck;
            };
            await Ui(async () => {
                await OpenGame(_ => folder, "Jessica", 21297);
                await StartGame();
            });
        }

        [OneTimeTearDown]
        public async Task CloseTheGame() {
            Dialogs.Opened -= Answer;
            DesktopPlayUi.ChooseFileToOpen = null;
            if (Window != null) await CloseGame();
            try { if (_work != null) Directory.Delete(_work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>The deck of a file: "A downloaded deck (.o8d file)" (0.0.3.98), "A registered deck (o8d)" (0.0.3.10).</summary>
        private void Answer(Window dialog) {
            _questions.Add(dialog.Title);
            MarvelChampionsTests.AnswerWith(dialog, "A downloaded deck (.o8d file)", "A registered deck (o8d)");
        }

        private static string Property(DataNew.Entities.Card card, string name) => card.GetProperty(name)?.ToString() ?? "";

        [Test]
        public async Task AMarvelCdbDeckLoadsThroughLoadHero() {
            await Ui(async () => {
                var cards = EngineProgram.GameEngine.Definition.AllCards().ToList();
                var hero = cards.FirstOrDefault(c => c.Name == "Echo" && Property(c, "Type") == "hero")
                    ?? cards.First(c => c.Name == "Spider-Man" && Property(c, "Type") == "hero");
                var owner = Property(hero, "Owner");
                // As MarvelCDB writes a deck: the hero, cards of the hero, and aspect cards (no owner).
                var ofTheHero = cards.Where(c => Property(c, "Owner") == owner && Property(c, "Type") is "event" or "support" or "upgrade").Take(3);
                var aspect = cards.Where(c => Property(c, "Owner") == "" && Property(c, "Type") is "event" or "ally" or "upgrade").Take(4).ToList();
                Assert.AreEqual(4, aspect.Count, "aspect cards in the game");
                _deck = Path.Combine(_work, "marvelcdb-" + owner + ".o8d");
                new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                    new XElement("deck", new XAttribute("game", EngineProgram.GameEngine.Definition.Id),
                        new XElement("section", new XAttribute("name", "Cards"), new XAttribute("shared", "False"),
                            new[] { hero }.Concat(ofTheHero).Concat(aspect).Select(c =>
                                new XElement("card", new XAttribute("qty", 1), new XAttribute("id", c.Id), c.Name))))).Save(_deck);

                var table = EngineProgram.GameEngine.Table;
                _questions.Clear();
                GameActions.RunGroupAction(MarvelChampionsTests.TableAction("Load Hero"), table, new Portable.Point(0, 0));
                await Until("the hero is on the table and the deck ready", () => table.Count > 0 && Pile("Deck").Count > 0, () => GameState(_questions));
                await Task.Delay(1500);
                await Save("mcdb-01-deck");

                Assert.AreEqual(1, _filters.Count, "the game asked for the file of the deck");
                StringAssert.Contains("*.o8d", _filters[0]);
                var mine = table.Cards.Concat(Player.LocalPlayer.Groups.SelectMany(g => g.Cards)).Select(c => c.Type.Model.Id).ToList();
                CollectionAssert.Contains(mine, hero.Id, $"{hero.Name}, the hero of the deck, is set up");
                foreach (var card in aspect) CollectionAssert.Contains(mine, card.Id, $"{card.Name}, of the deck");
                Assert.IsFalse(Window.ChatText.Any(t => t.Contains("not found in octgn database")), string.Join(" | ", Window.ChatText));
                MarvelChampionsTests.AssertNoScriptError(Window);
            });
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Octgn.Core.DataExtensionMethods;
using Octgn.Core.Play;
using Octgn.DataNew.Entities;
using Octgn.Play;
using Octgn.Portable;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;
using Player = Octgn.Play.Player;

namespace Octgn.Test.Engine
{
    // The engine's Program, not the one of this test program.
    using Program = Octgn.Program;

    /// <summary>
    /// The game engine of the WPF client (Octgn.JodsEngine), built for .NET 10 as Octgn.Engine, playing a
    /// local game end to end: the .NET 10 game server in its own process, the engine's protocol client,
    /// the game model and the IronPython scripts of the game (Basic-Game template of Octgn.GameStudio).
    /// </summary>
    [TestFixture, NonParallelizable, Category("Engine")]
    public class GameEngineTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

        private string _work;
        private DispatcherThread _dispatcher;
        private RecordingUi _ui;
        private GameSession _session;
        private Game _game;
        private readonly List<IGameMessage> _messages = new List<IGameMessage>();

        private sealed class RecordingUi : HeadlessPlayUi
        {
            public int? IntegerAnswer;

            public override int? AskInteger(string title, string question, int defaultValue) {
                Record("AskInteger " + question);
                return IntegerAnswer ?? defaultValue;
            }
        }

        [OneTimeSetUp]
        public void StartGame() {
            _work = Path.Combine(Path.GetTempPath(), "octgn-engine-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_work);
            GameSession.Initialize(Path.Combine(_work, "Data"));

            _dispatcher = new DispatcherThread();
            Program.Dispatcher = _dispatcher;
            Program.Ui = _ui = new RecordingUi();
            Program.GameMess.OnMessage += OnMessage;

            var definition = Headless.SampleGame.Prepare(Path.Combine(_work, "Game"));
            _game = GameSession.InstallGame(Path.GetDirectoryName(definition));

            _session = GameSession.HostLocalGame(_game, "Alice", broadcastPort: 21298).GetAwaiter().GetResult();
            Until("the server welcomes the player", () => Program.GameEngine.IsWelcomed && Player.LocalPlayer.Id != 255);

            _session.StartGame();
            Until("the table is loaded", () => Program.GameEngine.TableLoaded);
        }

        [OneTimeTearDown]
        public void LeaveGame() {
            Program.GameMess.OnMessage -= OnMessage;
            _session?.Dispose();
            _session?.StopServer();
            _dispatcher?.Dispose();
            Program.Dispatcher = null;
            try { Directory.Delete(_work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private void OnMessage(IGameMessage message) {
            lock (_messages) _messages.Add(message);
        }

        private List<string> Texts() {
            lock (_messages) return _messages.Select(GameMessageText.Body).ToList();
        }

        /// <summary>Waits until <paramref name="condition"/>, evaluated on the game dispatcher, holds.</summary>
        private void Until(string what, Func<bool> condition) {
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline) {
                if (_dispatcher.Invoke(condition)) return;
                Thread.Sleep(50);
            }
            Assert.Fail($"Timed out waiting until {what}. Messages: " + string.Join(" | ", Texts()));
        }

        private T OnDispatcher<T>(Func<T> func) => _dispatcher.Invoke(func);

        private Group Pile(string name) => Player.LocalPlayer.Groups.First(g => g.Name == name);

        private void LoadDeck(int count) {
            var model = _game.AllCards().First();
            var deck = new Deck {
                GameId = _game.Id,
                Sections = new List<ISection> {
                    new Section { Name = "Main Deck", Shared = false, Cards = new List<IMultiCard> { new MultiCard(model, count) } }
                }
            };
            _dispatcher.Invoke(() => Program.GameEngine.LoadDeck(deck, false));
        }

        [Test, Order(1)]
        public void TheGameStarts() {
            Assert.AreEqual(1, _ui.Events.Count(e => e == "StartGame"));
            OnDispatcher(() => {
                Assert.AreEqual("Alice", Player.LocalPlayer.Name);
                CollectionAssert.AreEquivalent(new[] { "Hand", "Deck", "Discard" }, Player.LocalPlayer.Groups.Select(g => g.Name).ToArray());
                Assert.AreEqual(1, Player.LocalPlayer.Counters.Length);
                // Keyboard shortcuts of the definition, read without WPF.
                Assert.AreEqual(new KeyGesture("D", ModifierKeys.Control), Pile("Deck").GroupShortcuts.First(s => s.ActionDef.Name == "Draw").Key);
                Assert.AreEqual(new KeyGesture("Delete", ModifierKeys.None), Pile("Discard").MoveToShortcut);
                Assert.IsNotNull(Program.GameEngine.ScriptEngine.ActionsScope, "script engine set up when the table loads");
                return 0;
            });
        }

        [Test, Order(2)]
        public void ADeckLoadsIntoItsPile() {
            LoadDeck(10);
            Until("the deck is in its pile", () => Pile("Deck").Count == 10);
            OnDispatcher(() => {
                var card = Pile("Deck").Cards.First();
                Assert.AreEqual(Player.LocalPlayer, card.Owner);
                Assert.IsFalse(card.FaceUp);
                // The user interface shows the back of the card's size: the game's back.png.
                StringAssert.EndsWith("back.png", card.GetPictureUri(card.FaceUp));
                return 0;
            });
        }

        [Test, Order(3)]
        public void AScriptDrawsACard() {
            OnDispatcher(() => {
                Program.GameEngine.ScriptEngine.ExecuteOnGroup("draw", Pile("Deck"), new Point(0, 0));
                return 0;
            });
            Until("the card is in the hand", () => Pile("Hand").Count == 1 && Pile("Deck").Count == 9);
            Until("the script notified the table", () => Texts().Any(t => t == "Alice draws a card from Deck."));
            OnDispatcher(() => {
                var card = Pile("Hand").Cards.Single();
                // The hand is visible to its owner: the card is known.
                Assert.AreEqual("Ace of Spades", card.Name);
                return 0;
            });
        }

        [Test, Order(4)]
        public void AScriptAsksTheUser() {
            _ui.IntegerAnswer = 3;
            OnDispatcher(() => {
                Program.GameEngine.ScriptEngine.ExecuteOnGroup("drawMany", Pile("Deck"), new Point(0, 0));
                return 0;
            });
            Until("three more cards are in the hand", () => Pile("Hand").Count == 4);
            Assert.That(_ui.Events, Has.Some.EqualTo("AskInteger Draw how many cards?"));
        }

        [Test, Order(5)]
        public void CardsMoveToTheTableAndFlip() {
            var id = OnDispatcher(() => {
                var card = Pile("Hand").Cards.First();
                card.MoveToTable(120, 80, true, Program.GameEngine.Table.Count, false);
                return card.Id;
            });
            Until("the card is on the table", () => Program.GameEngine.Table.Cards.Any(c => c.Id == id));
            OnDispatcher(() => {
                var card = Card.Find(id);
                Assert.AreEqual(120, card.X);
                Assert.AreEqual(80, card.Y);
                Assert.IsTrue(card.FaceUp);
                // A batchExecute action of the definition: the function gets the list of cards.
                Program.GameEngine.ScriptEngine.ExecuteOnBatch("flip", new[] { card });
                return 0;
            });
            Until("the script flipped the card", () => !Card.Find(id).FaceUp);
            Until("the flip is in the chat", () => Texts().Any(t => t.StartsWith("Alice flips") && t.EndsWith("face down.")));
        }

        [Test, Order(6)]
        public void RandomNumbersComeFromTheServer() {
            OnDispatcher(() => {
                Program.GameEngine.ScriptEngine.ExecuteOnGroup("rollDice", Program.GameEngine.Table, new Point(0, 0));
                return 0;
            });
            Until("the die roll is in the chat", () => Texts().Any(t => t.StartsWith("Alice rolls ") && t.EndsWith(" on a 6-sided die.")));
            var roll = Texts().First(t => t.StartsWith("Alice rolls "));
            var value = int.Parse(roll.Split(' ')[2]);
            Assert.That(value, Is.InRange(1, 6));
        }

        [Test, Order(7)]
        public void ShufflingWaitsForTheServer() {
            OnDispatcher(() => {
                Program.GameEngine.ScriptEngine.ExecuteOnGroup("shuffle", Pile("Deck"), new Point(0, 0));
                return 0;
            });
            Until("the shuffle is in the chat", () => Texts().Any(t => t == "Alice shuffles their Deck."));
            Until("the deck still has its cards", () => Pile("Deck").Count == 6 && !Pile("Deck").Locked);
        }

        [Test, Order(8)]
        public void LookAtAPileAsksTheUserInterface() {
            OnDispatcher(() => {
                Program.GameEngine.ScriptEngine.ExecuteFunction("_api.GroupLookAt", Pile("Deck").Id, 2, true);
                return 0;
            });
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline && !_ui.Events.Contains("ShowGroup Alice's Deck Top 2")) Thread.Sleep(50);
            Assert.That(_ui.Events, Has.Some.EqualTo("ShowGroup Alice's Deck Top 2"));
        }
    }
}

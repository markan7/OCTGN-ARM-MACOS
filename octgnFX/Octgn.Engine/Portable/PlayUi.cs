// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using Octgn.DataNew.Entities;
using Octgn.Play.Gui;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;
using Player = Octgn.Play.Player;

namespace Octgn.Portable
{
    /// <summary>
    /// What the game engine asks of the user interface in the .NET 10 build, where the WPF engine opens its
    /// own windows (PlayWindow, GroupWindow, the script dialogs of Octgn.Scripting.Controls, tooltips, sounds).
    /// The Avalonia client implements it; <see cref="HeadlessPlayUi"/> answers without a user.
    /// <para>
    /// Every member is called on the game dispatcher thread (<see cref="Program.Dispatcher"/>). The questions
    /// return only once answered, as WPF's modal dialogs do: a script waits for them, with the game dispatcher
    /// inside the call (the user interface runs its event loop meanwhile, as ShowDialog does in WPF).
    /// The answers follow those of the WPF dialogs (see each member).
    /// </para>
    /// </summary>
    public interface IPlayUi
    {
        /// <summary>The pre-game lobby closes: the game starts (PreGameLobby.Start in WPF).</summary>
        void StartGame();

        /// <summary>Shows the cards of a pile: all of them, or the top or bottom <paramref name="count"/> (GroupWindow).</summary>
        void ShowGroup(Group group, PilePosition position, int count);

        /// <summary>A short error for the local player, next to the pointer in WPF (Tooltip.PopupError).</summary>
        void ShowError(string message);

        /// <summary>Opens a web page, after the user agreed where needed (Program.LaunchUrl).</summary>
        void LaunchUrl(string url);

        /// <summary>Plays a sound file of the client (a player joins or leaves the pre-game lobby).</summary>
        void PlaySound(string file);

        /// <summary>Plays a sound of the game definition (playSound in scripts, or another player's).</summary>
        void PlayGameSound(GameSound sound);

        /// <summary>A limited game starts: the local player opens <paramref name="packs"/> and builds a deck (PickCardsDialog).</summary>
        void StartLimited(Guid[] packs);

        /// <summary>More packs for the local player's pool in the limited game started with <see cref="StartLimited"/>.</summary>
        void AddPacks(Guid[] packs);

        /// <summary>
        /// Another player asks to look at the local player's protected pile. <paramref name="answer"/> takes
        /// (granted, permanent, answered): answered false when the user closed the question without answering.
        /// It may be called later, from the game dispatcher.
        /// </summary>
        void AskPileViewPermission(Group pile, Player requester, string viewDescription, Action<bool, bool, bool> answer);

        // Questions of the scripts (Octgn.Scripting.Controls in WPF).

        /// <summary>Yes or no (Dialog.Confirm): true for yes.</summary>
        bool Confirm(string message);

        /// <summary>A text (InputDlg.GetString): null when cancelled.</summary>
        string AskString(string title, string question, string defaultValue);

        /// <summary>An integer of 0 or more (InputDlg.GetPositiveInt): null when cancelled.</summary>
        int? AskInteger(string title, string question, int defaultValue);

        /// <summary>
        /// One of <paramref name="choices"/> or <paramref name="buttons"/> (ChoiceDlg): the number of the
        /// choice from 1, of the button from -1 down, or 0 when closed without an answer.
        /// <paramref name="colors"/> holds a color per choice ("#RRGGBB", or "None").
        /// </summary>
        int AskChoice(string title, string question, IList<string> choices, IList<string> colors, IList<string> buttons);

        /// <summary>A marker of the game and a quantity (MarkerDlg), or null when cancelled.</summary>
        Tuple<GameMarker, int> AskMarker();

        /// <summary>
        /// A card of the game and a quantity (CardDlg), among the cards whose properties match
        /// <paramref name="properties"/> (all of them for <paramref name="op"/> "and", any of them otherwise;
        /// no filter when empty), or null when cancelled.
        /// </summary>
        Tuple<DataNew.Entities.Card, int> AskCard(Dictionary<string, List<string>> properties, string op, string title);

        /// <summary>One of <paramref name="cards"/> (SelectCardsDlg): its index, or null when cancelled.</summary>
        int? SelectCard(IList<Card> cards, string question, string title);

        /// <summary>
        /// Cards among the ones of <paramref name="cards"/> (and of <paramref name="cards2"/>, a second box the
        /// user can move cards to and from) (SelectMultiCardsDlg), or null when cancelled.
        /// <paramref name="min"/> and <paramref name="max"/> bound the number of selected cards (no bound when null).
        /// </summary>
        SelectMultiCardsResult SelectMultiCards(IList<int> cards, IList<int> cards2, int? min, int? max, string question, string title, string boxLabel, string boxLabel2);

        /// <summary>A pack of the game (PackDlg), or null when cancelled.</summary>
        Pack AskPack();

        /// <summary>A file to open (OpenFileDialog), or "" when cancelled. <paramref name="filter"/> as in WinForms ("Text|*.txt").</summary>
        string OpenFile(string title, string defaultValue, string filter);

        /// <summary>A file to save (SaveFileDialog), or "" when cancelled.</summary>
        string SaveFile(string title, string defaultValue, string filter);
    }

    /// <summary>
    /// The answer of <see cref="IPlayUi.SelectMultiCards"/>: the members the game scripts read
    /// (those of WPF's SelectMultiCardsDlg, hence their names).
    /// </summary>
    public sealed class SelectMultiCardsResult
    {
        /// <summary>Ids of the cards of the first box, in their new order.</summary>
        public List<int> allCards;

        /// <summary>Ids of the cards of the second box, in their new order, or null without a second box.</summary>
        public List<int> allCards2;

        /// <summary>Ids of the selected cards.</summary>
        public List<int> selectedCards { get; set; }
    }

    /// <summary>
    /// A user interface without user, for tests and programs without interface: the table is "shown" as
    /// soon as the game starts (<see cref="GameSession.StartTable"/>), questions get their default answer
    /// (yes for <see cref="Confirm"/>), file and card dialogs are cancelled, the rest is recorded in
    /// <see cref="Events"/>. Derive from it to answer otherwise.
    /// </summary>
    public class HeadlessPlayUi : IPlayUi
    {
        private readonly List<string> _events = new List<string>();

        /// <summary>What happened, in order ("ShowGroup Deck Top 3", "Confirm ...").</summary>
        public IReadOnlyList<string> Events {
            get { lock (_events) return _events.ToList(); }
        }

        protected void Record(string text) {
            lock (_events) _events.Add(text);
        }

        public virtual void StartGame() {
            Record("StartGame");
            GameSession.StartTable();
        }

        public virtual void ShowGroup(Group group, PilePosition position, int count) => Record($"ShowGroup {group.FullName} {position} {count}");

        public virtual void ShowError(string message) => Record("Error " + message);

        public virtual void LaunchUrl(string url) => Record("LaunchUrl " + url);

        public virtual void PlaySound(string file) => Record("PlaySound " + System.IO.Path.GetFileName(file));

        public virtual void PlayGameSound(GameSound sound) => Record("PlayGameSound " + sound.Name);

        public virtual void StartLimited(Guid[] packs) => Record("StartLimited " + packs.Length);

        public virtual void AddPacks(Guid[] packs) => Record("AddPacks " + packs.Length);

        public virtual void AskPileViewPermission(Group pile, Player requester, string viewDescription, Action<bool, bool, bool> answer) {
            Record($"AskPileViewPermission {pile.FullName} {requester.Name}");
            answer(false, false, true);
        }

        public virtual bool Confirm(string message) {
            Record("Confirm " + message);
            return true;
        }

        public virtual string AskString(string title, string question, string defaultValue) {
            Record("AskString " + question);
            return defaultValue;
        }

        public virtual int? AskInteger(string title, string question, int defaultValue) {
            Record("AskInteger " + question);
            return defaultValue;
        }

        public virtual int AskChoice(string title, string question, IList<string> choices, IList<string> colors, IList<string> buttons) {
            Record("AskChoice " + question);
            return choices.Count > 0 ? 1 : 0;
        }

        public virtual Tuple<GameMarker, int> AskMarker() {
            Record("AskMarker");
            return null;
        }

        public virtual Tuple<DataNew.Entities.Card, int> AskCard(Dictionary<string, List<string>> properties, string op, string title) {
            Record("AskCard " + title);
            return null;
        }

        public virtual int? SelectCard(IList<Card> cards, string question, string title) {
            Record("SelectCard " + question);
            return null;
        }

        public virtual SelectMultiCardsResult SelectMultiCards(IList<int> cards, IList<int> cards2, int? min, int? max, string question, string title, string boxLabel, string boxLabel2) {
            Record("SelectMultiCards " + question);
            return null;
        }

        public virtual Pack AskPack() {
            Record("AskPack");
            return null;
        }

        public virtual string OpenFile(string title, string defaultValue, string filter) {
            Record("OpenFile " + title);
            return string.Empty;
        }

        public virtual string SaveFile(string title, string defaultValue, string filter) {
            Record("SaveFile " + title);
            return string.Empty;
        }
    }
}

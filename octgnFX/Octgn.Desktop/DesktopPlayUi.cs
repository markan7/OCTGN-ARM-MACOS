// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using log4net;
using Octgn.DataNew.Entities;
using Octgn.Play.Gui;
using Octgn.Portable;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop
{
    /// <summary>
    /// The user interface the engine talks to (Program.Ui): the play window and the questions of the scripts.
    /// </summary>
    public sealed class DesktopPlayUi : IPlayUi
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(DesktopPlayUi));
        private readonly PlayWindow _window;

        public DesktopPlayUi(PlayWindow window) {
            _window = window;
        }

        public void StartGame() => _window.OnGameStarted();

        public void ShowGroup(Group group, PilePosition position, int count) {
            new GroupWindow(group, position, count, _window.DragLayer).Show(_window);
        }

        public void ShowError(string message) => _window.ShowNotice(message);

        public void LaunchUrl(string url) {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                _ = _window.Launcher.LaunchUriAsync(uri);
        }

        // Sounds: not played yet in this version (Windows plays them with NAudio).
        public void PlaySound(string file) => Log.Debug("Sound " + file);

        public void PlayGameSound(GameSound sound) => Log.Debug("Game sound " + sound.Name);

        public void StartLimited(Guid[] packs) =>
            _window.ShowNotice("A limited game (sealed, draft) started: this version of the table cannot open packs yet.");

        public void AddPacks(Guid[] packs) =>
            _window.ShowNotice("Packs were added to the limited game: this version of the table cannot open packs yet.");

        public void AskPileViewPermission(Group pile, Player requester, string viewDescription, Action<bool, bool, bool> answer) {
            var result = Dialogs.AskPileViewPermission(_window, requester.Name, viewDescription, pile.FullName);
            if (result == null) answer(false, false, false);
            else answer(result.Item1, result.Item2, true);
        }

        public bool Confirm(string message) => Dialogs.Confirm(_window, message);

        public string AskString(string title, string question, string defaultValue) => Dialogs.AskText(_window, title, question, defaultValue);

        public int? AskInteger(string title, string question, int defaultValue) {
            var text = Dialogs.AskText(_window, title, question, defaultValue.ToString(), positiveInteger: true);
            return text == null ? (int?)null : int.Parse(text);
        }

        public int AskChoice(string title, string question, IList<string> choices, IList<string> colors, IList<string> buttons) =>
            Dialogs.AskChoice(_window, title, question, choices, colors, buttons);

        public Tuple<GameMarker, int> AskMarker() => Dialogs.AskMarker(_window);

        public Tuple<DataNew.Entities.Card, int> AskCard(Dictionary<string, List<string>> properties, string op, string title) =>
            Dialogs.AskCard(_window, properties, op, title);

        public int? SelectCard(IList<Card> cards, string question, string title) => Dialogs.SelectCard(_window, cards, question, title);

        public SelectMultiCardsResult SelectMultiCards(IList<int> cards, IList<int> cards2, int? min, int? max, string question, string title, string boxLabel, string boxLabel2) =>
            Dialogs.SelectMultiCards(_window, cards, cards2, min, max, question, title);

        public Pack AskPack() => Dialogs.AskPack(_window);

        public string OpenFile(string title, string defaultValue, string filter) {
            var files = Wait(_window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = FileTypes(filter),
            }));
            return files?.FirstOrDefault()?.TryGetLocalPath() ?? string.Empty;
        }

        public string SaveFile(string title, string defaultValue, string filter) {
            var file = Wait(_window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = title,
                SuggestedFileName = defaultValue,
                FileTypeChoices = FileTypes(filter),
            }));
            return file?.TryGetLocalPath() ?? string.Empty;
        }

        /// <summary>Waits for a dialog of the system while the user interface runs (the script waits for it).</summary>
        private static T Wait<T>(Task<T> task) where T : class {
            using (var done = new CancellationTokenSource()) {
                task.ContinueWith(_ => done.Cancel(), TaskScheduler.Default);
                GameDispatcher.RunUntil(done.Token);
            }
            try {
                return task.GetAwaiter().GetResult();
            } catch (Exception ex) {
                Log.Warn("File dialog", ex);
                return null;
            }
        }

        /// <summary>A WinForms filter ("Text files|*.txt|All files|*.*") as Avalonia's file types.</summary>
        private static List<FilePickerFileType> FileTypes(string filter) {
            var types = new List<FilePickerFileType>();
            if (string.IsNullOrWhiteSpace(filter)) return types;
            var parts = filter.Split('|');
            for (var i = 0; i + 1 < parts.Length; i += 2)
                types.Add(new FilePickerFileType(parts[i].Trim()) { Patterns = parts[i + 1].Split(';').Select(p => p.Trim()).ToList() });
            return types;
        }
    }
}

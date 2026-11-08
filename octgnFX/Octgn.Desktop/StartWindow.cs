// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using log4net;
using Octgn.DataNew.Entities;
using Octgn.Online.Hosting;
using Octgn.Portable;

namespace Octgn.Desktop
{
    /// <summary>
    /// The first window: the games of the game database (add one from its folder), a nickname, and a game to
    /// host on this computer or to join at an address. It opens the play window.
    /// </summary>
    public sealed class StartWindow : Window
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(StartWindow));
        private readonly ListBox _games = new ListBox { Height = 260 };
        private readonly TextBox _nickname = new TextBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBox _address = new TextBox { PlaceholderText = "address:port", Width = 200 };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
        private readonly Button _host = new Button { Content = "Host a game on this computer" };
        private readonly Button _join = new Button { Content = "Join" };

        public StartWindow() {
            Title = "OCTGN";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            CanResize = false;

            var add = new Button { Content = "Add a game from its folder…" };
            add.Click += async (s, e) => await AddGameFromPicker();
            _host.Classes.Add("accent");
            _host.Click += async (s, e) => await Host(SelectedGame, null);
            _join.Click += async (s, e) => await JoinAddress();
            _games.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Game>((game, _) =>
                new TextBlock { Text = game == null ? "" : $"{game.Name}  ({game.Version})", Margin = new Thickness(4, 2) });
            _games.DoubleTapped += async (s, e) => await Host(SelectedGame, null);
            _nickname.Text = string.IsNullOrWhiteSpace(Core.Prefs.Nickname) ? Environment.UserName : Core.Prefs.Nickname;

            var joinRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            joinRow.Children.Add(_address);
            joinRow.Children.Add(_join);

            var body = new StackPanel { Margin = new Thickness(22), Spacing = 10 };
            body.Children.Add(new TextBlock { Text = "OCTGN", FontSize = 28, FontWeight = FontWeight.Bold });
            body.Children.Add(new TextBlock { Text = "Games", FontWeight = FontWeight.SemiBold });
            body.Children.Add(_games);
            body.Children.Add(add);
            body.Children.Add(new TextBlock { Text = "Nickname", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            body.Children.Add(_nickname);
            body.Children.Add(_host);
            body.Children.Add(new TextBlock { Text = "Or join the game of another computer (its address and port):", Margin = new Thickness(0, 8, 0, 0) });
            body.Children.Add(joinRow);
            body.Children.Add(_status);
            Content = body;

            RefreshGames();
        }

        private Game SelectedGame => _games.SelectedItem as Game;

        private void RefreshGames(Guid? select = null) {
            try {
                var games = GameSession.InstalledGames();
                _games.ItemsSource = games;
                _games.SelectedItem = games.FirstOrDefault(g => g.Id == select) ?? games.FirstOrDefault();
                if (games.Count == 0) _status.Text = "No game yet: add one from its folder (the folder of its definition.xml).";
            } catch (Exception ex) {
                Log.Error("Games", ex);
                _status.Text = "The game database could not be read: " + ex.Message;
            }
        }

        private async Task AddGameFromPicker() {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "The folder of the game (with its definition.xml)" });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) AddGame(path);
        }

        public Game AddGame(string folder) {
            try {
                var game = GameSession.InstallGame(folder);
                RefreshGames(game.Id);
                _status.Text = $"{game.Name} added.";
                return game;
            } catch (Exception ex) {
                Log.Warn("Add game " + folder, ex);
                _status.Text = "This folder holds no game OCTGN can read: " + ex.Message;
                return null;
            }
        }

        private string Nickname => string.IsNullOrWhiteSpace(_nickname.Text) ? Environment.UserName : _nickname.Text.Trim();

        /// <summary>Hosts a game on this computer and opens its play window (returned, or null on failure).</summary>
        public async Task<PlayWindow> Host(Game game, string deck) {
            if (game == null) {
                _status.Text = "Choose a game first.";
                return null;
            }
            return await Open(() => GameSession.HostLocalGame(game, Nickname), deck);
        }

        private async Task JoinAddress() {
            var game = SelectedGame;
            if (game == null) {
                _status.Text = "Choose the game first (the other computer plays it too).";
                return;
            }
            var address = _address.Text?.Trim();
            if (string.IsNullOrEmpty(address) || !address.Contains(':')) {
                _status.Text = "Type the address and the port of the game, as address:port.";
                return;
            }
            var hosted = new HostedGame {
                Id = Guid.NewGuid(),
                Name = address,
                GameId = game.Id,
                GameName = game.Name,
                GameVersion = game.Version.ToString(),
                HostAddress = address,
                Source = HostedGameSource.Lan,
            };
            await Open(() => GameSession.Join(game, hosted, Nickname, isHost: false, spectator: false), null);
        }

        private async Task<PlayWindow> Open(Func<Task<GameSession>> start, string deck) {
            _host.IsEnabled = _join.IsEnabled = false;
            _status.Text = "Starting…";
            // Until the play window exists: a game that starts meanwhile waits for it.
            var pending = new PendingPlayUi();
            Program.Ui = pending;
            try {
                var session = await start();
                var window = new PlayWindow(session) { DeckToLoad = deck };
                Program.Ui = new DesktopPlayUi(window);
                window.Closed += (s, e) => { Show(); _host.IsEnabled = _join.IsEnabled = true; _status.Text = ""; };
                window.Show();
                Hide();
                if (pending.Started) window.OnGameStarted();
                return window;
            } catch (Exception ex) {
                Log.Error("Start a game", ex);
                _status.Text = "The game could not start: " + ex.Message;
                _host.IsEnabled = _join.IsEnabled = true;
                return null;
            }
        }

        private sealed class PendingPlayUi : HeadlessPlayUi
        {
            public bool Started { get; private set; }

            public override void StartGame() => Started = true;
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using log4net;
using Octgn.Core.Play;
using Octgn.DataNew.Entities;
using Octgn.Play;
using Octgn.Portable;
using DispatcherTimer = Avalonia.Threading.DispatcherTimer;
using Card = Octgn.Play.Card;
using Group = Octgn.Play.Group;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop
{
    /// <summary>
    /// The game window (PlayWindow in WPF): the table in the middle, the local player's piles and hand
    /// below, the card preview and the chat on the right, the other players above. Before the game starts,
    /// the pre-game lobby covers the table.
    /// </summary>
    public sealed class PlayWindow : Window
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PlayWindow));

        private readonly GameSession _session;
        private readonly ObservableCollection<Control> _chatLines = new ObservableCollection<Control>();
        private readonly ListBox _chat;
        private readonly TextBox _chatInput = new TextBox { PlaceholderText = "Message (Enter to send)" };
        private readonly Image _preview = new Image { Stretch = Stretch.Uniform, Height = 360 };
        private readonly TextBlock _previewName = new TextBlock { FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        private readonly Grid _playerArea = new Grid { Margin = new Thickness(6), Height = 150 };
        private readonly StackPanel _piles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        private readonly StackPanel _opponents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(10, 4) };
        private readonly TextBlock _turn = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        private readonly Border _lobby;
        private readonly TextBlock _lobbyPlayers = new TextBlock { TextAlignment = TextAlignment.Center, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        private readonly Button _start = new Button { Content = "Start the game", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 15, Padding = new Thickness(18, 8) };
        private readonly DispatcherTimer _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private bool _started;

        public PlayWindow(GameSession session) {
            _session = session;
            var engine = Program.GameEngine;
            Title = $"OCTGN – {engine.Definition.Name}";
            Width = 1440;
            Height = 900;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            DragLayer = new DragLayer();
            Table = new TableView(engine.Table, DragLayer);
            Table.HoveredChanged += ShowPreview;

            _chat = new ListBox { ItemsSource = _chatLines, Background = Brushes.Transparent };
            _chatInput.KeyDown += ChatKeyDown;

            // Toolbar
            var loadDeck = new Button { Content = "Load a deck…" };
            loadDeck.Click += async (s, e) => await LoadDeckFromPicker();
            var zoomOut = new Button { Content = "−", Width = 34 };
            zoomOut.Click += (s, e) => Table.ZoomBy(1 / 1.25);
            var fit = new Button { Content = "Fit" };
            fit.Click += (s, e) => Table.FitView();
            var zoomIn = new Button { Content = "+", Width = 34 };
            zoomIn.Click += (s, e) => Table.ZoomBy(1.25);
            var title = new TextBlock { Text = engine.Definition.Name, FontWeight = FontWeight.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 6) };
            toolbar.Children.Add(title);
            toolbar.Children.Add(loadDeck);
            toolbar.Children.Add(new Border { Width = 12 });
            toolbar.Children.Add(zoomOut);
            toolbar.Children.Add(fit);
            toolbar.Children.Add(zoomIn);
            toolbar.Children.Add(_turn);

            // Right: preview and chat
            var sidebar = new Grid { Width = 320, RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(6) };
            Grid.SetRow(_preview, 0);
            Grid.SetRow(_previewName, 1);
            var chatBorder = new Border { Child = _chat, BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xff, 0xff, 0xff)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 6) };
            Grid.SetRow(chatBorder, 2);
            Grid.SetRow(_chatInput, 3);
            sidebar.Children.Add(_preview);
            sidebar.Children.Add(_previewName);
            sidebar.Children.Add(chatBorder);
            sidebar.Children.Add(_chatInput);

            // Pre-game lobby over the table
            _start.Classes.Add("accent");
            _start.Click += (s, e) => StartGame();
            var lobbyContent = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 460 };
            lobbyContent.Children.Add(new TextBlock { Text = "Pre-game lobby", FontSize = 24, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center });
            lobbyContent.Children.Add(_lobbyPlayers);
            lobbyContent.Children.Add(_start);
            _lobby = new Border { Background = new SolidColorBrush(Color.FromArgb(0xc0, 0x10, 0x10, 0x14)), Child = lobbyContent };

            var tableHost = new Panel();
            tableHost.Children.Add(Table);
            tableHost.Children.Add(_lobby);

            var main = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            main.Children.Add(toolbar);
            DockPanel.SetDock(sidebar, Dock.Right);
            main.Children.Add(sidebar);
            var bottom = new Border { Child = _playerArea, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1c)) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            main.Children.Add(bottom);
            var opponentsBar = new Border { Child = _opponents, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1c)) };
            DockPanel.SetDock(opponentsBar, Dock.Top);
            main.Children.Add(opponentsBar);
            main.Children.Add(tableHost);

            var root = new Panel();
            root.Children.Add(main);
            root.Children.Add(DragLayer);
            Content = root;

            DragLayer.Register(Table);
            BuildPlayerArea();

            Program.GameMess.OnMessage += OnGameMessage;
            foreach (var message in Program.GameMess.Messages.ToList()) AddMessage(message);

            _refresh.Tick += (s, e) => RefreshStatus();
            _refresh.Start();
            RefreshStatus();
            KeyDown += OnKeyDown;
        }

        public DragLayer DragLayer { get; }

        public TableView Table { get; }

        /// <summary>The views of the local player's groups, in the player area.</summary>
        public System.Collections.Generic.IEnumerable<GroupView> PlayerGroupViews =>
            _playerArea.GetLogicalDescendants().OfType<GroupView>();

        /// <summary>The deck to load once the table is ready (command line).</summary>
        public string DeckToLoad { get; set; }

        /// <summary>The table is ready: the game state is loaded (TableLoaded) and decks can be loaded.</summary>
        public event Action TableReady;

        // Game start

        private void StartGame() {
            try {
                _start.IsEnabled = false;
                _session.StartGame();
            } catch (Exception ex) {
                Log.Error("Start", ex);
                ShowNotice("The game could not start: " + ex.Message);
                _start.IsEnabled = true;
            }
        }

        /// <summary>The server started the game (IPlayUi.StartGame): the table replaces the lobby.</summary>
        public void OnGameStarted() {
            if (_started) return;
            _started = true;
            _lobby.IsVisible = false;
            Table.Focus();
            GameSession.StartTable();
            WaitForTable();
        }

        private async void WaitForTable() {
            while (Program.GameEngine != null && !Program.GameEngine.TableLoaded) await Task.Delay(100);
            if (Program.GameEngine == null) return;
            if (DeckToLoad != null) LoadDeck(DeckToLoad);
            TableReady?.Invoke();
        }

        public void LoadDeck(string path) {
            try {
                GameSession.LoadDeck(path);
            } catch (Exception ex) {
                Log.Warn("Load deck " + path, ex);
                ShowNotice($"The deck {System.IO.Path.GetFileName(path)} could not be loaded: {ex.Message}");
            }
        }

        private async Task LoadDeckFromPicker() {
            if (!Program.GameEngine.TableLoaded) {
                ShowNotice("Decks are loaded once the game has started.");
                return;
            }
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Load a deck",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("OCTGN decks") { Patterns = new[] { "*.o8d" } } },
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) LoadDeck(path);
        }

        // Player area

        /// <summary>
        /// The player area: the counters, the expanded piles (the hand) spread out, then the other piles in a
        /// row that scrolls when the game has many (the local player's, then the shared ones).
        /// </summary>
        private void BuildPlayerArea() {
            _playerArea.Children.Clear();
            _playerArea.ColumnDefinitions.Clear();
            _piles.Children.Clear();
            var player = Player.LocalPlayer;
            var own = player.Groups.OfType<Pile>().ToList();
            var shared = Player.GlobalPlayer?.Groups.OfType<Pile>().ToList() ?? new System.Collections.Generic.List<Pile>();
            var expanded = own.Where(p => p.ViewState == GroupViewState.Expanded).ToList();

            var counters = player.Counters.Length > 0 ? CountersPanel(player) : new Panel();
            var hands = new Grid();
            foreach (var pile in expanded) {
                var view = new FanView(pile, DragLayer) { Margin = new Thickness(4, 0) };
                hands.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                Grid.SetColumn(view, hands.Children.Count);
                hands.Children.Add(view);
                Watch(view);
            }
            foreach (var pile in own.Except(expanded)) _piles.Children.Add(Pile(pile));
            if (shared.Count > 0) {
                _piles.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(Color.FromArgb(0x50, 0xff, 0xff, 0xff)), Margin = new Thickness(8, 10) });
                foreach (var pile in shared) _piles.Children.Add(Pile(pile));
            }
            var piles = new ScrollViewer {
                Content = _piles,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Right,
            };

            _playerArea.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            _playerArea.ColumnDefinitions.Add(new ColumnDefinition(expanded.Count > 0 ? new GridLength(2, GridUnitType.Star) : new GridLength(0)));
            _playerArea.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(expanded.Count > 0 ? 3 : 1, GridUnitType.Star)));
            Grid.SetColumn(counters, 0);
            Grid.SetColumn(hands, 1);
            Grid.SetColumn(piles, 2);
            _playerArea.Children.Add(counters);
            _playerArea.Children.Add(hands);
            _playerArea.Children.Add(piles);
        }

        private PileView Pile(Pile pile) {
            var view = new PileView(pile, DragLayer, pile.ViewState == GroupViewState.Collapsed) { Margin = new Thickness(2, 0), VerticalAlignment = VerticalAlignment.Bottom };
            Watch(view);
            return view;
        }

        private void Watch(GroupView view) {
            view.HoveredChanged += ShowPreview;
            DragLayer.Register(view);
        }

        private Control CountersPanel(Player player) {
            var panel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
            panel.Children.Add(new TextBlock { Text = player.Name, FontWeight = FontWeight.Bold, Foreground = CardPainter.TextBrush(player.ActualColor) });
            foreach (var counter in player.Counters) {
                var value = new TextBlock { MinWidth = 28, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 15 };
                void Show() => value.Text = counter.Value.ToString(CultureInfo.CurrentCulture);
                Show();
                counter.PropertyChanged += (s, e) => Show();
                var minus = new Button { Content = "−", Padding = new Thickness(8, 0) };
                minus.Click += (s, e) => counter.Value -= 1;
                var plus = new Button { Content = "+", Padding = new Thickness(8, 0) };
                plus.Click += (s, e) => counter.Value += 1;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                row.Children.Add(new TextBlock { Text = counter.Name, Width = 60, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(minus);
                row.Children.Add(value);
                row.Children.Add(plus);
                panel.Children.Add(row);
            }
            return panel;
        }

        // Players, turn, lobby

        private void RefreshStatus() {
            var engine = Program.GameEngine;
            if (engine == null) return;
            var players = Player.AllExceptGlobal.ToList();
            _lobbyPlayers.Text = "Players: " + string.Join(", ", players.Select(p => p.Name + (p.Ready ? "" : " (joining)")))
                                 + (_session.IsHost
                                     ? $"\n\nThe other players join at {LocalAddressValue.Value}:{_session.HostedGame.Port}.\nStart the game when everybody is here."
                                     : "\n\nThe host starts the game.");
            _start.IsVisible = _session.IsHost;

            _turn.Text = engine.TurnNumber > 0
                ? $"Turn {engine.TurnNumber}" + (engine.ActivePlayer != null ? $" – {engine.ActivePlayer.Name}" : "") + (engine.CurrentPhase != null ? $" – {engine.CurrentPhase.Name}" : "")
                : "";

            _opponents.Children.Clear();
            foreach (var other in players.Where(p => p != Player.LocalPlayer)) {
                var text = other.Name + ": " + string.Join(", ", other.Groups.Select(g => $"{g.Name} {g.Count}"))
                           + string.Concat(other.Counters.Select(c => $", {c.Name} {c.Value}"));
                _opponents.Children.Add(new TextBlock { Text = text, Foreground = CardPainter.TextBrush(other.ActualColor), FontWeight = FontWeight.SemiBold });
            }
            if (_opponents.Children.Count == 0)
                _opponents.Children.Add(new TextBlock { Text = "No other player yet.", Foreground = Brushes.Gray });
        }

        private static readonly Lazy<string> LocalAddressValue = new Lazy<string>(LocalAddress);

        /// <summary>This computer's address on the local network, for the other players (127.0.0.1 without one).</summary>
        private static string LocalAddress() {
            try {
                var address = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a));
                return address?.ToString() ?? "127.0.0.1";
            } catch (System.Net.Sockets.SocketException) {
                return "127.0.0.1";
            }
        }

        // Preview

        private void ShowPreview(Card card) {
            if (card == null) return;
            var up = CardPainter.IsUp(card);
            _preview.Source = Pictures.ForCard(card, up);
            _previewName.Text = up ? card.RealName : "";
        }

        // Chat

        private void OnGameMessage(IGameMessage message) {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AddMessage(message));
        }

        private void AddMessage(IGameMessage message) {
            if (message is DebugMessage && !Program.DeveloperMode) return;
            IBrush brush;
            switch (message) {
                case WarningMessage _: brush = Brushes.Orange; break;
                case SystemMessage _: brush = Brushes.LightGray; break;
                case DebugMessage _: brush = Brushes.Gray; break;
                case NotifyBarMessage bar: brush = new SolidColorBrush(CardPainter.ToAvalonia(bar.MessageColor)); break;
                default:
                    brush = message.From != null ? CardPainter.TextBrush(message.From.Color) : Brushes.WhiteSmoke;
                    break;
            }
            AddLine(GameMessageText.Format(message), brush);
        }

        private void AddLine(string text, IBrush brush) {
            _chatLines.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = brush, FontSize = 12.5 });
            while (_chatLines.Count > 500) _chatLines.RemoveAt(0);
            _chat.ScrollIntoView(_chatLines[_chatLines.Count - 1]);
        }

        /// <summary>A message for the local player only (errors of the table: Tooltip.PopupError in WPF).</summary>
        public void ShowNotice(string message) => AddLine(message, Brushes.IndianRed);

        /// <summary>The lines of the chat, as shown (tests).</summary>
        public string[] ChatText => _chatLines.OfType<TextBlock>().Select(t => t.Text).ToArray();

        private void ChatKeyDown(object sender, KeyEventArgs e) {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            var text = _chatInput.Text?.Trim();
            if (!string.IsNullOrEmpty(text) && Program.Client != null) Program.Client.Rpc.ChatReq(text);
            _chatInput.Text = "";
            e.Handled = true;
        }

        // Keyboard shortcuts of the game

        private void OnKeyDown(object sender, KeyEventArgs e) {
            if (e.Handled || _chatInput.IsFocused || !_started) return;
            var hovered = Table.Hovered ?? PlayerGroupViews.Select(v => v.Hovered).FirstOrDefault(c => c != null);
            if (GameActions.RunShortcut(e.Key, e.KeyModifiers, hovered, Table.PointerTablePosition)) e.Handled = true;
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            _refresh.Stop();
            Program.GameMess.OnMessage -= OnGameMessage;
            Table.Detach();
            foreach (var view in PlayerGroupViews.ToList()) view.Detach();
            _session.Dispose();
        }
    }
}

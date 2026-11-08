// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using Octgn.Core.DataExtensionMethods;
using Octgn.Core.DataManagers;
using Octgn.Core.Play;
using Octgn.DataNew;
using Octgn.DataNew.Entities;
using Octgn.Library;
using Octgn.Library.Exceptions;
using Octgn.Library.Localization;
using Octgn.Networking;
using Octgn.Online.Hosting;
using Octgn.Play;
using Octgn.Scripting;
using Card = Octgn.Play.Card;
using Player = Octgn.Play.Player;

namespace Octgn.Portable
{
    /// <summary>
    /// A game of this client, from the user interface's side: what the WPF engine's loaders, launchers
    /// (JoinGameLauncher) and PlayWindow do to start the engine, without WPF. The engine itself is the
    /// shared one: <see cref="Program.GameEngine"/>, <see cref="Program.Client"/>, the models of Octgn.Play.
    /// <para>
    /// The user interface gives the game dispatcher (its UI thread, <see cref="Program.Dispatcher"/>) and
    /// its <see cref="IPlayUi"/>, joins or hosts a game, then, once the game starts
    /// (<see cref="IPlayUi.StartGame"/>), calls <see cref="StartTable"/>.
    /// </para>
    /// </summary>
    public sealed class GameSession : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object InitLock = new object();
        private static bool _initialized;

        /// <summary>The script API versions of the WPF engine (Loaders/VersionedLoader.cs, from Scripting/ApiVersions.xml).</summary>
        private static readonly (string Version, string Released)[] ApiVersions = {
            ("3.1.0.0", "2014-1-12"),
            ("3.1.0.1", "2014-1-22"),
            ("3.1.0.2", "2015-8-26"),
        };

        /// <summary>The port of the LAN broadcast of the games (GameBroadcaster and GameBroadcastListener).</summary>
        public const int DefaultBroadcastPort = 21234;

        private HostedGameProcess _server;

        private GameSession(HostedGame hosted, bool isHost) {
            HostedGame = hosted;
            IsHost = isHost;
        }

        /// <summary>The game being played, as the server announces it.</summary>
        public HostedGame HostedGame { get; }

        public bool IsHost { get; }

        /// <summary>The process of the local game server, when this client hosts it (null otherwise).</summary>
        public int? ServerProcessId => IsHost ? HostedGame.ProcessId : (int?)null;

        /// <summary>
        /// What the WPF engine's loaders do once per process: the OCTGN data folder (configuration, game
        /// database), the game messages, and the script API versions. <paramref name="dataDirectory"/>
        /// replaces the user's data folder, also after a first call (tests).
        /// </summary>
        public static void Initialize(string dataDirectory = null) {
            lock (InitLock) {
                if (dataDirectory != null) {
                    Environment.SetEnvironmentVariable(Library.Providers.DataDirectoryProvider.OCTGNDATA_ENVIRONMENTALVARIABLE, dataDirectory);
                    Config.Instance = new Config();
                    DbContext.SetContext(new DbContext());
                }
                if (_initialized) return;
                if (Config.Instance == null) Config.Instance = new Config();

                // GameMessageLoader: cards in messages keep the name they have when the message is written.
                GameMessage.MuteChecker = () => Program.Client != null && Program.Client.Muted != 0;
                var messages = new GameMessageDispatcher();
                messages.ProcessMessage(DescribeArguments);
                Program.GameMess = messages;

                // VersionedLoader.
                Versioned.Setup(Program.DeveloperMode);
                foreach (var (version, released) in ApiVersions) {
                    Versioned.RegisterVersion(Version.Parse(version), DateTime.Parse(released, System.Globalization.CultureInfo.InvariantCulture), ReleaseMode.Live);
                    Versioned.RegisterFile("PythonApi", PackUri.ApplicationPrefix + "Scripting/Versions/" + version + ".py", Version.Parse(version));
                }
                Versioned.Register<ScriptApi>();
                _initialized = true;
            }
        }

        private static IGameMessage DescribeArguments(IGameMessage message) {
            var args = message.Arguments;
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                var cardModel = arg as DataNew.Entities.Card;
                var cardId = arg as CardIdentity;
                var card = arg as Card;
                if (card != null && (card.FaceUp || card.MayBeConsideredFaceUp))
                    cardId = card.Type;
                if (cardId != null || cardModel != null)
                    args[i] = new ChatCard(cardId != null ? cardId.Model : cardModel, card);
                else
                    args[i] = arg == null ? "[?]" : arg.ToString();
            }
            return message;
        }

        /// <summary>
        /// Puts a game in the game database of the data folder, where the engine looks for it, and returns it.
        /// The game is a folder with its definition.xml and its Sets folder (or that definition.xml), as in the
        /// game's repository, or its package: an .o8g file, or the .nupkg of the OCTGN game feed (a NuGet
        /// package with the game in its def folder). Both are installed as the WPF client's game manager
        /// installs a package (a folder as o8build would package it): a newer version replaces the old one,
        /// whose files go.
        /// </summary>
        public static Game InstallGame(string path) {
            Initialize();
            if (File.Exists(path) && IsGamePackage(path)) return InstallGamePackage(path);
            var definition = Directory.Exists(path) ? Path.Combine(path, "definition.xml") : path;
            var source = Path.GetDirectoryName(Path.GetFullPath(definition));
            var game = (Game)new GameSerializer().Deserialize(definition);
            var target = Path.Combine(Config.Instance.Paths.DatabasePath, game.Id.ToString());
            if (!string.Equals(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar), source.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)) {
                GameManager.Get().InstallGame(new FolderGamePackage(source, game));
                AfterInstall(game.Id);
            }
            return Reload(game.Id)
                ?? throw new InvalidOperationException($"The game {game.Name} ({game.Id}) is not in the game database after its installation.");
        }

        /// <summary>A game package: .o8g, or .nupkg as the OCTGN game feed serves it.</summary>
        public static bool IsGamePackage(string path) {
            var extension = Path.GetExtension(path);
            return extension.Equals(".o8g", StringComparison.OrdinalIgnoreCase) || extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A package of card images (.o8c).</summary>
        public static bool IsImagePack(string path) => Path.GetExtension(path).Equals(".o8c", StringComparison.OrdinalIgnoreCase);

        private static Game InstallGamePackage(string path) {
            var package = Core.Packaging.GamePackage.FromFile(path);
            if (!Guid.TryParse(package.Id, out var id))
                throw new InvalidOperationException($"{Path.GetFileName(path)} is not a game package: its id ({package.Id}) is not the id of a game.");
            GameManager.Get().InstallGame(package);
            if (Reload(id) == null)
                throw new InvalidOperationException($"{Path.GetFileName(path)} holds no game (a def folder with its definition.xml).");
            AfterInstall(id);
            return Reload(id);
        }

        /// <summary>
        /// What the WPF client does after an installation, at its next start (UpdateChecker): the card images
        /// that come with the sets of the game (Sets/set id/Cards) go to the image database, where the engine
        /// looks for the pictures. And the set folders an old version leaves empty go, as the game database
        /// would read them as sets.
        /// </summary>
        private static void AfterInstall(Guid id) {
            var game = Reload(id);
            if (game == null) return;
            var sets = Path.Combine(game.InstallPath, "Sets");
            if (Directory.Exists(sets)) {
                foreach (var folder in Directory.GetDirectories(sets)) {
                    if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any()) continue;
                    try {
                        Directory.Delete(folder, true);
                    } catch (IOException ex) {
                        Log.Warn("Remove the empty set folder " + folder, ex);
                    }
                }
                game = Reload(id);
            }
            foreach (var set in game.Sets()) {
                if (!Directory.Exists(set.PackUri)) continue;
                foreach (var file in Directory.GetFiles(set.PackUri)) {
                    Directory.CreateDirectory(set.ImagePackUri);
                    File.Copy(file, Path.Combine(set.ImagePackUri, Path.GetFileName(file)), true);
                }
            }
        }

        /// <summary>
        /// Installs a package of card images (.o8c: game id/Sets/set id/Cards/card id.jpg) of an installed game,
        /// where the engine looks for the pictures of the cards, and returns that game.
        /// </summary>
        public static Game InstallImagePack(string path) {
            Initialize();
            Guid? id = null;
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path)) {
                foreach (var entry in zip.Entries) {
                    if (Guid.TryParse(entry.FullName.Split('/', '\\')[0], out var gameId)) {
                        id = gameId;
                        break;
                    }
                }
            }
            if (id == null) throw new InvalidOperationException($"{Path.GetFileName(path)} is not a package of card images.");
            var game = GameManager.Get().GetById(id.Value)
                ?? throw new InvalidOperationException($"{Path.GetFileName(path)} holds card images of a game that is not installed ({id}): install the game first.");
            GameManager.Get().Installo8c(path);
            return game;
        }

        /// <summary>
        /// The game as its files are now. The game database keeps the games it read, and only forgets one when
        /// a file watcher tells it, a moment after a change: an update would still read the old version.
        /// </summary>
        private static Game Reload(Guid id) {
            if (DbContext.Get() is IDbContextCaching cache) cache.Invalidate(new Game { Id = id });
            return GameManager.Get().GetById(id);
        }

        /// <summary>
        /// The deck could not be loaded because one of its cards is not in the game: a deck of a newer version
        /// of the game (a pack the installed version does not have), as decks of MarvelCDB and other sites can be.
        /// </summary>
        public static bool IsMissingCard(Exception ex) {
            if (!(ex is UserMessageException)) return false;
            var pattern = "^" + System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Escape(L.D.Exception__CanNotLoadDeckCardNotInstalled_Format), @"\\\{\d+}", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(ex.Message, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);
        }

        /// <summary>The games of the game database.</summary>
        public static IReadOnlyList<Game> InstalledGames() {
            Initialize();
            return GameManager.Get().Games.OrderBy(g => g.Name).ToList();
        }

        /// <summary>
        /// Hosts a game on this computer (the .NET 10 Octgn.Online.StandAloneServer, in a process of its own),
        /// then joins it as its host. <paramref name="findServerInSourceTree"/>: look for the server in the
        /// output folder of its project when it is not next to this program.
        /// </summary>
        public static async Task<GameSession> HostLocalGame(Game game, string nickname, string gameName = null,
            bool findServerInSourceTree = true, int broadcastPort = DefaultBroadcastPort) {
            Initialize();
            var version = Const.OctgnVersion;
            var hosted = new HostedGame {
                Id = Guid.NewGuid(),
                Name = gameName ?? (nickname + "'s game"),
                HostUser = new Communication.User(Program.UserId ?? "##LOCAL##" + nickname, nickname),
                GameName = game.Name,
                GameId = game.Id,
                GameVersion = game.Version.ToString(),
                OctgnVersion = version.ToString(),
                HostAddress = "0.0.0.0:" + FreeTcpPort(),
                Source = HostedGameSource.Lan,
                Spectators = true,
                DateCreated = DateTimeOffset.Now,
            };
            var server = new HostedGameProcess(hosted, isDebug: findServerInSourceTree, isLocal: true, broadcastPort: broadcastPort);
            await server.StartAsync().ConfigureAwait(false);
            try {
                var session = await Join(game, hosted, nickname, isHost: true, spectator: false).ConfigureAwait(false);
                session._server = server;
                return session;
            } catch {
                KillServer(hosted.ProcessId);
                throw;
            }
        }

        /// <summary>
        /// Joins a game (JoinGameLauncher): builds the engine and its script engine on the game dispatcher,
        /// connects to the game's server, and says hello (GameEngine.Begin, as the pre-game lobby does).
        /// </summary>
        public static async Task<GameSession> Join(Game game, HostedGame hosted, string nickname, bool isHost, bool spectator, string password = null) {
            Initialize();
            var dispatcher = Program.Dispatcher ?? throw new InvalidOperationException("Set Program.Dispatcher (the game dispatcher) first.");
            if (Program.GameEngine != null) throw new InvalidOperationException("A game is already running.");

            Program.CurrentHostedGame = hosted;
            Program.IsHost = isHost;
            if (hosted.Source == HostedGameSource.Online) Program.CurrentOnlineGameName = hosted.Name;
            var session = new GameSession(hosted, isHost);

            await dispatcher.InvokeAsync(() => {
                Program.GameEngine = new GameEngine(game, nickname, spectator, password ?? (isHost ? hosted.Password ?? "" : ""), isLocal: hosted.Source == HostedGameSource.Lan);
                // The script engine registers itself with the game (MEF creates it for PlayWindow in WPF).
                new Engine();
                // PreGameLobby.OnLoaded.
                Program.GameSettings.UseTwoSidedTable = game.UseTwoSidedTable;
                Program.GameSettings.ChangeTwoSidedTable = game.ChangeTwoSidedTable;
                Program.GameSettings.AllowSpectators = true;
            }).ConfigureAwait(false);

            try {
                var client = await Connect(hosted.Host, hosted.Port).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Unable to connect to {hosted.Name} at {hosted.HostAddress}");
                await dispatcher.InvokeAsync(() => {
                    Program.Client = client;
                    Program.GameEngine.Begin();
                }).ConfigureAwait(false);
            } catch {
                await dispatcher.InvokeAsync(Program.StopGame).ConfigureAwait(false);
                throw;
            }
            return session;
        }

        /// <summary>
        /// What PlayWindow does when the pre-game lobby closes, once the user interface shows the table: sets
        /// up the script engine of a player (not of a spectator) and tells the server this client is ready
        /// (GameEngine.Ready): the server then sends the game state, and the game's OnTableLoaded events run.
        /// Call it on the game dispatcher.
        /// </summary>
        public static void StartTable() {
            Program.Dispatcher.VerifyAccess();
            var engine = Program.GameEngine ?? throw new InvalidOperationException("No game is running.");
            if (Player.LocalPlayer.Spectator == false && engine.IsReplay == false)
                engine.ScriptEngine.SetupEngine(false);
            Program.Dispatcher.BeginInvoke(new Action(() => Program.GameEngine?.Ready()), DispatcherPriority.ContextIdle);
        }

        /// <summary>Starts the game for every player (the host's Start button in the pre-game lobby).</summary>
        public void StartGame() {
            if (!IsHost) throw new InvalidOperationException("Only the host starts the game.");
            Program.Dispatcher.Invoke(() => Program.Client.Rpc.Start());
        }

        /// <summary>Loads a deck file (.o8d) for the local player (PlayWindow's Load deck).</summary>
        public static void LoadDeck(string path) {
            Program.Dispatcher.VerifyAccess();
            var engine = Program.GameEngine ?? throw new InvalidOperationException("No game is running.");
            var game = GameManager.Get().GetById(engine.Definition.Id);
            var deck = new Deck().Load(game, path);
            engine.LoadDeck(deck, false);
        }

        /// <summary>Leaves the game (Program.StopGame on the game dispatcher); a hosted server stops by itself once empty.</summary>
        public void Dispose() {
            var dispatcher = Program.Dispatcher;
            try {
                if (dispatcher != null) dispatcher.Invoke(() => Program.StopGame());
                else Program.StopGame();
            } catch (Exception ex) {
                Log.Warn("Leaving the game", ex);
            }
            // StopGame clears the game dispatcher: the user interface keeps its own.
            if (Program.Dispatcher == null) Program.Dispatcher = dispatcher;
        }

        /// <summary>Stops the local game server now (it stops by itself 15 s after its last player leaves).</summary>
        public void StopServer() {
            if (IsHost) KillServer(HostedGame.ProcessId);
        }

        private static async Task<ClientSocket> Connect(string host, int port) {
            foreach (var address in Resolve(host)) {
                try {
                    Log.Info($"Connecting to {address}:{port}");
                    var client = new ClientSocket(address, port);
                    await client.Connect().ConfigureAwait(false);
                    return client;
                } catch (Exception ex) {
                    Log.Warn($"Couldn't connect to {address}:{port}", ex);
                }
            }
            return null;
        }

        private static IEnumerable<IPAddress> Resolve(string host) {
            if (host == "0.0.0.0" || string.IsNullOrEmpty(host)) {
                yield return IPAddress.Loopback;
                yield break;
            }
            foreach (var address in Dns.GetHostAddresses(host)) {
                if (address.Equals(IPAddress.IPv6Loopback)) continue;
                yield return address;
            }
        }

        private static int FreeTcpPort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void KillServer(int processId) {
            try {
                using (var process = System.Diagnostics.Process.GetProcessById(processId)) process.Kill();
            } catch (ArgumentException) {
            } catch (InvalidOperationException) {
            }
        }
    }

    /// <summary>
    /// A card named in a game message, as it was when the message was written (ChatCard of the WPF chat):
    /// its model when it could be seen, and the card of the table it is, if any.
    /// </summary>
    public sealed class ChatCard
    {
        public ChatCard(DataNew.Entities.Card model, Card gameCard) {
            Card = model;
            GameCard = gameCard;
        }

        public DataNew.Entities.Card Card { get; }

        public Card GameCard { get; }

        public override string ToString() {
            return Card == null ? "[?]" : Card.GetName();
        }
    }
}

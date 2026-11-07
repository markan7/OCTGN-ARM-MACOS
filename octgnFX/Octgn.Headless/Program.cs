// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using Octgn.Communication;
using Octgn.DataNew;
using Octgn.DataNew.Entities;
using Octgn.Library;
using Octgn.Library.Networking;
using Octgn.Library.Providers;
using Octgn.Online.Hosting;

namespace Octgn.Headless
{
    /// <summary>
    /// Proof of concept without user interface (step 2 of the macOS port). Each step uses the shared OCTGN
    /// libraries the way the client does: game definition (Octgn.DataNew), local hosting (HostedGameProcess
    /// and the .NET 10 Octgn.Online.StandAloneServer), LAN discovery (GameBroadcastListener), the game
    /// protocol, and the game's IronPython 2.7 scripts with the 3.1.0.2 Python API.
    /// </summary>
    public static class Program
    {
        private const int BroadcastPort = 21299; // Not the client's port (21234): real clients on the LAN ignore it.

        private static readonly List<(string Step, bool Ok, string Detail)> Results = new List<(string, bool, string)>();
        private static ScriptEngine _engine;
        private static ScriptScope _scope;
        private static bool _verbose;

        public static async Task<int> Main(string[] args) {
            var verbose = _verbose = args.Contains("--verbose");
            var keep = args.Contains("--keep");
            var gameIndex = Array.IndexOf(args, "--game");
            var gamePath = gameIndex >= 0 && gameIndex + 1 < args.Length ? Path.GetFullPath(args[gameIndex + 1]) : null;
            if (gameIndex >= 0 && gamePath == null) {
                Console.Error.WriteLine("Usage: Octgn.Headless [--game <game folder or definition.xml>] [--verbose] [--keep]");
                return 2;
            }
            if (verbose) log4net.Config.BasicConfigurator.Configure();

            Console.WriteLine($"OCTGN headless check: .NET {Environment.Version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");

            var work = Path.Combine(Path.GetTempPath(), "octgn-headless-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(work);
            // Separate data folder (configuration, game database, images) for this run.
            Environment.SetEnvironmentVariable(DataDirectoryProvider.OCTGNDATA_ENVIRONMENTALVARIABLE, Path.Combine(work, "Data"));
            Config.Instance = new Config(); // As the client does at startup.

            HostedGame hosted = null;
            try {
                var game = Step("Game definition", () => LoadGame(work, gamePath));
                if (game == null) return Finish(work, keep);

                using (var listener = new GameBroadcastListener(BroadcastPort)) {
                    listener.StartListening();

                    hosted = await StepAsync("Local server", () => Host(game));
                    if (hosted == null) return Finish(work, keep);

                    await StepAsync("LAN discovery", async () => {
                        var seen = await listener.WaitForGame(hosted.Id);
                        return $"broadcast received for \"{seen.Name}\" at {seen.HostAddress} (MS-NRBF payload, no BinaryFormatter)";
                    });
                }

                using (var client = new MinimalGameClient()) {
                    var players = new Dictionary<int, string>();
                    client.MessageReceived += m => {
                        if (m.Method == MinimalGameClient.NewPlayer) lock (players) players[(byte)m.Args[0]] = (string)m.Args[1];
                        if (verbose) Console.WriteLine("    <= " + m);
                    };

                    var localId = Step("Join", () => Join(client, game, hosted));
                    if (localId == null) return Finish(work, keep);

                    var api = new HeadlessScriptApi(game, client, localId.Value,
                        id => { lock (players) return players.TryGetValue(id, out var n) ? n : "Player " + id; },
                        () => { lock (players) return players.Keys.ToArray(); },
                        text => Console.WriteLine("    notify: " + text));

                    Step("Scripts", () => RunScripts(game, api));

                    // IronPython's os functions call the C library through Mono.Unix (a native library) on
                    // macOS and Linux: os.stat is what os.path.exists and os.path.isfile use.
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _scope != null) Step("POSIX calls", () => {
                        var mode = _engine.Execute<int>("import posix\nposix.stat(_wd).st_mode", _scope);
                        var system = _engine.Execute<string>("posix.uname()[0]", _scope);
                        if ((mode & 0x4000) == 0) throw new InvalidOperationException($"posix.stat: mode {mode:x} is not a directory");
                        return $"posix.stat (mode 0o{Convert.ToString(mode, 8)}) and posix.uname ({system}) through Mono.Unix";
                    });

                    if (api.GlobalVariablesSent.Count > 0) Step("Global variables", () => {
                        var echoed = new HashSet<string>();
                        var deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline && !api.GlobalVariablesSent.All(echoed.Contains)) {
                            lock (client.Log)
                                foreach (var m in client.Log.Where(m => m.Method == MinimalGameClient.SetGlobalVariable))
                                    if ((string)m.Args[2] == api.GlobalVariables[(string)m.Args[0]]) echoed.Add((string)m.Args[0]);
                            System.Threading.Thread.Sleep(100);
                        }
                        var missing = api.GlobalVariablesSent.Where(n => !echoed.Contains(n)).Distinct().ToList();
                        if (missing.Count > 0) throw new InvalidOperationException("Not echoed by the server: " + string.Join(", ", missing));
                        return "set by the scripts, echoed by the server: " +
                               string.Join(", ", api.GlobalVariablesSent.Distinct().Select(n => $"{n} = {api.GlobalVariables[n]}"));
                    });

                    Step("Chat", () => {
                        var printed = new HashSet<string>();
                        var deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline && !api.Notifications.All(printed.Contains)) {
                            lock (client.Log)
                                foreach (var m in client.Log.Where(m => m.Method == MinimalGameClient.Print))
                                    printed.Add((string)m.Args[1]);
                            System.Threading.Thread.Sleep(100);
                        }
                        var missing = api.Notifications.Where(n => !printed.Contains(n)).ToList();
                        if (missing.Count > 0) throw new InvalidOperationException("Not broadcast by the server: " + string.Join(" | ", missing));
                        return $"the server broadcast the {api.Notifications.Count} notifications back to the table";
                    });
                }

                await StepAsync("Shutdown", async () => {
                    // The server stops by itself 15 s after its last player leaves (local game,
                    // PlayerCollection), on a timer: allow for both.
                    var stopped = await WaitForExit(hosted.ProcessId, TimeSpan.FromSeconds(45));
                    if (!stopped) throw new TimeoutException("The server did not stop after the last player left.");
                    return "the server stopped after the last player left";
                });
                hosted = null;
            } finally {
                if (hosted != null) Kill(hosted.ProcessId);
            }
            return Finish(work, keep);
        }

        private static (Game, string) LoadGame(string work, string gamePath) {
            // An unpacked game package (a folder with definition.xml), or the sample game.
            var definition = gamePath == null
                ? SampleGame.Prepare(Path.Combine(work, "Game"))
                : Directory.Exists(gamePath) ? Path.Combine(gamePath, "definition.xml") : gamePath;
            if (!File.Exists(definition)) throw new FileNotFoundException("No game definition (definition.xml).", definition);
            var game = (Game)new GameSerializer().Deserialize(definition);
            var setsDirectory = Path.Combine(Path.GetDirectoryName(definition), "Sets");
            var sets = !Directory.Exists(setsDirectory) ? new List<Set>() : Directory
                .GetFiles(setsDirectory, "set.xml", SearchOption.AllDirectories)
                .Select(path => (Set)new SetSerializer { Game = game }.Deserialize(path))
                .ToList();
            game.InstallPath = Path.GetDirectoryName(definition);
            var detail = $"\"{game.Name}\" {game.Version}, script API {game.ScriptVersion}, {game.Player.Groups.Count()} piles, " +
                         $"{game.Player.Counters.Count()} counter(s), {sets.Count} set(s) / {sets.Sum(s => s.Cards.Count())} card(s), {game.Scripts.Count} script(s)";
            return (game, detail);
        }

        private static async Task<HostedGame> Host(Game game) {
            var version = typeof(Config).Assembly.GetName().Version;
            var hosted = new HostedGame {
                Id = Guid.NewGuid(),
                Name = "Headless check",
                HostUser = new User("headless-host", "Host"),
                GameName = game.Name,
                GameId = game.Id,
                GameVersion = game.Version.ToString(),
                OctgnVersion = version.ToString(),
                HostAddress = "0.0.0.0:" + FreeTcpPort(),
                Source = HostedGameSource.Lan,
                Spectators = true,
                DateCreated = DateTimeOffset.Now,
            };
            // isDebug: look for the server in its project output folder when it is not next to this program.
            var process = new HostedGameProcess(hosted, isDebug: true, isLocal: true, broadcastPort: BroadcastPort);
            await process.StartAsync();
            return hosted;
        }

        private static (byte?, string) Join(MinimalGameClient client, Game game, HostedGame hosted) {
            var version = typeof(Config).Assembly.GetName().Version;
            client.Connect(IPAddress.Loopback.ToString(), hosted.Port);
            client.SendHello("Mac", "headless-player", "Octgn.Headless", version, version, game.Id, game.Version, hosted.Password, false);
            var welcome = client.WaitFor(MinimalGameClient.Welcome, TimeSpan.FromSeconds(10));
            client.WaitFor(MinimalGameClient.Settings, TimeSpan.FromSeconds(5));
            var id = (byte)welcome.Args[0];
            return (id, $"Hello accepted: player {id} in game session \"{welcome.Args[2]}\" ({welcome.Args[1]})");
        }

        private static string RunScripts(Game game, HeadlessScriptApi api) {
            var scripting = Path.Combine(AppContext.BaseDirectory, "Scripting");
            var engine = Python.CreateEngine();
            engine.SetSearchPaths(new[] { Path.Combine(scripting, "Lib"), game.InstallPath });
            engine.Runtime.IO.RedirectToConsole();

            var scope = engine.CreateScope();
            _engine = engine;
            _scope = scope;
            scope.SetVariable("_api", api);
            scope.SetVariable("_wd", game.InstallPath);
            engine.Execute(File.ReadAllText(Path.Combine(scripting, "CaseInsensitiveDict.py")), scope);
            engine.Execute(File.ReadAllText(Path.Combine(scripting, "Versions", game.ScriptVersion + ".py")), scope);
            var lines = 0;
            foreach (var script in game.Scripts) {
                var path = Path.Combine(game.InstallPath, script);
                lines += File.ReadLines(path).Count();
                engine.CreateScriptSourceFromFile(path).Execute(scope);
            }
            var done = new List<string> { $"IronPython {engine.LanguageVersion}: API {game.ScriptVersion} and {game.Scripts.Count} game script(s) ({lines} lines) loaded" };

            // As the engine does when the table is ready (GameEvents.OnTableLoaded).
            if (game.Events.TryGetValue("OnTableLoaded", out var handlers)) {
                foreach (var handler in handlers) engine.Execute(handler.PythonFunction + "()", scope);
                done.Add("OnTableLoaded ran (" + string.Join(", ", handlers.Select(h => h.PythonFunction + "()")) + ")");
            }

            // Group actions of the sample game's table: random numbers come from the server.
            if (scope.ContainsVariable("rollDice") && scope.ContainsVariable("flipCoin")) {
                engine.Execute("rollDice(table)", scope);
                engine.Execute("flipCoin(table)", scope);
                var dice = api.RandomResults[0];
                if (dice < 1 || dice > 6) throw new InvalidOperationException("Invalid die result " + dice);
                done.Add($"rollDice and flipCoin ran, random numbers {string.Join(" and ", api.RandomResults)} from the server");
            }

            // Game scripts read JSON (decks) with .NET Framework's JavaScriptSerializer: its .NET 10 stand-in.
            var name = engine.Execute<string>(
                "import clr\n" +
                "clr.AddReference('System.Web.Extensions')\n" +
                "from System.Web.Script.Serialization import JavaScriptSerializer\n" +
                "_deck = JavaScriptSerializer().DeserializeObject('{\"name\": \"Headless\", \"slots\": {\"01001\": 1}}')\n" +
                "_deck['name'] + ':' + str(_deck['slots']['01001'])", scope);
            if (name != "Headless:1") throw new InvalidOperationException("JavaScriptSerializer returned " + name);
            done.Add("JSON read through System.Web.Extensions");

            // A message for the table, through the server (checked by the Chat step).
            engine.Execute("notify('{} ran the headless check'.format(me.name))", scope);

            var player = engine.Execute<string>("me.name", scope);
            var piles = engine.Execute<string>("', '.join(me.piles.keys())", scope);
            return string.Join("; ", done) + $" (player {player}, piles: {piles})";
        }

        private static int FreeTcpPort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task<bool> WaitForExit(int processId, TimeSpan timeout) {
            try {
                using (var process = Process.GetProcessById(processId)) {
                    var exited = Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));
                    return await exited;
                }
            } catch (ArgumentException) {
                return true; // Already gone.
            }
        }

        private static void Kill(int processId) {
            try {
                using (var process = Process.GetProcessById(processId)) process.Kill();
            } catch (ArgumentException) {
            } catch (InvalidOperationException) {
            }
        }

        private static T Step<T>(string name, Func<(T Value, string Detail)> body) {
            try {
                var (value, detail) = body();
                Record(name, true, detail);
                return value;
            } catch (Exception ex) {
                Record(name, false, Describe(ex));
                return default(T);
            }
        }

        private static void Step(string name, Func<string> body) {
            Step<object>(name, () => (null, body()));
        }

        private static async Task<T> StepAsync<T>(string name, Func<Task<T>> body) where T : class {
            try {
                var result = await body();
                Record(name, true, result as string ?? DescribeHosted(result as HostedGame));
                return result;
            } catch (Exception ex) {
                Record(name, false, Describe(ex));
                return null;
            }
        }

        private static string DescribeHosted(HostedGame hosted) {
            return hosted == null ? "" : $"Octgn.Online.StandAloneServer (pid {hosted.ProcessId}) listening on {hosted.HostAddress}";
        }

        private static string Describe(Exception ex) {
            var inner = ex is AggregateException agg && agg.InnerExceptions.Count == 1 ? agg.InnerException : ex;
            if (_verbose) Console.WriteLine(inner);
            return inner.GetType().Name + ": " + inner.Message;
        }

        private static void Record(string step, bool ok, string detail) {
            Results.Add((step, ok, detail));
            Console.WriteLine($"==> [{(ok ? "OK" : "FAILED")}] {step}: {detail}");
        }

        private static int Finish(string work, bool keep) {
            Console.WriteLine();
            foreach (var (step, ok, _) in Results)
                Console.WriteLine($"  {(ok ? "OK    " : "FAILED")} {step}");
            if (keep) Console.WriteLine("Files kept in " + work);
            else try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            var success = Results.Count > 0 && Results.All(r => r.Ok);
            Console.WriteLine(success ? "Result: OK" : "Result: FAILED");
            return success ? 0 : 1;
        }
    }
}

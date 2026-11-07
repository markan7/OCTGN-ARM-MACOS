using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Octgn.Online.Hosting;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Octgn.Communication;

namespace Octgn.Library
{
    public class HostedGameProcess
    {
        private static ILogger Log = LoggerFactory.Create(nameof(HostedGameProcess));
        public HostedGame HostedGame { get; }
        public const int LocalPortAttempts = 10;
        private readonly bool _isLocal;

        public HostedGameProcess(
            HostedGame game,
            bool isDebug,
            bool isLocal,
            int broadcastPort = 21234
            ) {

            HostedGame = game ?? throw new ArgumentNullException(nameof(game));
            _isLocal = isLocal;

            if (game.HostUser == null) throw new InvalidOperationException($"{nameof(game)}.{nameof(game.HostUser)} can't be null.");

            var arguments = CreateArguments(game, broadcastPort, isLocal);

            if (!Version.TryParse(game.OctgnVersion, out var sasVersion) && !isLocal) throw new InvalidOperationException($"{nameof(game.OctgnVersion)} '{game.OctgnVersion}' is invalid.");

            var path = GetSASPath(sasVersion, isLocal, isDebug);

            _process = new Process();
            _process.StartInfo = CreateStartInfo(path, arguments);

            if (isLocal) { //Launch as a subprocess and share our console. Don't do this on the server otherwise it causes issues restarting things individually
                _process.StartInfo.UseShellExecute = false;
                _process.StartInfo.CreateNoWindow = true;
            }
        }

        private readonly Process _process;
        private int _started;

        public void Stop() {
            _process.Close();
        }

        public void Start() {
            Start(null);
        }

        private void Start(string readyPipe) {
            if (readyPipe != null) {
                if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Server already started.");
                _process.StartInfo.Arguments += " -ready-pipe=" + readyPipe;
            }
            Log.Info($"Starting {_process.StartInfo.FileName} {_process.StartInfo.Arguments}");
            _process.Start();
            HostedGame.ProcessId = _process.Id;
        }

        public async Task StartAsync() {
            if (!_isLocal) { Start(); return; }
            var preferredPort = HostedGame.Port;
            if (preferredPort > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(HostedGame.Port));
            var host = HostedGame.Host;
            var pipeName = Guid.NewGuid().ToString("N");
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)) {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler onExit = (_, __) => exited.TrySetResult(true);
                _process.EnableRaisingEvents = true;
                _process.Exited += onExit;
                var ready = ReadReadyPort(pipe, preferredPort);
                var started = false;
                try {
                    Start(pipeName);
                    started = true;
                    var completed = await Task.WhenAny(ready, exited.Task, Task.Delay(10000));
                    if (completed == exited.Task || _process.HasExited)
                        throw new InvalidOperationException("The game server exited before it finished starting.");
                    if (completed != ready)
                        throw new TimeoutException("The game server did not become ready in time.");
                    var port = await ready;
                    HostedGame.HostAddress = $"{host}:{port}";
                } catch {
                    if (started) {
                        try { if (!_process.HasExited) _process.Kill(); }
                        catch (Exception ex) { Log.Warn("Could not stop the game server after startup failed.", ex); }
                    }
                    throw;
                } finally {
                    _process.Exited -= onExit;
                    pipe.Dispose();
                    // Closing the pipe also ends/observes a read abandoned on exit or timeout.
                    // The wait is bounded: on Unix, a pipe disposed while its connection completes
                    // may leave the read pending.
                    if (await Task.WhenAny(ready, Task.Delay(1000)) == ready) {
                        try { await ready; } catch { }
                    } else {
                        _ = ready.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
            }
        }

        private static async Task<int> ReadReadyPort(NamedPipeServerStream pipe, int preferredPort) {
            await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            var bytes = new byte[sizeof(int)];
            var received = 0;
            while (received < bytes.Length) {
                var count = await pipe.ReadAsync(bytes, received, bytes.Length - received).ConfigureAwait(false);
                if (count == 0) throw new InvalidOperationException("The game server reported an unusable port.");
                received += count;
            }
            var port = BitConverter.ToInt32(bytes, 0);
            if (port < preferredPort || port > Math.Min(ushort.MaxValue, preferredPort + LocalPortAttempts - 1))
                throw new InvalidOperationException("The game server reported an unusable port.");
            return port;
        }

        public static async Task ReportReadyAsync(string pipeName, int port) {
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous)) {
                await pipe.ConnectAsync(5000).ConfigureAwait(false);
                var bytes = BitConverter.GetBytes(port);
                await pipe.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
        }

        public static string GetSASPath(Version sasVersion, bool isLocal, bool isDebug) {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetCrossPlatformSASPath(isDebug);

            var path = "";
            // Get file path
            if (isDebug) {
                if (System.Reflection.Assembly.GetEntryAssembly().GetName().Name == "Octgn.Desktop") {
                    path = Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\..\Octgn.Online.StandAloneServer\bin\Debug\Octgn.Online.StandAloneServer.exe");
                } else {
                    path = Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\Octgn.Online.StandAloneServer\bin\Debug\Octgn.Online.StandAloneServer.exe");
                }
                path = Path.GetFullPath(path);
            } else if (isLocal) {
                path = Directory.GetCurrentDirectory() + "\\Octgn.Online.StandAloneServer.exe";
            } else {

                path = Path.Combine("c:\\Server\\sas", sasVersion + "_Override", "Octgn.Online.StandAloneServer.exe");
                if (!File.Exists(path)) {
                    path = Path.Combine("c:\\Server\\sas", sasVersion.ToString(), "Octgn.Online.StandAloneServer.exe");
                }
            }

            return path;
        }

        public const string SASFileName = "Octgn.Online.StandAloneServer";

        /// <summary>
        /// macOS and Linux: the server (.NET 10 build) is next to the client, or, for a debug build,
        /// in the output folder of the Octgn.Online.StandAloneServer project. The returned file is
        /// either the native launcher or the server's .dll (see <see cref="CreateStartInfo"/>).
        /// </summary>
        private static string GetCrossPlatformSASPath(bool isDebug) {
            var baseDirectory = AppContext.BaseDirectory;
            var candidates = new List<string> { baseDirectory };
            if (isDebug) {
                // <octgnFX>/<client project>/bin/<Configuration>/net10.0/: the client's own configuration first.
                var octgnFX = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
                var clientConfiguration = Path.GetFileName(Path.GetDirectoryName(baseDirectory.TrimEnd(Path.DirectorySeparatorChar)));
                var configurations = new[] { clientConfiguration, "Debug", "Release" }
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var configuration in configurations)
                    candidates.Add(Path.Combine(octgnFX, SASFileName, "bin", configuration, "net10.0"));
            }
            foreach (var directory in candidates) {
                // The native launcher only starts the .dll next to it.
                var assembly = Path.Combine(directory, SASFileName + ".dll");
                if (!File.Exists(assembly)) continue;
                var launcher = Path.Combine(directory, SASFileName);
                return File.Exists(launcher) ? launcher : assembly;
            }
            return Path.Combine(baseDirectory, SASFileName);
        }

        /// <summary>
        /// Process start information for the server at <paramref name="path"/>: a .dll is started
        /// through the dotnet host that runs the current process.
        /// </summary>
        public static ProcessStartInfo CreateStartInfo(string path, IEnumerable<string> arguments) {
            var joined = string.Join(" ", arguments);
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return new ProcessStartInfo(path, joined);

            var host = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(host) || !Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                host = "dotnet";
            return new ProcessStartInfo(host, "\"" + path + "\" " + joined);
        }

        public static string[] CreateArguments(HostedGame game, int broadcastPort, bool isLocal) {
            var atemp = new List<string>();
            atemp.Add("-id=" + game.Id.ToString());
            atemp.Add("-name=\"" + game.Name + "\"");
            atemp.Add("-hostuserid=\"" + game.HostUser.Id + "\"");
            atemp.Add("-hostusername=\"" + game.HostUser.DisplayName + "\"");
            atemp.Add("-gamename=\"" + game.GameName + "\"");
            atemp.Add("-gameid=" + game.GameId);
            atemp.Add("-gameversion=" + game.GameVersion);
            atemp.Add("-bind=" + game.HostAddress);
            atemp.Add("-password=" + game.Password);
            atemp.Add("-broadcastport=" + broadcastPort);
            if (!string.IsNullOrWhiteSpace(game.GameIconUrl))
                atemp.Add("-gameiconurl=" + game.GameIconUrl);
            if (!string.IsNullOrWhiteSpace(game.HostUserIconUrl))
                atemp.Add("-usericonurl=" + game.HostUserIconUrl);
            if (game.Spectators)
                atemp.Add("-spectators");
            if (isLocal)
                atemp.Add("-local");

            return atemp.ToArray();
        }
    }
}

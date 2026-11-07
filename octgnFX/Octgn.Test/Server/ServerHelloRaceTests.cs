using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Octgn.Online.Hosting;
using Octgn.Server;

namespace Octgn.Test.Server
{
    /// <summary>
    /// A client may send its Hello as soon as it is connected. The server must not start reading
    /// the socket before the player listens to it, or that Hello is raised to nobody and lost: the
    /// client then waits forever for its Welcome.
    /// </summary>
    [TestFixture, NonParallelizable, Timeout(20000)]
    public class ServerHelloRaceTests
    {
        private const byte ErrorMessage = 0;
        private const byte HelloMessage = 3;
        private const byte WelcomeMessage = 5;

        private Octgn.Server.Server _server;
        private TcpClient _client;

        [TearDown]
        public void Cleanup()
        {
            _client?.Close();
            if (_server == null) return;
            _server.Dispose();
            _server.Context.State.Players.Dispose();
            ((IDisposable)typeof(Octgn.Server.Server)
                .GetField("_broadcaster", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_server)).Dispose();
        }

        [Test]
        public async Task Server_AnswersHelloSentBeforeTheConnectionIsAccepted()
        {
            var port = FreePort();
            var game = new HostedGame {
                Id = Guid.NewGuid(),
                GameId = Guid.NewGuid(),
                GameVersion = "1.0",
                Name = "Hello race",
                HostAddress = $"127.0.0.1:{port}",
                Spectators = true,
                Status = HostedGameStatus.StartedHosting
            };
            // A local server binds its listener in the constructor. Server.Start is not called:
            // it would also start the UDP broadcasts.
            _server = new Octgn.Server.Server(new Config { IsLocal = true, PlayerTimeoutSeconds = 3600 }, game, 21234, true);
            var version = _server.Context.Config.ServerVersion;

            // The connection completes in the listen backlog, and the Hello is already waiting in the
            // socket when the server accepts it: the worst case for the server.
            _client = new TcpClient(AddressFamily.InterNetwork);
            _client.Connect(IPAddress.Loopback, port);
            var stream = _client.GetStream();
            var hello = Hello("Racer", "racer", "Hello race test", version, version, game.GameId, new Version(game.GameVersion));
            stream.Write(hello, 0, hello.Length);

            var accept = (Task)typeof(Octgn.Server.Server)
                .GetMethod("ListenSingle", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_server, null);
            await accept;

            // The server sends PlayerSettings first (Player.Setup), then the Welcome.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            byte message;
            do
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) Assert.Fail("No Welcome within 10 s.");
                message = ReadMessageNumber(stream, left);
                Assert.AreNotEqual(ErrorMessage, message, "The server refused the Hello.");
            } while (message != WelcomeMessage);
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // Same layout as the client's BinarySenderStub.Hello: Int32 length, Int32 muted, message number, parameters.
        private static byte[] Hello(string nick, string userId, string client, Version clientVersion, Version octgnVersion, Guid gameId, Version gameVersion)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(0);
                writer.Write(0);
                writer.Write(HelloMessage);
                writer.Write(nick);
                writer.Write(userId);
                writer.Write((ulong)0);
                writer.Write(client);
                writer.Write(clientVersion.ToString());
                writer.Write(octgnVersion.ToString());
                writer.Write(gameId.ToByteArray());
                writer.Write(gameVersion.ToString());
                writer.Write(string.Empty);
                writer.Write(false);
                writer.Flush();
                writer.Seek(0, SeekOrigin.Begin);
                writer.Write((int)memory.Length);
                writer.Flush();
                return memory.ToArray();
            }
        }

        // Reads the next message sent by the server and returns its number.
        private static byte ReadMessageNumber(NetworkStream stream, TimeSpan timeout)
        {
            stream.ReadTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);
            var length = BitConverter.ToInt32(ReadExactly(stream, 4), 0);
            Assert.That(length, Is.InRange(9, 1024 * 1024), "Invalid message length");
            var body = ReadExactly(stream, length - 4); // Int32 muted, message number, parameters
            return body[4];
        }

        private static byte[] ReadExactly(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            try
            {
                while (read < count)
                {
                    var received = stream.Read(buffer, read, count - read);
                    if (received == 0) Assert.Fail("The server closed the connection without a Welcome.");
                    read += received;
                }
            }
            catch (IOException e) when (e.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
            {
                Assert.Fail("The server did not answer the Hello: it was lost.");
            }
            return buffer;
        }
    }
}

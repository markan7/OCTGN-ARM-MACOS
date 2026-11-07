// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Octgn.Headless
{
    /// <summary>A decoded message from the game server.</summary>
    internal sealed class ServerMessage
    {
        public ServerMessage(byte method, string name, object[] args) {
            Method = method;
            Name = name;
            Args = args;
        }

        public byte Method { get; }
        public string Name { get; }
        public object[] Args { get; }

        public override string ToString() => Name + "(" + string.Join(", ", Array.ConvertAll(Args, a => a is string s ? "\"" + s + "\"" : Convert.ToString(a))) + ")";
    }

    /// <summary>
    /// Client side of the OCTGN game protocol (Octgn.Server/Protocol.xml), reduced to the messages the
    /// proof of concept needs. Frame: Int32 length (itself included), Int32 muted, byte message number
    /// (its position in Protocol.xml), then the parameters written with BinaryWriter.
    /// </summary>
    internal sealed class MinimalGameClient : IDisposable
    {
        public const byte Error = 0;
        public const byte Kick = 2;
        public const byte Hello = 3;
        public const byte Welcome = 5;
        public const byte Settings = 6;
        public const byte PlayerSettings = 7;
        public const byte NewPlayer = 8;
        public const byte Leave = 9;
        public const byte PrintReq = 23;
        public const byte Print = 24;
        public const byte RandomReq = 25;
        public const byte Random = 26;
        public const byte SetGlobalVariable = 85;
        public const byte Ping = 87;

        private readonly TcpClient _tcp = new TcpClient();
        private readonly object _sendLock = new object();
        private readonly BlockingCollection<ServerMessage> _inbox = new BlockingCollection<ServerMessage>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private NetworkStream _stream;
        private Task _reader;
        private Timer _pinger;

        /// <summary>Every message received, in order.</summary>
        public List<ServerMessage> Log { get; } = new List<ServerMessage>();

        public event Action<ServerMessage> MessageReceived;

        public void Connect(string host, int port) {
            _tcp.Connect(host, port);
            _stream = _tcp.GetStream();
            _reader = Task.Run(ReadLoop);
            // The server drops players that do not ping (PlayerTimeoutSeconds).
            _pinger = new Timer(_ => TrySend(Ping, w => { }), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        public void SendHello(string nick, string userId, string client, Version clientVersion, Version octgnVersion,
                              Guid gameId, Version gameVersion, string password, bool spectator) {
            Send(Hello, w => {
                w.Write(nick);
                w.Write(userId);
                w.Write((ulong)0); // pkey
                w.Write(client);
                w.Write(clientVersion.ToString());
                w.Write(octgnVersion.ToString());
                w.Write(gameId.ToByteArray());
                w.Write(gameVersion.ToString());
                w.Write(password ?? string.Empty);
                w.Write(spectator);
            });
        }

        public void SendPrintRequest(string text) => Send(PrintReq, w => w.Write(text));

        public void SendRandomRequest(int min, int max) => Send(RandomReq, w => {
            w.Write(min);
            w.Write(max);
        });

        public void SendSetGlobalVariable(string name, string oldValue, string value) => Send(SetGlobalVariable, w => {
            w.Write(name);
            w.Write(oldValue);
            w.Write(value);
        });

        /// <summary>Next message of the given number (others are skipped), or an exception after the timeout.</summary>
        public ServerMessage WaitFor(byte method, TimeSpan timeout) {
            var deadline = DateTime.UtcNow + timeout;
            while (true) {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !_inbox.TryTake(out var message, left))
                    throw new TimeoutException($"No message {method} from the server within {timeout.TotalSeconds:0} s.");
                if (message.Method == Error || message.Method == Kick)
                    throw new InvalidOperationException("The server refused the request: " + message);
                if (message.Method == method) return message;
            }
        }

        private void Send(byte method, Action<BinaryWriter> writeArguments) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(0); // length, set below
                    writer.Write(0); // muted
                    writer.Write(method);
                    writeArguments(writer);
                    writer.Flush();
                    writer.Seek(0, SeekOrigin.Begin);
                    writer.Write((int)stream.Length);
                }
                var bytes = stream.ToArray();
                lock (_sendLock) _stream.Write(bytes, 0, bytes.Length);
            }
        }

        private void TrySend(byte method, Action<BinaryWriter> writeArguments) {
            try {
                if (_tcp.Connected) Send(method, writeArguments);
            } catch (IOException) {
            } catch (ObjectDisposedException) {
            }
        }

        private async Task ReadLoop() {
            var header = new byte[4];
            try {
                while (!_stop.IsCancellationRequested) {
                    if (!await ReadExactly(header)) break;
                    var length = BitConverter.ToInt32(header, 0);
                    if (length < 9 || length > 16 * 1024 * 1024) throw new InvalidDataException($"Invalid message length {length}.");
                    var body = new byte[length - 4];
                    if (!await ReadExactly(body)) break;
                    var message = Decode(body);
                    lock (Log) Log.Add(message);
                    MessageReceived?.Invoke(message);
                    _inbox.Add(message);
                }
            } catch (Exception) when (_stop.IsCancellationRequested) {
            } finally {
                _inbox.CompleteAdding();
            }
        }

        private async Task<bool> ReadExactly(byte[] buffer) {
            var read = 0;
            while (read < buffer.Length) {
                var count = await _stream.ReadAsync(buffer, read, buffer.Length - read, _stop.Token);
                if (count == 0) return false;
                read += count;
            }
            return true;
        }

        private static ServerMessage Decode(byte[] body) {
            using (var reader = new BinaryReader(new MemoryStream(body))) {
                reader.ReadInt32(); // muted
                var method = reader.ReadByte();
                switch (method) {
                    case Error: return new ServerMessage(method, "Error", new object[] { reader.ReadString() });
                    case Kick: return new ServerMessage(method, "Kick", new object[] { reader.ReadString() });
                    case Welcome:
                        return new ServerMessage(method, "Welcome", new object[] {
                            reader.ReadByte(), new Guid(reader.ReadBytes(16)), reader.ReadString(), reader.ReadBoolean() });
                    case Settings:
                        return new ServerMessage(method, "Settings", new object[] {
                            reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadBoolean() });
                    case PlayerSettings:
                        return new ServerMessage(method, "PlayerSettings", new object[] {
                            reader.ReadByte(), reader.ReadBoolean(), reader.ReadBoolean() });
                    case NewPlayer:
                        return new ServerMessage(method, "NewPlayer", new object[] {
                            reader.ReadByte(), reader.ReadString(), reader.ReadString(), reader.ReadUInt64(), reader.ReadBoolean(), reader.ReadBoolean() });
                    case Leave: return new ServerMessage(method, "Leave", new object[] { reader.ReadByte() });
                    case Print: return new ServerMessage(method, "Print", new object[] { reader.ReadByte(), reader.ReadString() });
                    case Random: return new ServerMessage(method, "Random", new object[] { reader.ReadInt32() });
                    case SetGlobalVariable:
                        return new ServerMessage(method, "SetGlobalVariable", new object[] { reader.ReadString(), reader.ReadString(), reader.ReadString() });
                    case Ping: return new ServerMessage(method, "Ping", new object[0]);
                    default: return new ServerMessage(method, "Message#" + method, new object[] { body.Length + " bytes" });
                }
            }
        }

        public void Dispose() {
            _stop.Cancel();
            _pinger?.Dispose();
            try { _tcp.Close(); } catch (SocketException) { }
            try { _reader?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        }
    }
}

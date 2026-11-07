// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Formats.Nrbf;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using NUnit.Framework;
using Octgn.Communication;
using Octgn.Library.Networking;
using Octgn.Online.Hosting;

namespace Octgn.Test.Library.Networking
{
    [TestFixture]
    public class HostedGameBroadcastSerializerTests
    {
        /// <summary>
        /// The game serialized in TestData/HostedGame.BinaryFormatter.bin. That file was written by
        /// BinaryFormatter (Mono 6.8), the serializer the Windows client uses for LAN broadcasts.
        /// </summary>
        private static HostedGame Sample(bool started) {
            return new HostedGame {
                Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
                Name = "Partie de test é",
                HostUser = new User("user-42", "Marc"),
                GameName = "Basic Game",
                GameId = new Guid("a6c8d2e8-2a3f-4c1e-9e0a-123456789abc"),
                GameVersion = "1.0.0.0",
                OctgnVersion = "3.4.445.0",
                HasPassword = true,
                Password = "",
                ProcessId = 4242,
                Spectators = true,
                GameIconUrl = "",
                HostUserIconUrl = "",
                HostAddress = "192.168.1.20:8888",
                DateCreated = new DateTimeOffset(2026, 11, 7, 15, 30, 12, 345, TimeSpan.FromHours(1)),
                DateStarted = started ? new DateTimeOffset(2026, 11, 7, 15, 45, 0, TimeSpan.FromMinutes(-150)) : (DateTimeOffset?)null,
                Status = HostedGameStatus.GameInProgress,
                Source = HostedGameSource.Lan,
            };
        }

        private static byte[] BinaryFormatterPayload() {
            using (var stream = typeof(HostedGameBroadcastSerializerTests).Assembly.GetManifestResourceStream("HostedGame.BinaryFormatter.bin"))
            using (var copy = new MemoryStream()) {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static void AssertSameGame(HostedGame expected, HostedGame actual) {
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Name, actual.Name);
            Assert.AreEqual(expected.HostUser?.Id, actual.HostUser?.Id);
            Assert.AreEqual(expected.HostUser?.DisplayName, actual.HostUser?.DisplayName);
            Assert.AreEqual(expected.GameName, actual.GameName);
            Assert.AreEqual(expected.GameId, actual.GameId);
            Assert.AreEqual(expected.GameVersion, actual.GameVersion);
            Assert.AreEqual(expected.OctgnVersion, actual.OctgnVersion);
            Assert.AreEqual(expected.HasPassword, actual.HasPassword);
            Assert.AreEqual(expected.Password, actual.Password);
            Assert.AreEqual(expected.ProcessId, actual.ProcessId);
            Assert.AreEqual(expected.Spectators, actual.Spectators);
            Assert.AreEqual(expected.GameIconUrl, actual.GameIconUrl);
            Assert.AreEqual(expected.HostUserIconUrl, actual.HostUserIconUrl);
            Assert.AreEqual(expected.HostAddress, actual.HostAddress);
            Assert.AreEqual(expected.DateCreated, actual.DateCreated);
            Assert.AreEqual(expected.DateCreated.Offset, actual.DateCreated.Offset);
            Assert.AreEqual(expected.DateStarted, actual.DateStarted);
            Assert.AreEqual(expected.DateStarted?.Offset, actual.DateStarted?.Offset);
            Assert.AreEqual(expected.Status, actual.Status);
            Assert.AreEqual(expected.Source, actual.Source);
        }

        [Test]
        public void ReadsWhatBinaryFormatterWrote() {
            var game = HostedGameBroadcastSerializer.Deserialize(BinaryFormatterPayload());
            AssertSameGame(Sample(started: true), game);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RoundTrip(bool started) {
            var game = Sample(started);
            AssertSameGame(game, HostedGameBroadcastSerializer.Deserialize(HostedGameBroadcastSerializer.Serialize(game)));
        }

        [Test]
        public void RoundTrip_EmptyGame() {
            var game = new HostedGame();
            AssertSameGame(game, HostedGameBroadcastSerializer.Deserialize(HostedGameBroadcastSerializer.Serialize(game)));
        }

        [Test]
        public void WritesTheBinaryFormatterLayout() {
            var payload = HostedGameBroadcastSerializer.Serialize(Sample(started: true));
            Assert.IsTrue(NrbfDecoder.StartsWithPayloadHeader(payload));

            var root = NrbfDecoder.DecodeClassRecord(new MemoryStream(payload));
            Assert.AreEqual("Octgn.Online.Hosting.HostedGame", root.TypeName.FullName);
            Assert.AreEqual("Octgn.Online", root.TypeName.AssemblyName.Name);
            // Every field, under the name BinaryFormatter uses for auto-properties.
            foreach (var field in typeof(HostedGame).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                Assert.IsTrue(root.HasMember(field.Name), field.Name);
        }

        [Test]
        public void RejectsAnotherRootType() {
            var payload = BinaryFormatterPayload();
            var name = Encoding.UTF8.GetBytes("Octgn.Online.Hosting.HostedGame");
            var index = IndexOf(payload, name);
            Assert.That(index, Is.GreaterThan(0));
            payload[index + name.Length - 1] = (byte)'X'; // ...HostedGamX

            Assert.Throws<SerializationException>(() => HostedGameBroadcastSerializer.Deserialize(payload));
        }

        [Test]
        public void RejectsGarbage() {
            var payload = new byte[256];
            new Random(42).NextBytes(payload);
            Assert.Catch(() => HostedGameBroadcastSerializer.Deserialize(payload));
        }

        private static int IndexOf(byte[] data, byte[] pattern) {
            for (var i = 0; i <= data.Length - pattern.Length; i++) {
                var found = true;
                for (var j = 0; j < pattern.Length && found; j++)
                    found = data[i + j] == pattern[j];
                if (found) return i;
            }
            return -1;
        }
    }
}

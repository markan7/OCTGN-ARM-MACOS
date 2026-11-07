// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Formats.Nrbf;
using System.IO;
using System.Runtime.Serialization;
using Octgn.Communication;
using Octgn.Online.Hosting;

namespace Octgn.Library.Networking
{
    /// <summary>
    /// Reads a LAN broadcast (a <see cref="HostedGame"/> serialized by <c>BinaryFormatter</c> or by
    /// <see cref="NrbfHostedGameWriter"/>) with System.Formats.Nrbf. The payload only provides values:
    /// no type it names is ever loaded or instantiated, unlike <c>BinaryFormatter.Deserialize</c>.
    /// Members missing from the payload keep their default value.
    /// </summary>
    internal static class NrbfHostedGameReader
    {
        public static HostedGame Read(byte[] data) {
            ClassRecord root;
            using (var ms = new MemoryStream(data, writable: false)) {
                root = NrbfDecoder.DecodeClassRecord(ms);
            }
            Expect(root, "Octgn.Online.Hosting.HostedGame", "Octgn.Online");

            var game = new HostedGame {
                Id = GetGuid(root, nameof(HostedGame.Id)),
                Name = GetString(root, nameof(HostedGame.Name)),
                HostUser = GetUser(root, nameof(HostedGame.HostUser)),
                GameName = GetString(root, nameof(HostedGame.GameName)),
                GameId = GetGuid(root, nameof(HostedGame.GameId)),
                GameVersion = GetString(root, nameof(HostedGame.GameVersion)),
                OctgnVersion = GetString(root, nameof(HostedGame.OctgnVersion)),
                HasPassword = GetBoolean(root, nameof(HostedGame.HasPassword)),
                Password = GetString(root, nameof(HostedGame.Password)),
                ProcessId = GetInt32(root, nameof(HostedGame.ProcessId)),
                Spectators = GetBoolean(root, nameof(HostedGame.Spectators)),
                GameIconUrl = GetString(root, nameof(HostedGame.GameIconUrl)),
                HostUserIconUrl = GetString(root, nameof(HostedGame.HostUserIconUrl)),
                HostAddress = GetString(root, nameof(HostedGame.HostAddress)),
                DateCreated = GetDateTimeOffset(root, nameof(HostedGame.DateCreated)) ?? default(DateTimeOffset),
                DateStarted = GetDateTimeOffset(root, nameof(HostedGame.DateStarted)),
                Status = GetEnum<HostedGameStatus>(root, nameof(HostedGame.Status)),
                Source = GetEnum<HostedGameSource>(root, nameof(HostedGame.Source)),
            };
            return game;
        }

        // Auto-properties are serialized through their compiler-generated backing field.
        private static string Field(string property) => "<" + property + ">k__BackingField";

        /// <param name="assemblyName">Simple name of the expected assembly, or null for a core library type.</param>
        private static void Expect(ClassRecord record, string typeFullName, string assemblyName) {
            var typeName = record.TypeName;
            var matches = typeName.FullName == typeFullName
                && (assemblyName == null || typeName.AssemblyName?.Name == assemblyName);
            if (!matches)
                throw new SerializationException($"Unexpected type '{typeName.AssemblyQualifiedName}' in LAN broadcast, expected '{typeFullName}'.");
        }

        private static string GetString(ClassRecord record, string property) {
            var name = Field(property);
            return record.HasMember(name) ? record.GetString(name) : null;
        }

        private static bool GetBoolean(ClassRecord record, string property) {
            var name = Field(property);
            return record.HasMember(name) && record.GetBoolean(name);
        }

        private static int GetInt32(ClassRecord record, string property) {
            var name = Field(property);
            return record.HasMember(name) ? record.GetInt32(name) : 0;
        }

        private static Guid GetGuid(ClassRecord record, string property) {
            var name = Field(property);
            var value = record.HasMember(name) ? record.GetClassRecord(name) : null;
            if (value == null) return Guid.Empty;
            Expect(value, "System.Guid", null);
            return new Guid(
                value.GetInt32("_a"), value.GetInt16("_b"), value.GetInt16("_c"),
                value.GetByte("_d"), value.GetByte("_e"), value.GetByte("_f"), value.GetByte("_g"),
                value.GetByte("_h"), value.GetByte("_i"), value.GetByte("_j"), value.GetByte("_k"));
        }

        private static DateTimeOffset? GetDateTimeOffset(ClassRecord record, string property) {
            var name = Field(property);
            var value = record.HasMember(name) ? record.GetClassRecord(name) : null;
            if (value == null) return null;
            Expect(value, "System.DateTimeOffset", null);
            // Same data as DateTimeOffset's ISerializable constructor: UTC date and offset in minutes.
            var utcTicks = value.GetDateTime("DateTime").Ticks;
            var offset = TimeSpan.FromMinutes(value.GetInt16("OffsetMinutes"));
            return new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(offset);
        }

        private static T GetEnum<T>(ClassRecord record, string property) where T : struct, Enum {
            var name = Field(property);
            var value = record.HasMember(name) ? record.GetClassRecord(name) : null;
            if (value == null) return default(T);
            Expect(value, typeof(T).FullName, typeof(T).Assembly.GetName().Name);
            var raw = value.GetInt32("value__");
            if (!Enum.IsDefined(typeof(T), raw))
                throw new SerializationException($"Invalid {typeof(T).Name} value {raw} in LAN broadcast.");
            return (T)Enum.ToObject(typeof(T), raw);
        }

        private static User GetUser(ClassRecord record, string property) {
            var name = Field(property);
            var value = record.HasMember(name) ? record.GetClassRecord(name) : null;
            if (value == null) return null;
            Expect(value, "Octgn.Communication.User", "Octgn.Communication");
            return new User {
                Id = GetString(value, nameof(User.Id)),
                DisplayName = GetString(value, nameof(User.DisplayName)),
            };
        }
    }
}

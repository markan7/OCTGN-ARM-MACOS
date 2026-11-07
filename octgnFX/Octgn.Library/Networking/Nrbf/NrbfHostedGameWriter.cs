// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Octgn.Online.Hosting;

namespace Octgn.Library.Networking
{
    /// <summary>
    /// Writes a <see cref="HostedGame"/> in the MS-NRBF format, the way <c>BinaryFormatter</c> lays it out,
    /// so that the Windows client (which deserializes broadcasts with <c>BinaryFormatter</c>) can read it.
    /// Only the field types used by <see cref="HostedGame"/> and <see cref="Octgn.Communication.User"/>
    /// are supported; anything else throws, which the serialization tests catch.
    /// </summary>
    /// <remarks>Specification: [MS-NRBF] .NET Remoting: Binary Format Data Structure.</remarks>
    internal sealed class NrbfHostedGameWriter
    {
        // RecordTypeEnumeration
        private const byte SerializedStreamHeader = 0;
        private const byte SystemClassWithMembersAndTypes = 4;
        private const byte ClassWithMembersAndTypes = 5;
        private const byte BinaryObjectString = 6;
        private const byte MemberReference = 9;
        private const byte ObjectNull = 10;
        private const byte MessageEnd = 11;
        private const byte BinaryLibrary = 12;

        // BinaryTypeEnumeration
        private const byte BtPrimitive = 0;
        private const byte BtString = 1;
        private const byte BtSystemClass = 3;
        private const byte BtClass = 4;

        // PrimitiveTypeEnumeration
        private const byte PtBoolean = 1;
        private const byte PtByte = 2;
        private const byte PtDouble = 6;
        private const byte PtInt16 = 7;
        private const byte PtInt32 = 8;
        private const byte PtInt64 = 9;
        private const byte PtDateTime = 13;

        private const string MscorlibName = "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

        private static readonly string[] GuidMembers = { "_a", "_b", "_c", "_d", "_e", "_f", "_g", "_h", "_i", "_j", "_k" };
        private static readonly byte[] GuidMemberTypes = { PtInt32, PtInt16, PtInt16, PtByte, PtByte, PtByte, PtByte, PtByte, PtByte, PtByte, PtByte };

        private readonly BinaryWriter _out;
        private readonly MemoryStream _buffer = new MemoryStream();
        private readonly Dictionary<Assembly, int> _libraries = new Dictionary<Assembly, int>();
        private readonly Dictionary<object, int> _referenceIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Queue<object> _pending = new Queue<object>();
        private int _lastId;

        private NrbfHostedGameWriter() {
            _out = new BinaryWriter(_buffer, new UTF8Encoding(false));
        }

        public static byte[] Write(HostedGame game) {
            var writer = new NrbfHostedGameWriter();
            writer.WriteGraph(game);
            return writer._buffer.ToArray();
        }

        private void WriteGraph(object root) {
            var rootId = GetReferenceId(root);

            _out.Write(SerializedStreamHeader);
            _out.Write(rootId); // RootId
            _out.Write(-1);     // HeaderId
            _out.Write(1);      // MajorVersion
            _out.Write(0);      // MinorVersion

            foreach (var assembly in UserAssemblies(root.GetType(), new HashSet<Type>()))
                DefineLibrary(assembly);

            _pending.Enqueue(root);
            while (_pending.Count > 0) {
                var obj = _pending.Dequeue();
                WriteClassRecord(obj, _referenceIds[obj]);
            }

            _out.Write(MessageEnd);
            _out.Flush();
        }

        // Libraries must be declared before any record refers to them; declare them all up front.
        private static IEnumerable<Assembly> UserAssemblies(Type type, HashSet<Type> seen) {
            if (!seen.Add(type) || IsSystemType(type)) yield break;
            yield return type.Assembly;
            foreach (var field in SerializableFields(type)) {
                var fieldType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                foreach (var assembly in UserAssemblies(fieldType, seen))
                    yield return assembly;
            }
        }

        private void DefineLibrary(Assembly assembly) {
            if (_libraries.ContainsKey(assembly)) return;
            var id = ++_lastId;
            _libraries[assembly] = id;
            _out.Write(BinaryLibrary);
            _out.Write(id);
            _out.Write(assembly.FullName);
        }

        private int GetReferenceId(object obj) {
            if (!_referenceIds.TryGetValue(obj, out var id)) {
                id = ++_lastId;
                _referenceIds[obj] = id;
            }
            return id;
        }

        /// <summary>Class record for a [Serializable] user type (the root, a referenced class or an inline struct/enum).</summary>
        private void WriteClassRecord(object obj, int objectId) {
            var type = obj.GetType();
            if (type.IsEnum) {
                _out.Write(ClassWithMembersAndTypes);
                _out.Write(objectId);
                _out.Write(type.FullName);
                _out.Write(1);
                _out.Write("value__");
                _out.Write(BtPrimitive);
                _out.Write(PtInt32);
                _out.Write(_libraries[type.Assembly]);
                _out.Write(Convert.ToInt32(obj));
                return;
            }

            if (!type.IsSerializable || typeof(ISerializable).IsAssignableFrom(type))
                throw new NotSupportedException($"Type {type} can not be written as a LAN broadcast.");

            var fields = SerializableFields(type).ToArray();
            _out.Write(ClassWithMembersAndTypes);
            _out.Write(objectId);
            _out.Write(type.FullName);
            _out.Write(fields.Length);
            foreach (var field in fields)
                _out.Write(field.Name);
            foreach (var field in fields)
                _out.Write(BinaryTypeOf(field.FieldType));
            foreach (var field in fields)
                WriteAdditionalInfo(field.FieldType);
            _out.Write(_libraries[type.Assembly]);

            foreach (var field in fields)
                WriteMemberValue(field.FieldType, field.GetValue(obj));
        }

        private static IEnumerable<FieldInfo> SerializableFields(Type type) {
            // Same members, in the same order, as FormatterServices.GetSerializableMembers.
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsNotSerialized);
        }

        private static bool IsSystemType(Type type) {
            return type.Assembly == typeof(object).Assembly;
        }

        private static byte BinaryTypeOf(Type type) {
            if (type == typeof(string)) return BtString;
            if (PrimitiveCode(type) != 0) return BtPrimitive;
            if (type == typeof(Guid) || type == typeof(DateTimeOffset) || Nullable.GetUnderlyingType(type) != null) return BtSystemClass;
            if (type.IsEnum || (type.IsClass && type.IsSerializable && !IsSystemType(type))) return BtClass;
            throw new NotSupportedException($"Field type {type} is not supported in LAN broadcasts.");
        }

        private void WriteAdditionalInfo(Type type) {
            switch (BinaryTypeOf(type)) {
                case BtPrimitive:
                    _out.Write(PrimitiveCode(type));
                    break;
                case BtSystemClass:
                    _out.Write(SystemTypeName(type));
                    break;
                case BtClass:
                    _out.Write(type.FullName);
                    _out.Write(_libraries[type.Assembly]);
                    break;
            }
        }

        private static string SystemTypeName(Type type) {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) {
                if (underlying != typeof(DateTimeOffset) && underlying != typeof(Guid))
                    throw new NotSupportedException($"Field type {type} is not supported in LAN broadcasts.");
                return $"System.Nullable`1[[{underlying.FullName}, {MscorlibName}]]";
            }
            return type.FullName;
        }

        private static byte PrimitiveCode(Type type) {
            if (type == typeof(bool)) return PtBoolean;
            if (type == typeof(byte)) return PtByte;
            if (type == typeof(short)) return PtInt16;
            if (type == typeof(int)) return PtInt32;
            if (type == typeof(long)) return PtInt64;
            if (type == typeof(double)) return PtDouble;
            if (type == typeof(DateTime)) return PtDateTime;
            return 0;
        }

        private void WriteMemberValue(Type declaredType, object value) {
            if (value == null) {
                _out.Write(ObjectNull);
                return;
            }

            switch (BinaryTypeOf(declaredType)) {
                case BtString:
                    WriteString((string)value);
                    return;
                case BtPrimitive:
                    WritePrimitive(value);
                    return;
                case BtSystemClass:
                    // Value types are written inline; a boxed Nullable<T> is a boxed T.
                    if (value is Guid guid) WriteGuid(guid);
                    else if (value is DateTimeOffset dto) WriteDateTimeOffset(dto);
                    else throw new NotSupportedException($"Value of type {value.GetType()} is not supported in LAN broadcasts.");
                    return;
                case BtClass:
                    if (value.GetType().IsEnum) {
                        // BinaryFormatter gives inline value types a negative id.
                        WriteClassRecord(value, -(++_lastId));
                        return;
                    }
                    // Reference types are written after the current object, as BinaryFormatter does.
                    var isNew = !_referenceIds.ContainsKey(value);
                    var id = GetReferenceId(value);
                    if (isNew) _pending.Enqueue(value);
                    _out.Write(MemberReference);
                    _out.Write(id);
                    return;
            }
        }

        private void WriteString(string value) {
            if (_stringIds.TryGetValue(value, out var existing)) {
                _out.Write(MemberReference);
                _out.Write(existing);
                return;
            }
            var id = ++_lastId;
            _stringIds[value] = id;
            _out.Write(BinaryObjectString);
            _out.Write(id);
            _out.Write(value);
        }

        private void WritePrimitive(object value) {
            switch (value) {
                case bool b: _out.Write(b); break;
                case byte b: _out.Write(b); break;
                case short s: _out.Write(s); break;
                case int i: _out.Write(i); break;
                case long l: _out.Write(l); break;
                case double d: _out.Write(d); break;
                case DateTime dt: _out.Write(dt.Ticks | ((long)dt.Kind << 62)); break; // ticks, kind in the top two bits
                default: throw new NotSupportedException($"Primitive {value.GetType()} is not supported in LAN broadcasts.");
            }
        }

        private void WriteGuid(Guid guid) {
            _out.Write(SystemClassWithMembersAndTypes);
            _out.Write(-(++_lastId));
            _out.Write(typeof(Guid).FullName);
            _out.Write(GuidMembers.Length);
            foreach (var name in GuidMembers) _out.Write(name);
            foreach (var _ in GuidMembers) _out.Write(BtPrimitive);
            foreach (var code in GuidMemberTypes) _out.Write(code);
            // Guid.ToByteArray: _a, _b and _c little-endian, then _d to _k.
            _out.Write(guid.ToByteArray());
        }

        private void WriteDateTimeOffset(DateTimeOffset value) {
            // DateTimeOffset is ISerializable: its GetObjectData stores the UTC date and the offset in minutes.
            _out.Write(SystemClassWithMembersAndTypes);
            _out.Write(-(++_lastId));
            _out.Write(typeof(DateTimeOffset).FullName);
            _out.Write(2);
            _out.Write("DateTime");
            _out.Write("OffsetMinutes");
            _out.Write(BtPrimitive);
            _out.Write(BtPrimitive);
            _out.Write(PtDateTime);
            _out.Write(PtInt16);
            _out.Write(value.UtcDateTime.Ticks); // DateTimeKind.Unspecified, as stored by DateTimeOffset
            _out.Write((short)value.Offset.TotalMinutes);
        }
    }
}

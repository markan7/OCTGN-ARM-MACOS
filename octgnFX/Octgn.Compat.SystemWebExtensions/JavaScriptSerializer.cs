// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace System.Web.Script.Serialization
{
    /// <summary>
    /// The part of .NET Framework's JavaScriptSerializer (System.Web.Extensions) that game scripts use,
    /// for .NET 10, where that assembly does not exist. Game scripts load it with
    /// <c>clr.AddReference('System.Web.Extensions')</c>, mostly to read decks exported as JSON.
    /// Results have the same shapes as the original: an object is a Dictionary&lt;string, object&gt;,
    /// an array is an object[], a number is an int, a long, a decimal or a double (the first one
    /// that holds it), and a "\/Date(milliseconds)\/" string is a UTC DateTime.
    /// </summary>
    public class JavaScriptSerializer
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public JavaScriptSerializer() {
        }

        /// <summary>Maximum length of a JSON string, in characters (the original's default).</summary>
        public int MaxJsonLength { get; set; } = 2097152;

        /// <summary>Maximum nesting depth (the original's default).</summary>
        public int RecursionLimit { get; set; } = 100;

        public object DeserializeObject(string input) {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Length > MaxJsonLength)
                throw new ArgumentException("The length of the string exceeds the value set on the maxJsonLength property.", nameof(input));
            var options = new JsonDocumentOptions {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = RecursionLimit,
            };
            try {
                using (var document = JsonDocument.Parse(input, options))
                    return ToObject(document.RootElement);
            } catch (JsonException e) {
                throw new ArgumentException("Invalid JSON primitive: " + e.Message, nameof(input), e);
            }
        }

        public T Deserialize<T>(string input) {
            return (T)Deserialize(input, typeof(T));
        }

        public object Deserialize(string input, Type targetType) {
            return ConvertToType(DeserializeObject(input), targetType);
        }

        public T ConvertToType<T>(object obj) {
            return (T)ConvertToType(obj, typeof(T));
        }

        public object ConvertToType(object obj, Type targetType) {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            return Convert(obj, targetType, 0);
        }

        public string Serialize(object obj) {
            var output = new StringBuilder();
            Serialize(obj, output);
            return output.ToString();
        }

        public void Serialize(object obj, StringBuilder output) {
            if (output == null) throw new ArgumentNullException(nameof(output));
            using (var stream = new MemoryStream()) {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default })) {
                    Write(writer, obj, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
                }
                var json = Encoding.UTF8.GetString(stream.ToArray());
                if (json.Length > MaxJsonLength)
                    throw new InvalidOperationException("The length of the string exceeds the value set on the maxJsonLength property.");
                output.Append(json);
            }
        }

        #region Reading

        private static object ToObject(JsonElement element) {
            switch (element.ValueKind) {
                case JsonValueKind.Object:
                    var dictionary = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                        dictionary[property.Name] = ToObject(property.Value);
                    return dictionary;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(ToObject).ToArray();
                case JsonValueKind.String:
                    return TryReadDate(element, out var date) ? (object)date : element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var i)) return i;
                    if (element.TryGetInt64(out var l)) return l;
                    if (element.TryGetDecimal(out var m)) return m;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }

        // The original reads "\/Date(1234567890123)\/" (with the escaped slashes) as a date.
        private static bool TryReadDate(JsonElement element, out DateTime date) {
            date = default;
            var raw = element.GetRawText();
            const string prefix = "\"\\/Date(";
            const string suffix = ")\\/\"";
            if (!raw.StartsWith(prefix, StringComparison.Ordinal) || !raw.EndsWith(suffix, StringComparison.Ordinal)) return false;
            var value = raw.Substring(prefix.Length, raw.Length - prefix.Length - suffix.Length);
            // An offset ("+0100") may follow the milliseconds: the instant is the same.
            var end = value.IndexOfAny(new[] { '+', '-' }, 1);
            if (end > 0) value = value.Substring(0, end);
            if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds)) return false;
            date = Epoch.AddMilliseconds(milliseconds);
            return true;
        }

        private object Convert(object value, Type type, int depth) {
            if (depth > RecursionLimit) throw new ArgumentException("RecursionLimit exceeded.");
            var underlying = Nullable.GetUnderlyingType(type);
            if (value == null) {
                if (!type.IsValueType || underlying != null) return null;
                throw new InvalidOperationException($"Cannot convert null to a value type ({type}).");
            }
            if (underlying != null) type = underlying;
            if (type.IsInstanceOfType(value) && !(value is object[] && type != typeof(object[]) && type != typeof(object))) return value;
            if (type.IsEnum) return value is string s ? Enum.Parse(type, s, true) : Enum.ToObject(type, value);
            if (type == typeof(Guid)) return new Guid((string)value);
            if (type == typeof(string)) return System.Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is IConvertible && (type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime)))
                return System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);

            if (value is Dictionary<string, object> map) {
                var dictionaryType = FindGenericInterface(type, typeof(IDictionary<,>));
                if (dictionaryType != null || typeof(IDictionary).IsAssignableFrom(type)) {
                    var valueType = dictionaryType?.GetGenericArguments()[1] ?? typeof(object);
                    var concrete = type.IsInterface || type.IsAbstract
                        ? typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType)
                        : type;
                    var result = (IDictionary)Activator.CreateInstance(concrete);
                    foreach (var pair in map) result[pair.Key] = Convert(pair.Value, valueType, depth + 1);
                    return result;
                }
                var instance = Activator.CreateInstance(type);
                foreach (var pair in map) {
                    var property = type.GetProperty(pair.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0) {
                        property.SetValue(instance, Convert(pair.Value, property.PropertyType, depth + 1));
                        continue;
                    }
                    var field = type.GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (field != null && !field.IsInitOnly) field.SetValue(instance, Convert(pair.Value, field.FieldType, depth + 1));
                }
                return instance;
            }

            if (value is object[] items) {
                if (type.IsArray) {
                    var elementType = type.GetElementType();
                    var array = Array.CreateInstance(elementType, items.Length);
                    for (var index = 0; index < items.Length; index++) array.SetValue(Convert(items[index], elementType, depth + 1), index);
                    return array;
                }
                var listType = FindGenericInterface(type, typeof(IEnumerable<>));
                var itemType = listType?.GetGenericArguments()[0] ?? typeof(object);
                var concrete = type.IsInterface || type.IsAbstract ? typeof(List<>).MakeGenericType(itemType) : type;
                var list = (IList)Activator.CreateInstance(concrete);
                foreach (var item in items) list.Add(Convert(item, itemType, depth + 1));
                return list;
            }

            throw new InvalidOperationException($"Cannot convert {value.GetType()} to {type}.");
        }

        private static Type FindGenericInterface(Type type, Type definition) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == definition) return type;
            return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition);
        }

        #endregion

        #region Writing

        private void Write(Utf8JsonWriter writer, object value, int depth, HashSet<object> parents) {
            if (depth > RecursionLimit) throw new ArgumentException("RecursionLimit exceeded.");
            switch (value) {
                case null:
                case DBNull _:
                    writer.WriteNullValue();
                    return;
                case string s:
                    writer.WriteStringValue(s);
                    return;
                case char c:
                    writer.WriteStringValue(c.ToString());
                    return;
                case bool b:
                    writer.WriteBooleanValue(b);
                    return;
                case Enum e:
                    writer.WriteNumberValue(System.Convert.ToInt64(e, CultureInfo.InvariantCulture));
                    return;
                case DateTime d:
                    // The original's format, which it reads back as a date.
                    var milliseconds = (long)(d.ToUniversalTime() - Epoch).TotalMilliseconds;
                    writer.WriteRawValue("\"\\/Date(" + milliseconds.ToString(CultureInfo.InvariantCulture) + ")\\/\"", skipInputValidation: true);
                    return;
                case Guid g:
                    writer.WriteStringValue(g.ToString());
                    return;
                case Uri u:
                    writer.WriteStringValue(u.OriginalString);
                    return;
                case BigInteger big: // Python's long
                    writer.WriteRawValue(big.ToString(CultureInfo.InvariantCulture), skipInputValidation: true);
                    return;
                case float f:
                    writer.WriteNumberValue(f);
                    return;
                case double dbl:
                    writer.WriteNumberValue(dbl);
                    return;
                case decimal dec:
                    writer.WriteNumberValue(dec);
                    return;
                case IConvertible number when IsInteger(number):
                    writer.WriteRawValue(System.Convert.ToString(number, CultureInfo.InvariantCulture), skipInputValidation: true);
                    return;
            }

            if (!value.GetType().IsValueType && !parents.Add(value))
                throw new InvalidOperationException("A circular reference was detected while serializing an object of type " + value.GetType() + ".");
            try {
                switch (value) {
                    case IDictionary dictionary: // Also Python's dict
                        writer.WriteStartObject();
                        foreach (DictionaryEntry entry in dictionary) {
                            writer.WritePropertyName(System.Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                            Write(writer, entry.Value, depth + 1, parents);
                        }
                        writer.WriteEndObject();
                        return;
                    case IEnumerable sequence: // Also Python's list and tuple
                        writer.WriteStartArray();
                        foreach (var item in sequence) Write(writer, item, depth + 1, parents);
                        writer.WriteEndArray();
                        return;
                }

                writer.WriteStartObject();
                var type = value.GetType();
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.GetValue(value), depth + 1, parents);
                }
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                    writer.WritePropertyName(field.Name);
                    Write(writer, field.GetValue(value), depth + 1, parents);
                }
                writer.WriteEndObject();
            } finally {
                parents.Remove(value);
            }
        }

        private static bool IsInteger(IConvertible value) {
            switch (value.GetTypeCode()) {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }

        #endregion
    }
}

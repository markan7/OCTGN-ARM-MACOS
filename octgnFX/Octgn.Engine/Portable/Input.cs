// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Octgn.Portable
{
    /// <summary>The modifier keys of a <see cref="KeyGesture"/> (System.Windows.Input.ModifierKeys).</summary>
    [Flags]
    public enum ModifierKeys
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8,
    }

    /// <summary>
    /// A keyboard shortcut of the game definition ("Ctrl+D", "F5", "Shift+Del"), in place of WPF's
    /// System.Windows.Input.KeyGesture. <see cref="Key"/> is the name of the key in WPF's Key enumeration
    /// ("D", "D1", "F5", "Delete", "Return"), which Avalonia's Avalonia.Input.Key also uses.
    /// </summary>
    public sealed class KeyGesture : IEquatable<KeyGesture>
    {
        public KeyGesture(string key, ModifierKeys modifiers, string displayString = null) {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Modifiers = modifiers;
            DisplayString = displayString ?? ToString();
        }

        public string Key { get; }

        public ModifierKeys Modifiers { get; }

        /// <summary>The shortcut as the game wrote it (shown in menus).</summary>
        public string DisplayString { get; }

        public string GetDisplayStringForCulture(CultureInfo culture) {
            return DisplayString;
        }

        public bool Equals(KeyGesture other) {
            return other != null && Modifiers == other.Modifiers && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) {
            return Equals(obj as KeyGesture);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Modifiers, Key.ToUpperInvariant());
        }

        public override string ToString() {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
            parts.Add(Key);
            return string.Join("+", parts);
        }
    }

    /// <summary>
    /// Reads a <see cref="KeyGesture"/> from its text, as WPF's KeyGestureConverter: modifiers and key separated
    /// by '+', with the key names and abbreviations of WPF's KeyConverter ("Del", "Esc", "PgUp", "0" to "9").
    /// </summary>
    public sealed class KeyGestureConverter
    {
        private static readonly Dictionary<string, string> KeyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["Enter"] = "Return",
            ["Esc"] = "Escape",
            ["PgUp"] = "PageUp",
            ["PgDn"] = "PageDown",
            ["PageDown"] = "PageDown",
            ["Next"] = "PageDown",
            ["Prior"] = "PageUp",
            ["Ins"] = "Insert",
            ["Del"] = "Delete",
            ["Backspace"] = "Back",
            ["Bksp"] = "Back",
            ["Bksp."] = "Back",
            ["Break"] = "Cancel",
            ["Win"] = "LWin",
            ["+"] = "OemPlus",
            ["-"] = "OemMinus",
            ["Plus"] = "OemPlus",
            ["Minus"] = "OemMinus",
            ["Spacebar"] = "Space",
        };

        private static readonly Dictionary<string, ModifierKeys> ModifierNames = new Dictionary<string, ModifierKeys>(StringComparer.OrdinalIgnoreCase) {
            ["Ctrl"] = ModifierKeys.Control,
            ["Control"] = ModifierKeys.Control,
            ["Shift"] = ModifierKeys.Shift,
            ["Alt"] = ModifierKeys.Alt,
            ["Win"] = ModifierKeys.Windows,
            ["Windows"] = ModifierKeys.Windows,
        };

        /// <summary>The shortcut, or NotSupportedException when the text is not one (as WPF).</summary>
        public object ConvertFromInvariantString(string text) {
            return Parse(text);
        }

        public static KeyGesture Parse(string text) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var trimmed = text.Trim();
            if (trimmed.Length == 0) throw new NotSupportedException("An empty text is not a keyboard shortcut.");

            // The key is after the last '+', unless the key itself is '+' ("Ctrl++").
            string keyText;
            string modifiersText;
            if (trimmed.EndsWith("++", StringComparison.Ordinal) || trimmed == "+") {
                keyText = "+";
                modifiersText = trimmed.Substring(0, trimmed.Length - 1).TrimEnd('+');
            } else {
                var plus = trimmed.LastIndexOf('+');
                keyText = trimmed.Substring(plus + 1).Trim();
                modifiersText = plus < 0 ? string.Empty : trimmed.Substring(0, plus);
            }

            var modifiers = ModifierKeys.None;
            foreach (var part in modifiersText.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim())) {
                if (!ModifierNames.TryGetValue(part, out var modifier))
                    throw new NotSupportedException($"'{part}' is not a modifier key in the shortcut '{text}'.");
                modifiers |= modifier;
            }

            return new KeyGesture(NormalizeKey(keyText, text), modifiers, trimmed);
        }

        private static string NormalizeKey(string key, string text) {
            if (key.Length == 0) throw new NotSupportedException($"The shortcut '{text}' has no key.");
            if (KeyAliases.TryGetValue(key, out var alias)) return alias;
            if (key.Length == 1) {
                var c = char.ToUpperInvariant(key[0]);
                if (c >= 'A' && c <= 'Z') return c.ToString();
                if (c >= '0' && c <= '9') return "D" + c;
            }
            // Other keys go by their name in WPF's Key enumeration (F5, Tab, Home, NumPad1, OemComma...).
            if (key.All(char.IsLetterOrDigit))
                return char.ToUpperInvariant(key[0]) + key.Substring(1);
            throw new NotSupportedException($"'{key}' is not a key in the shortcut '{text}'.");
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Globalization;
using System.Linq;
using Octgn.Core.Play;

namespace Octgn.Portable
{
    /// <summary>
    /// The text of a game message (chat, notifications, game events), for the game log and the chat of a
    /// user interface. The WPF client builds rich text instead (ChatControl.GameMessageToBlock).
    /// </summary>
    public static class GameMessageText
    {
        /// <summary>"[12:34:56] Alice: hello", or "[12:34:56] text" for messages without a speaker.</summary>
        public static string Format(IGameMessage message) {
            var time = message.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var text = Body(message);
            return message is PlayerEventMessage || message is ChatMessage
                ? $"[{time}] {Speaker(message)}{text}"
                : $"[{time}] {text}";
        }

        /// <summary>The message without time and speaker, its arguments formatted (players and cards by name).</summary>
        public static string Body(IGameMessage message) {
            var args = message.Arguments ?? Array.Empty<object>();
            if (args.Length == 0) return message.Message ?? string.Empty;
            try {
                return string.Format(CultureInfo.CurrentCulture, message.Message ?? string.Empty, args.Select(Describe).ToArray());
            } catch (FormatException) {
                return message.Message ?? string.Empty;
            }
        }

        private static string Speaker(IGameMessage message) {
            if (message is ChatMessage) return (message.From?.Name ?? "?") + ": ";
            if (message is PlayerEventMessage) return (message.From?.Name ?? "?") + " ";
            return string.Empty;
        }

        private static object Describe(object argument) {
            switch (argument) {
                case null:
                    return string.Empty;
                case IPlayPlayer player:
                    return player.Name;
                default:
                    return argument;
            }
        }
    }
}

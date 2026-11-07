// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using Octgn.DataNew.Entities;

namespace Octgn.Headless
{
    /// <summary>
    /// The "_api" object of the Python scripts, reduced to what the game definition's scripts need at load
    /// time and in the actions the proof of concept runs. Same method names and signatures as
    /// Octgn.JodsEngine/Scripting/Versions/Script_3_1_0_2.cs; Random and Notify go through the game server,
    /// as in the WPF engine.
    /// </summary>
    public sealed class HeadlessScriptApi
    {
        private readonly Game _game;
        private readonly MinimalGameClient _client;
        private readonly Func<int, string> _playerName;
        private readonly Func<IEnumerable<int>> _players;
        private readonly Action<string> _output;

        internal HeadlessScriptApi(Game game, MinimalGameClient client, byte localPlayerId,
                                   Func<int, string> playerName, Func<IEnumerable<int>> players, Action<string> output) {
            _game = game;
            _client = client;
            LocalPlayer = localPlayerId;
            _playerName = playerName;
            _players = players;
            _output = output;
        }

        public byte LocalPlayer { get; }
        public bool Muted { get; private set; }
        public List<string> Notifications { get; } = new List<string>();
        public List<int> RandomResults { get; } = new List<int>();

        public int LocalPlayerId() => LocalPlayer;

        public int SharedPlayerId() => -1; // The sample game has no shared (global) player.

        public List<int> AllPlayers() => _players().ToList();

        public string PlayerName(int id) => _playerName(id);

        public int? GetActivePlayer() => null;

        // Group and counter ids follow the engine: 0x01000000 is the table, then (player << 16) | index.
        public List<KeyValuePair<int, string>> PlayerCounters(int id) {
            return _game.Player.Counters
                .Select((c, i) => new KeyValuePair<int, string>((id << 16) | (i + 1), c.Name))
                .ToList();
        }

        public List<KeyValuePair<int, string>> PlayerPiles(int id) {
            return _game.Player.Groups
                .Select((g, i) => new KeyValuePair<int, string>(0x01000000 | (id << 16) | (i + 1), g.Name))
                .ToList();
        }

        public int GroupCount(int id) => 0;

        public string[] CardProperties() => _game.CardProperties.Keys.ToArray();

        public string OCTGN_Version() => typeof(Octgn.Library.Config).Assembly.GetName().Version.ToString();

        public string GameDef_Version() => _game.Version.ToString();

        public string GetGameName() => _game.Name;

        public void Mute(bool muted) {
            Muted = muted;
        }

        /// <summary>Asks the server, like the engine: every player sees the same result.</summary>
        public int Random(int min, int max) {
            _client.SendRandomRequest(min, max);
            var result = (int)_client.WaitFor(MinimalGameClient.Random, TimeSpan.FromSeconds(5)).Args[0];
            RandomResults.Add(result);
            return result;
        }

        public void Notify(string message) {
            Notifications.Add(message);
            _client.SendPrintRequest(message);
            _output(message);
        }

        /// <summary>Messages for the local player only (not sent to the server).</summary>
        public void Whisper(string message) {
            _output("(whisper) " + message);
        }

        /// <summary>No one to ask: answers yes, as a player who accepts every prompt.</summary>
        public bool Confirm(string message) {
            Confirmations.Add(message);
            _output("(confirm, answered yes) " + message.Split('\n')[0]);
            return true;
        }

        public List<string> Confirmations { get; } = new List<string>();

        #region Settings (the game's local preferences, kept in memory)

        private readonly Dictionary<string, object> _settings = new Dictionary<string, object>();

        public object GetSetting(string name, object defaultValue) {
            return _settings.TryGetValue(name, out var value) ? value : defaultValue;
        }

        public void SaveSetting(string name, object value) {
            _settings[name] = value;
        }

        #endregion

        #region Global variables

        private Dictionary<string, string> _globalVariables;

        /// <summary>Values of the game's global variables, starting with the game definition's.</summary>
        public Dictionary<string, string> GlobalVariables =>
            _globalVariables ?? (_globalVariables = _game.GlobalVariables.Values.ToDictionary(v => v.Name, v => v.Value));

        /// <summary>Names sent to the server with SetGlobalVariable, in order.</summary>
        public List<string> GlobalVariablesSent { get; } = new List<string>();

        public string GetGlobalVariable(string name) {
            if (GlobalVariables.TryGetValue(name, out var value)) return value;
            _output($"(warning) Global variable '{name}' isn't defined");
            return "";
        }

        /// <summary>As the engine: changed locally, then sent to the server, which tells every player.</summary>
        public void SetGlobalVariable(string name, object value) {
            var text = string.Format("{0}", value);
            GlobalVariables.TryGetValue(name, out var oldValue);
            GlobalVariables[name] = text;
            GlobalVariablesSent.Add(name);
            _client.SendSetGlobalVariable(name, oldValue ?? "", text);
        }

        #endregion

        /// <summary>The engine waits for its queued actions; this API runs everything synchronously.</summary>
        public void Update() {
        }
    }
}

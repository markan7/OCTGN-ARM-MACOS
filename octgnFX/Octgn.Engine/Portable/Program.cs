// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using log4net;
using Player = Octgn.Play.Player;

namespace Octgn
{
    // The members of the WPF engine's Program (Program.cs) that open windows, in this build.
    public static partial class Program
    {
        /// <summary>
        /// The user interface the engine asks to show things and ask questions (PlayWindow and the WPF dialogs
        /// in the Windows client). A program without user interface keeps the <see cref="HeadlessPlayUi"/>.
        /// </summary>
        public static IPlayUi Ui { get; set; } = new HeadlessPlayUi();

        /// <summary>The game is over for this client: leave it (Program.Exit without closing the application).</summary>
        public static void Exit() {
            try {
                Client?.Rpc?.Leave(Player.LocalPlayer);
            } catch (Exception e) {
                Log.Error("Exit() Player leave exception", e);
            }
        }

        public static void LaunchUrl(string url) {
            if (url == null) return;
            Ui?.LaunchUrl(url);
        }

        public static void DoCrazyException(Exception e, string action) {
            Log.Error(action, e);
            Ui?.ShowError(action);
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System.Globalization;
using Octgn.Play.Gui;
using Group = Octgn.Play.Group;

namespace Octgn.Scripting
{
    // What the script API versions do with WPF windows (Scripting/Versions/Script_*.cs), in this build.
    public abstract partial class ScriptApi
    {
        /// <summary>
        /// lookAt in the scripts: shows the top or bottom <paramref name="value"/> cards of a pile, all of them
        /// when <paramref name="value"/> is negative, or as many as the user says when it is 0 (GroupWindow in WPF).
        /// </summary>
        protected void ShowGroupWindow(Group group, bool isTop, int value) {
            var position = isTop ? PilePosition.Top : PilePosition.Bottom;
            if (value > 0) {
                QueueAction(() => Program.Ui.ShowGroup(group, position, value));
            } else if (value == 0) {
                var title = isTop ? "View top cards" : "View bottom cards";
                var count = QueueAction<int?>(() => Program.Ui.AskInteger(title, "How many cards do you want to see?", 1));
                if (count == null) return;
                QueueAction(() => Program.Ui.ShowGroup(group, position, count.Value));
            } else {
                QueueAction(() => Program.Ui.ShowGroup(group, PilePosition.All, 0));
            }
        }
    }
}

namespace Octgn.Portable
{
    /// <summary>The simple questions of the scripts (Octgn.Scripting.Controls.Dialog in WPF), asked by <see cref="Program.Ui"/>.</summary>
    public static class Dialog
    {
        public static int InputPositiveInt(string title, string prompt, int n) {
            return Program.Ui.AskInteger(title, prompt, n) ?? n;
        }

        public static bool Confirm(string prompt) {
            return Program.Ui.Confirm(prompt);
        }
    }
}

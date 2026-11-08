// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

// Same-named stand-ins for the few classes of the WPF engine that the shared code calls as services:
// what they do here goes through Program.Ui (see IPlayUi), so the calls stay as they are in the shared files.

using System;
using System.IO;
using Octgn.DataNew.Entities;

namespace Octgn.Play.Gui
{
    /// <summary>Which cards of a pile a pile view shows (defined with GroupWindow in WPF).</summary>
    public enum PilePosition
    {
        All,
        Top,
        Bottom
    }
}

namespace Octgn.Utils
{
    /// <summary>The sounds of the client (Utils/Sounds.cs plays them with NAudio in WPF): played by <see cref="Program.Ui"/>.</summary>
    public static class Sounds
    {
        /// <summary>A sound file of the client (see <see cref="Octgn.Properties.Resources"/>).</summary>
        public static void PlaySound(string file, bool subRequired = false) {
            // Subscriber sounds (subRequired) are not played: this build does not check subscriptions.
            if (subRequired || file == null) return;
            Program.Ui?.PlaySound(file);
        }

        public static void PlayGameSound(GameSound sound) {
            if (sound == null) return;
            Program.Ui?.PlayGameSound(sound);
        }

        public static void Close() {
        }
    }
}

namespace Octgn.Properties
{
    /// <summary>
    /// The resources of the WPF engine (Properties/Resources.resx) the shared code uses, as files next to
    /// the assembly: the sounds are file paths, the Python text is read from its file.
    /// </summary>
    internal static class Resources
    {
        private static string File(params string[] parts) {
            return Path.Combine(AppContext.BaseDirectory, Path.Combine(parts));
        }

        internal static string userjoinsroom => File("Resources", "userjoinsroom.wav");

        internal static string knockknock => File("Resources", "knockknock.wav");

        internal static string doorclose => File("Resources", "doorclose.wav");

        internal static string CaseInsensitiveDict => System.IO.File.ReadAllText(File("Scripting", "CaseInsensitiveDict.py"));
    }
}

namespace Octgn
{
    /// <summary>
    /// The OCTGN subscription of the user (SubscriptionModule.cs in WPF, through the site's API): unknown in
    /// this build, which does not check it. Subscriber features (deck sleeves in online games, subscriber
    /// sounds) are off.
    /// </summary>
    public sealed class SubscriptionModule
    {
        private static readonly SubscriptionModule Instance = new SubscriptionModule();

        public static SubscriptionModule Get() {
            return Instance;
        }

        /// <summary>Null: unknown.</summary>
        public bool? IsSubscribed => null;

#pragma warning disable 67
        public event Action<bool> IsSubbedChanged;
#pragma warning restore 67
    }
}

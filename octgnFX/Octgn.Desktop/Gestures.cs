// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;

namespace Octgn.Desktop
{
    /// <summary>
    /// The clicks as the platform has them. On macOS, Control + click is the secondary click (the context menu, as
    /// the right button or a two-finger click on a trackpad) and ⌘ + click adds a card to the selection or takes it
    /// out. On Windows and Linux, as in the WPF client, Ctrl + click does that.
    /// </summary>
    public static class Gestures
    {
        /// <summary>The macOS conventions are in use (the tests check both).</summary>
        public static bool MacConventions { get; set; } = OperatingSystem.IsMacOS();

        /// <summary>A press that opens the context menu: the right button, or Control + click on macOS.</summary>
        public static bool IsContextClick(PointerPoint point, KeyModifiers modifiers) =>
            point.Properties.IsRightButtonPressed
            || (MacConventions && point.Properties.IsLeftButtonPressed && (modifiers & KeyModifiers.Control) != 0);

        /// <summary>A click that adds the card to the selection or takes it out: ⌘ on macOS, Ctrl elsewhere.</summary>
        public static bool TogglesSelection(KeyModifiers modifiers) =>
            MacConventions
                ? (modifiers & KeyModifiers.Meta) != 0
                : (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        /// <summary>Raised when a menu of game actions opens (for the tests).</summary>
        public static event Action<ContextMenu> MenuOpened;

        /// <summary>Opens a menu of game actions at the pointer, over a control. Nothing happens without items.</summary>
        public static void OpenMenu(Control owner, IList<Control> items) {
            if (items.Count == 0) return;
            var menu = new ContextMenu { ItemsSource = items };
            menu.Open(owner);
            MenuOpened?.Invoke(menu);
        }
    }
}

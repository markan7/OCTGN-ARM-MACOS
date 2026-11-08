// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;

namespace Octgn.Portable
{
    /// <summary>
    /// The WPF resources the game data refers to by "pack://application:,,,/..." URIs (the default card
    /// pictures Resources/Front.jpg and Resources/Back.jpg, the Python API Scripting/Versions/*.py): this
    /// build copies them next to its assembly, at the same relative path.
    /// </summary>
    public static class PackUri
    {
        public const string ApplicationPrefix = "pack://application:,,,/";

        public static bool IsPackUri(string uri) {
            return uri != null && uri.StartsWith(ApplicationPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The file of a pack URI of the application; any other text unchanged (a file path or URI).</summary>
        public static string ToFilePath(string uri) {
            if (!IsPackUri(uri)) return uri;
            var relative = Uri.UnescapeDataString(uri.Substring(ApplicationPrefix.Length))
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(AppContext.BaseDirectory, relative);
        }
    }
}

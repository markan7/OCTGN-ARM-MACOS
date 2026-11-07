// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Octgn.Headless
{
    /// <summary>The "Basic-Game" template of Octgn.GameStudio, made loadable.</summary>
    internal static class SampleGame
    {
        private static readonly Regex Placeholder = new Regex("SAMPLEID-GUID-0000-0000-[0-9A-Z]{12}");

        /// <summary>
        /// Copies the template (shipped next to the binaries) to <paramref name="target"/> and replaces its
        /// placeholder ids, which GameStudio normally fills in, by GUIDs. Returns the definition file.
        /// </summary>
        public static string Prepare(string target) {
            var source = Path.Combine(AppContext.BaseDirectory, "SampleGame");
            if (!File.Exists(Path.Combine(source, "definition.xml")))
                throw new FileNotFoundException("The game template is missing. Clone the repository with its submodules (git submodule update --init).", source);

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
            foreach (var xml in Directory.GetFiles(target, "*.xml", SearchOption.AllDirectories)) {
                var text = File.ReadAllText(xml);
                File.WriteAllText(xml, Placeholder.Replace(text, m => IdFor(m.Value).ToString()), new UTF8Encoding(true));
            }
            return Path.Combine(target, "definition.xml");
        }

        /// <summary>Same placeholder, same GUID: the set keeps pointing at its game.</summary>
        public static Guid IdFor(string placeholder) {
            using (var md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(placeholder)));
        }
    }
}

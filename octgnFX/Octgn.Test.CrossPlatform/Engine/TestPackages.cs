// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Xml.Linq;

namespace Octgn.Test.Engine
{
    /// <summary>The packages of a game folder, as OCTGN's tools make them (GameStudio, the image pack makers).</summary>
    public static class TestPackages
    {
        /// <summary>A picture of one pixel (PNG).</summary>
        public static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        /// <summary>
        /// The Basic-Game template (see <see cref="Headless.SampleGame"/>) as in the packages GameStudio makes: the
        /// folder of a set named after its id, as the game database finds a set (and the pictures of its cards).
        /// Returns the folder of the game.
        /// </summary>
        public static string SampleGame(string target) {
            var folder = Path.GetDirectoryName(Headless.SampleGame.Prepare(target));
            foreach (var set in Directory.GetDirectories(Path.Combine(folder, "Sets"))) {
                var id = (string)XDocument.Load(Path.Combine(set, "set.xml")).Root.Attribute("id");
                Directory.Move(set, Path.Combine(folder, "Sets", id));
            }
            return folder;
        }

        /// <summary>
        /// The game package (.o8g, or .nupkg as the OCTGN game feed serves it): a NuGet package whose id is the
        /// game's, the files of the game in its def folder, their names escaped as NuGet writes them (%20 for a space).
        /// </summary>
        public static string Game(string gameFolder, string path) {
            var game = XDocument.Load(Path.Combine(gameFolder, "definition.xml")).Root;
            var id = (string)game.Attribute("id");
            var name = SecurityElement.Escape((string)game.Attribute("name"));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                Text(zip, id + ".nuspec", $@"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
  <metadata>
    <id>{id}</id>
    <version>{(string)game.Attribute("version")}</version>
    <title>{name}</title>
    <authors>OCTGN tests</authors>
    <description>{name}</description>
  </metadata>
</package>");
                Text(zip, "[Content_Types].xml", "<Types />");
                Text(zip, "_rels/.rels", "<Relationships />");
                foreach (var file in Directory.GetFiles(gameFolder, "*", SearchOption.AllDirectories)) {
                    var parts = Path.GetRelativePath(gameFolder, file).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString);
                    zip.CreateEntryFromFile(file, "def/" + string.Join("/", parts));
                }
            }
            return path;
        }

        /// <summary>The package of the card images of a game (.o8c): game id/Sets/set id/Cards/card id.png.</summary>
        public static string Images(string gameFolder, string path) {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                foreach (var setFile in Directory.GetFiles(Path.Combine(gameFolder, "Sets"), "set.xml", SearchOption.AllDirectories)) {
                    var set = XDocument.Load(setFile).Root;
                    foreach (var card in set.Descendants("card")) {
                        var entry = zip.CreateEntry($"{(string)set.Attribute("gameId")}/Sets/{(string)set.Attribute("id")}/Cards/{(string)card.Attribute("id")}.png");
                        using (var stream = entry.Open()) stream.Write(Png, 0, Png.Length);
                    }
                }
            }
            return path;
        }

        private static void Text(ZipArchive zip, string name, string content) {
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new System.Text.UTF8Encoding(false)))
                writer.Write(content);
        }
    }
}

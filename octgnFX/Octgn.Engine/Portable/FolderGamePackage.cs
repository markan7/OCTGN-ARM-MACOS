/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Octgn.Core.Packaging;
using Octgn.DataNew.Entities;

namespace Octgn.Portable
{
    /// <summary>
    /// The folder of a game (the one of its definition.xml, as in the game's repository) as its package would
    /// be, as o8build makes it: definition.xml and the folders of the game in def, the folder of each set named
    /// after the id of the set. The game database finds a set by that name: a set in a folder named otherwise
    /// ("Ant-Man", "fear_no_evil_by_ffg") has its cards, but no set for them, nor their pictures.
    /// </summary>
    internal sealed class FolderGamePackage : IPackage
    {
        private readonly string _folder;

        public FolderGamePackage(string folder, Game game) {
            _folder = Path.GetFullPath(folder);
            Id = game.Id.ToString();
            Version = new SemanticVersion(game.Version.ToString());
            Title = game.Name;
            Description = game.Description;
            Authors = game.Authors ?? new List<string>();
            Tags = string.Join(" ", game.Tags ?? new List<string>());
            IconUrl = Uri.TryCreate(game.IconUrl, UriKind.Absolute, out var icon) ? icon : null;
            ProjectUrl = Uri.TryCreate(game.GameUrl, UriKind.Absolute, out var url) ? url : null;
        }

        public string Id { get; }
        public SemanticVersion Version { get; }
        public string Title { get; }
        public string Description { get; }
        public string Summary => Description;
        public IEnumerable<string> Authors { get; }
        public string Tags { get; }
        public Uri IconUrl { get; }
        public Uri ProjectUrl { get; }
        public DateTimeOffset? Published => null;
        public int DownloadCount => 0;
        public bool IsLatestVersion => true;
        public bool IsAbsoluteLatestVersion => true;

        public IEnumerable<IPackageFile> GetFiles() {
            yield return new File(Path.Combine("def", "definition.xml"), Path.Combine(_folder, "definition.xml"));
            foreach (var directory in Directory.GetDirectories(_folder)) {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".", StringComparison.Ordinal)) continue; // .git, .github
                if (name == "Sets") {
                    foreach (var set in Directory.GetDirectories(directory)) {
                        var target = Path.Combine("def", "Sets", SetId(set) ?? Path.GetFileName(set));
                        foreach (var file in Directory.GetFiles(set, "*", SearchOption.AllDirectories))
                            yield return new File(Path.Combine(target, Path.GetRelativePath(set, file)), file);
                    }
                } else {
                    foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                        yield return new File(Path.Combine("def", Path.GetRelativePath(_folder, file)), file);
                }
            }
        }

        /// <summary>The id of a set, from its set.xml; null without one.</summary>
        private static string SetId(string folder) {
            var file = Path.Combine(folder, "set.xml");
            if (!System.IO.File.Exists(file)) return null;
            var id = (string)XDocument.Load(file).Root?.Attribute("id");
            return Guid.TryParse(id, out var guid) ? guid.ToString() : null;
        }

        private sealed class File : IPackageFile
        {
            private readonly string _source;

            public File(string path, string source) {
                Path = path;
                _source = source;
            }

            public string Path { get; }
            public Stream GetStream() => System.IO.File.OpenRead(_source);
        }
    }
}

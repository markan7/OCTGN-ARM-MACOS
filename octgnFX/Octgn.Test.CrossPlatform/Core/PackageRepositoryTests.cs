// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using NUnit.Framework;
using Octgn.Core;
using Octgn.Core.Packaging;

namespace Octgn.Test.Core
{
    [TestFixture]
    public class PackageRepositoryTests
    {
        private string _feed;

        [SetUp]
        public void SetUp() {
            _feed = Path.Combine(Path.GetTempPath(), "octgn-feed-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_feed);
        }

        [TearDown]
        public void TearDown() {
            if (Directory.Exists(_feed)) Directory.Delete(_feed, true);
        }

        /// <summary>Writes a game package the way NuGet lays it out (metadata parts, escaped entry names).</summary>
        private string WritePackage(string id, string version, string folder = null) {
            var directory = folder == null ? _feed : Path.Combine(_feed, folder);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{id}.{version}.nupkg");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                void Entry(string name, string content) {
                    using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)))
                        writer.Write(content);
                }
                Entry($"{id}.nuspec", $@"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
  <metadata>
    <id>{id}</id>
    <version>{version}</version>
    <title>Title of {id}</title>
    <authors>Alice, Bob</authors>
    <description>Description of {id}</description>
    <iconUrl>https://example.com/{id}.png</iconUrl>
  </metadata>
</package>");
                Entry("[Content_Types].xml", "<Types />");
                Entry("_rels/.rels", "<Relationships />");
                Entry("package/services/metadata/core-properties/0.psmdcp", "<coreProperties />");
                Entry("def/definition.xml", $"<game id=\"{id}\" version=\"{version}\" />");
                Entry("def/Sets/My%20Set/set.xml", "<set />");
            }
            return path;
        }

        [Test]
        public void LocalFeed_ListsEveryVersionAndFlagsTheLatest() {
            WritePackage("game-one", "1.0.0");
            WritePackage("game-one", "1.2.0", "sub");
            WritePackage("game-one", "2.0.0-beta");
            WritePackage("game-two", "3.1.0");

            var packages = PackageRepositoryFactory.Default.CreateRepository(_feed).GetPackages().ToList();

            Assert.AreEqual(4, packages.Count);
            IPackage Get(string id, string version) => packages.Single(p => p.Id == id && p.Version.ToString() == version);
            Assert.IsTrue(Get("game-one", "2.0.0-beta").IsAbsoluteLatestVersion);
            Assert.IsFalse(Get("game-one", "2.0.0-beta").IsLatestVersion);
            Assert.IsTrue(Get("game-one", "1.2.0").IsLatestVersion);
            Assert.IsFalse(Get("game-one", "1.2.0").IsAbsoluteLatestVersion);
            Assert.IsFalse(Get("game-one", "1.0.0").IsLatestVersion || Get("game-one", "1.0.0").IsAbsoluteLatestVersion);
            Assert.IsTrue(Get("game-two", "3.1.0").IsLatestVersion && Get("game-two", "3.1.0").IsAbsoluteLatestVersion);

            var two = Get("game-two", "3.1.0");
            Assert.AreEqual("Title of game-two", two.Title);
            CollectionAssert.AreEqual(new[] { "Alice", "Bob" }, two.Authors);
            Assert.AreEqual(new Uri("https://example.com/game-two.png"), two.IconUrl);
        }

        [Test]
        public void LocalFeed_FindPackagesIgnoresCase() {
            WritePackage("Game-One", "1.0.0");
            WritePackage("game-two", "1.0.0");
            var found = new LocalPackageRepository(_feed).FindPackages(new[] { "GAME-ONE" }).ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Game-One", found[0].Id);
        }

        [Test]
        public void LocalFeed_MissingFolderIsEmpty() {
            CollectionAssert.IsEmpty(new LocalPackageRepository(Path.Combine(_feed, "missing")).GetPackages());
        }

        [Test]
        public void LocalFeed_InvalidPackageThrows() {
            File.WriteAllText(Path.Combine(_feed, "broken.1.0.0.nupkg"), "not a zip");
            Assert.Catch(() => new LocalPackageRepository(_feed).GetPackages());
        }

        [Test]
        public void GetFiles_SkipsNuGetMetadataAndUnescapesNames() {
            WritePackage("game-one", "1.0.0");
            var package = new LocalPackageRepository(_feed).GetPackages().Single();

            var files = package.GetFiles().ToDictionary(f => f.Path);

            CollectionAssert.AreEquivalent(new[] {
                Path.Combine("def", "definition.xml"),
                Path.Combine("def", "Sets", "My Set", "set.xml"),
            }, files.Keys);
            using (var reader = new StreamReader(files[Path.Combine("def", "definition.xml")].GetStream()))
                Assert.AreEqual("<game id=\"game-one\" version=\"1.0.0\" />", reader.ReadToEnd());
        }

        [Test]
        public void GameFeedManager_ExtractsAPackage() {
            WritePackage("game-one", "1.0.0");
            var package = new LocalPackageRepository(_feed).GetPackages().Single();
            var target = Path.Combine(_feed, "extracted");

            GameFeedManager.Get().ExtractPackage(target, package);

            FileAssert.Exists(Path.Combine(target, "def", "definition.xml"));
            FileAssert.Exists(Path.Combine(target, "def", "Sets", "My Set", "set.xml"));
            Assert.IsFalse(File.Exists(Path.Combine(target, "game-one.nuspec")));
        }

        [Test]
        public void Factory_HttpIsAFeedAnythingElseAFolder() {
            Assert.IsInstanceOf<NuGetFeedRepository>(PackageRepositoryFactory.Default.CreateRepository("https://www.myget.org/F/octgngames/"));
            Assert.IsInstanceOf<LocalPackageRepository>(PackageRepositoryFactory.Default.CreateRepository(_feed));
            var fromUri = PackageRepositoryFactory.Default.CreateRepository(new Uri(_feed).AbsoluteUri);
            Assert.IsInstanceOf<LocalPackageRepository>(fromUri);
            Assert.AreEqual(Path.GetFullPath(_feed), Path.GetFullPath(fromUri.Source));
        }

        [Test]
        public void RemoteFeed_UnreachableIsAWebException() {
            // Port 9 (discard) is closed on the loopback address: the connection is refused at once.
            var repository = new NuGetFeedRepository("http://127.0.0.1:9/nuget/");
            Assert.Catch<WebException>(() => repository.GetPackages());
        }

        [TestCase("https://www.myget.org/F/octgngames/", true)]
        [TestCase("http://localhost:8080/feed", true)]
        [TestCase("sdflakwejfasw", false)]
        [TestCase("/home/user/feed", false)]
        public void PathValidator_IsValidUrl(string url, bool valid) {
            Assert.AreEqual(valid, PathValidator.IsValidUrl(url));
        }

        [Test]
        public void SemanticVersion_ComparesLikeNuGet() {
            Assert.Less(new SemanticVersion("1.0.0.0").CompareTo(new SemanticVersion("1.0.1")), 0);
            Assert.Less(new SemanticVersion("2.0.0-beta").CompareTo(new SemanticVersion("2.0.0")), 0);
            Assert.AreEqual(0, new SemanticVersion(new Version(1, 2, 3, 4)).CompareTo(new SemanticVersion("1.2.3.4")));
            Assert.AreEqual(new Version(1, 2, 3, 4), new SemanticVersion("1.2.3.4").Version);
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Octgn.Core.DataExtensionMethods;
using Octgn.Library.Exceptions;
using Octgn.Portable;

namespace Octgn.Test.Engine
{
    /// <summary>
    /// The games installed from their packages, as the WPF client's game manager installs them: a game (.o8g,
    /// or the .nupkg of the OCTGN game feed), a newer version of it, its card images (.o8c). And the deck that
    /// has a card the installed version does not have.
    /// </summary>
    [TestFixture, NonParallelizable, Category("Engine")]
    public class GamePackageTests
    {
        private string _work;
        private string _game;

        [OneTimeSetUp]
        public void SetUp() {
            _work = Path.Combine(Path.GetTempPath(), "octgn-package-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_work);
            GameSession.Initialize(Path.Combine(_work, "Data"));
            _game = TestPackages.SampleGame(Path.Combine(_work, "Game"));
            // NuGet escapes the names of the files in the package: a space is %20 there.
            File.WriteAllBytes(Path.Combine(_game, "Resources", "with a space.png"), TestPackages.Png);
        }

        [OneTimeTearDown]
        public void TearDown() {
            try { Directory.Delete(_work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private XElement Definition => XDocument.Load(Path.Combine(_game, "definition.xml")).Root;

        [Test, Order(1)]
        public void AGamePackageInstallsTheGame() {
            var package = TestPackages.Game(_game, Path.Combine(_work, "sample.o8g"));

            var game = GameSession.InstallGame(package);

            Assert.AreEqual(Guid.Parse((string)Definition.Attribute("id")), game.Id);
            Assert.AreEqual(Version.Parse((string)Definition.Attribute("version")), game.Version);
            Assert.AreEqual(Path.Combine(GameDatabase, game.Id.ToString()), game.InstallPath.TrimEnd(Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(_game, "*", SearchOption.AllDirectories))
                FileAssert.Exists(Path.Combine(game.InstallPath, Path.GetRelativePath(_game, file)));
            Assert.That(game.AllCards().Count(), Is.GreaterThan(0), "the cards of its sets");
            CollectionAssert.Contains(GameSession.InstalledGames().Select(g => g.Id).ToList(), game.Id);
        }

        [Test, Order(2)]
        public void ANewerVersionReplacesTheGame() {
            // The feed's .nupkg of a newer version: the game database reads it at once, not its old version.
            var definition = Path.Combine(_game, "definition.xml");
            var newer = File.ReadAllText(definition).Replace("version=\"1.0.0.0\"", "version=\"1.0.1.0\"");
            Assert.AreNotEqual(File.ReadAllText(definition), newer, "the template's version is 1.0.0.0");
            File.WriteAllText(definition, newer);
            var package = TestPackages.Game(_game, Path.Combine(_work, "sample.1.0.1.nupkg"));

            var game = GameSession.InstallGame(package);

            Assert.AreEqual(new Version(1, 0, 1, 0), game.Version);
            Assert.AreEqual(1, GameSession.InstalledGames().Count(g => g.Id == game.Id));
        }

        [Test, Order(3)]
        public void AnImagePackGivesTheCardsTheirPictures() {
            var game = GameSession.InstalledGames().Single();
            var card = game.AllCards().First();
            var package = TestPackages.Images(_game, Path.Combine(_work, "images.o8c"));

            Assert.AreEqual(game.Id, GameSession.InstallImagePack(package).Id);

            var picture = card.GetPicture();
            Assert.AreEqual(Path.Combine(card.GetSet().ImagePackUri, card.Id + ".png"), picture);
            CollectionAssert.AreEqual(TestPackages.Png, File.ReadAllBytes(picture));
        }

        [Test]
        public void TheImagesOfAGameNotInstalledAreRefused() {
            var package = Path.Combine(_work, "other-images.o8c");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
                zip.CreateEntry($"{Guid.NewGuid()}/Sets/{Guid.NewGuid()}/Cards/{Guid.NewGuid()}.png");

            var ex = Assert.Throws<InvalidOperationException>(() => GameSession.InstallImagePack(package));
            StringAssert.Contains("install the game first", ex.Message);
        }

        [Test]
        public void APackageThatIsNotAGameIsRefused() {
            var package = Path.Combine(_work, "library.nupkg");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) {
                using (var writer = new StreamWriter(zip.CreateEntry("Some.Library.nuspec").Open()))
                    writer.Write(@"<?xml version=""1.0""?><package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd""><metadata><id>Some.Library</id><version>1.0.0</version><authors>Someone</authors><description>Not a game</description></metadata></package>");
            }

            var ex = Assert.Throws<InvalidOperationException>(() => GameSession.InstallGame(package));
            StringAssert.Contains("not the id of a game", ex.Message);
        }

        [Test]
        public void ADeckWithACardTheGameDoesNotHaveIsSaidSo() {
            Assert.IsTrue(GameSession.IsMissingCard(new UserMessageException(
                Octgn.Library.Localization.L.D.Exception__CanNotLoadDeckCardNotInstalled_Format, "/decks/hero.o8d", Guid.NewGuid(), "Echo")));
            Assert.IsFalse(GameSession.IsMissingCard(new UserMessageException("The deck is empty.")));
            Assert.IsFalse(GameSession.IsMissingCard(new InvalidOperationException("Problem loading deck x. The card with id: y and name: z is not installed.")));
        }

        private static string GameDatabase => Path.GetFullPath(Octgn.Library.Config.Instance.Paths.DatabasePath).TrimEnd(Path.DirectorySeparatorChar);
    }
}

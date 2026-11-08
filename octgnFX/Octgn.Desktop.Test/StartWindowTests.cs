// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Octgn.Core.DataExtensionMethods;
using Octgn.Portable;
using Octgn.Test.Engine;

namespace Octgn.Desktop.Test
{
    /// <summary>The first window: the nickname, and the games and their card images installed from their packages.</summary>
    [TestFixture, NonParallelizable]
    public class StartWindowTests : UiFixture
    {
        private string _work;

        [OneTimeSetUp]
        public Task SetUp() => Ui(async () => {
            _work = Path.Combine(Path.GetTempPath(), "octgn-start-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_work);
            App.SetUpEngine(Path.Combine(_work, "Data"));
            await Task.CompletedTask;
        });

        [OneTimeTearDown]
        public void TearDown() {
            try { Directory.Delete(_work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        [Test, Order(1)]
        public async Task TheNicknameIsTheUsersNameUntilOneIsSaved() {
            await Ui(async () => {
                // OCTGN's setting reads "null" until a nickname is saved: the player was named "null".
                Assert.AreEqual("null", Core.Prefs.Nickname);
                Assert.AreEqual(Environment.UserName, new StartWindow().NicknameText);
                Core.Prefs.Nickname = "Peter";
                Assert.AreEqual("Peter", new StartWindow().NicknameText);
                await Task.CompletedTask;
            });
        }

        [Test, Order(2)]
        public async Task PackagesInstallAGameAndTheImagesOfItsCards() {
            await Ui(async () => {
                var folder = TestPackages.SampleGame(Path.Combine(_work, "Game"));
                var game = TestPackages.Game(folder, Path.Combine(_work, "sample.o8g"));
                var images = TestPackages.Images(folder, Path.Combine(_work, "sample-images.o8c"));
                var start = new StartWindow();
                start.Show();
                try {
                    Assert.AreEqual(0, start.Games.Count);
                    // The images first: the game is installed before them all the same.
                    Assert.IsTrue(await start.Install(new[] { images, game }), start.StatusText);

                    var installed = start.Games.Single();
                    Assert.AreEqual($"Installed: {installed.Name} {installed.Version}, card images of {installed.Name} (sample-images.o8c).", start.StatusText);
                    var card = installed.AllCards().First();
                    Assert.AreEqual(Path.Combine(card.GetSet().ImagePackUri, card.Id + ".png"), card.GetPicture());
                    await Save(start, "00-start-window");
                } finally {
                    start.Close();
                }
            });
        }

        [Test, Order(3)]
        public async Task AFailedPackageIsSaid() {
            await Ui(async () => {
                var broken = Path.Combine(_work, "broken.o8g");
                File.WriteAllText(broken, "not a package");
                var start = new StartWindow();
                Assert.IsFalse(await start.Install(new[] { broken }));
                StringAssert.StartsWith("broken.o8g could not be installed: ", start.StatusText);
            });
        }
    }
}

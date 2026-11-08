// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using NUnit.Framework;
using Octgn.DataNew.Entities;
using Octgn.Portable;
using Dispatcher = Avalonia.Threading.Dispatcher;
using Point = Avalonia.Point;
using Group = Octgn.Play.Group;
using Player = Octgn.Play.Player;

namespace Octgn.Desktop.Test
{
    using EngineProgram = Octgn.Program;

    /// <summary>
    /// A local game in the Avalonia table, without a screen: Avalonia's headless platform (one application for
    /// all the tests), the engine on its UI thread, the window drawn by Skia, the mouse simulated. Pictures of
    /// the window go to the folder of OCTGN_SCREENSHOTS (or the "screenshots" folder of the output).
    /// </summary>
    public abstract class UiFixture
    {
        protected static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
        private static HeadlessUnitTestSession _session;
        private string _work;

        protected GameSession Game { get; private set; }

        protected PlayWindow Window { get; private set; }

        private static HeadlessUnitTestSession Session =>
            _session ?? (_session = HeadlessUnitTestSession.StartNew(typeof(Program), AvaloniaTestIsolationLevel.PerAssembly));

        /// <summary>Runs on the UI thread (the game dispatcher), and waits.</summary>
        protected static Task Ui(Func<Task> action) => Session.Dispatch(async () => { await action(); return 0; }, CancellationToken.None);

        protected static Task<T> Ui<T>(Func<T> func) => Session.Dispatch(func, CancellationToken.None);

        /// <summary>Installs a game in a data folder of its own, hosts it, and opens its play window (on the UI thread).</summary>
        protected async Task OpenGame(Func<string, string> prepareGameFolder, string nickname, int broadcastPort) {
            _work = Path.Combine(Path.GetTempPath(), "octgn-desktop-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_work);

            App.SetUpEngine(Path.Combine(_work, "Data"));
            var game = GameSession.InstallGame(prepareGameFolder(_work));
            Game = await GameSession.HostLocalGame(game, nickname, broadcastPort: broadcastPort);
            Window = new PlayWindow(Game);
            EngineProgram.Ui = new DesktopPlayUi(Window);
            Window.Show();
            await Until("the server welcomes the player", () => EngineProgram.GameEngine.IsWelcomed);
        }

        protected async Task StartGame() {
            Game.StartGame();
            await Until("the table is loaded", () => EngineProgram.GameEngine.TableLoaded);
        }

        protected async Task CloseGame() {
            await Ui(async () => {
                Window?.Close();
                await Task.Delay(100);
            });
            Game?.StopServer();
            // The headless session lives as long as the process: disposing it would wait for its thread forever.
            try {
                if (_work != null) Directory.Delete(_work, true);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }

        protected static Group Pile(string name) => Player.LocalPlayer.Groups.First(g => g.Name == name);

        protected GroupView View(string name) => Window.PlayerGroupViews.First(v => v.Group.Name == name);

        protected static async Task Until(string what, Func<bool> condition) {
            var deadline = DateTime.UtcNow + Wait;
            while (!condition()) {
                if (DateTime.UtcNow > deadline) Assert.Fail("Timed out waiting until " + what);
                await Task.Delay(30);
            }
        }

        protected Task Save(string name) => Save(Window, name);

        /// <summary>The folder of the pictures: OCTGN_SCREENSHOTS, or the "screenshots" folder of the output.</summary>
        private static string Screenshots {
            get {
                var folder = Environment.GetEnvironmentVariable("OCTGN_SCREENSHOTS") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>
        /// A picture of a window as it is now: the UI thread commits the changes to the renderer between timer
        /// ticks, so it runs its jobs around them.
        /// </summary>
        protected async Task Save(TopLevel window, string name) {
            await Task.Delay(30);
            for (var i = 0; i < 3; i++) {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(Screenshots, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        /// <summary>The window point at the center of a rectangle of a view.</summary>
        protected Point Center(Control view, Rect rect) => view.TranslatePoint(rect.Center, Window).Value;

        protected async Task Click(Point point, int count = 1, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None) {
            for (var i = 0; i < count; i++) {
                Window.MouseDown(point, button, modifiers);
                Window.MouseUp(point, button, modifiers);
            }
            await Task.Delay(20);
        }

        protected async Task Drag(Point from, Point to) {
            Window.MouseMove(from);
            Window.MouseDown(from, MouseButton.Left);
            for (var i = 1; i <= 10; i++) {
                Window.MouseMove(new Point(from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10), RawInputModifiers.LeftMouseButton);
                await Task.Delay(5);
            }
            Window.MouseUp(to, MouseButton.Left);
            await Task.Delay(20);
        }
    }
}

// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using log4net;
using Octgn.Portable;

namespace Octgn.Desktop
{
    /// <summary>What the command line asks for (see README.md).</summary>
    public sealed class StartupOptions
    {
        public string Game { get; set; }
        public string Deck { get; set; }
        public string Nickname { get; set; }
        public string DataDirectory { get; set; }
        public bool Verbose { get; set; }

        public static StartupOptions Parse(string[] args) {
            string Value(string name) {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            return new StartupOptions {
                Game = Value("--game") is string g ? Path.GetFullPath(g) : null,
                Deck = Value("--deck") is string d ? Path.GetFullPath(d) : null,
                Nickname = Value("--nick"),
                DataDirectory = Value("--data") is string data ? Path.GetFullPath(data) : null,
                Verbose = args.Contains("--verbose"),
            };
        }
    }

    public sealed class App : Application
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(App));

        public static StartupOptions Options { get; set; } = new StartupOptions();

        public override void Initialize() {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
            Name = "OCTGN";
        }

        /// <summary>The engine on Avalonia's UI thread, once per process (also for the tests, without a desktop).</summary>
        public static void SetUpEngine(string dataDirectory) {
            GameSession.Initialize(dataDirectory);
            Program.Dispatcher = new GameDispatcher();
            Program.Dispatcher.UnhandledException += ex => Log.Error("Game dispatcher", ex);
        }

        public override async void OnFrameworkInitializationCompleted() {
            base.OnFrameworkInitializationCompleted();
            if (!(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)) return;
            SetUpEngine(Options.DataDirectory);
            var start = new StartWindow();
            desktop.MainWindow = start;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            if (Options.Nickname != null) Core.Prefs.Nickname = Options.Nickname;
            start.Show();
            if (Options.Game != null) {
                var game = start.AddGame(Options.Game);
                if (game != null) await start.Host(game, Options.Deck);
            }
        }
    }

    public static class DesktopMain
    {
        [STAThread]
        public static int Main(string[] args) {
            App.Options = StartupOptions.Parse(args);
            if (App.Options.Verbose) log4net.Config.BasicConfigurator.Configure();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}

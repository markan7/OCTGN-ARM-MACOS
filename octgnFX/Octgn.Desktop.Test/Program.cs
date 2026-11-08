// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using Avalonia;
using Avalonia.Headless;
using NUnitLite;

namespace Octgn.Desktop.Test
{
    public static class Program
    {
        public static int Main(string[] args) {
            return new AutoRun(typeof(Program).Assembly).Execute(args);
        }

        /// <summary>The application of the table on Avalonia's headless platform, drawing with Skia as on a screen.</summary>
        public static AppBuilder BuildAvaloniaApp() {
            return AppBuilder.Configure<App>()
                .UseSkia()
                .WithInterFont()
                // Frames are drawn on the UI thread when the tests ask for them (CaptureRenderedFrame).
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true });
        }
    }
}

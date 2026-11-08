// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Threading;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using AvaloniaPriority = Avalonia.Threading.DispatcherPriority;
using EnginePriority = Octgn.Portable.DispatcherPriority;

namespace Octgn.Desktop
{
    /// <summary>
    /// The game dispatcher of the engine (Program.Dispatcher) on Avalonia's UI thread: the game model lives
    /// there, as it lives on WPF's UI thread in the Windows client.
    /// </summary>
    public sealed class GameDispatcher : Portable.Dispatcher
    {
        public override bool CheckAccess() {
            return AvaloniaDispatcher.UIThread.CheckAccess();
        }

        protected override void Post(Action action, EnginePriority priority) {
            AvaloniaDispatcher.UIThread.Post(action, Map(priority));
        }

        private static AvaloniaPriority Map(EnginePriority priority) {
            switch (priority) {
                case EnginePriority.Send: return AvaloniaPriority.Send;
                case EnginePriority.Render: return AvaloniaPriority.Render;
                case EnginePriority.Loaded: return AvaloniaPriority.Loaded;
                case EnginePriority.Input: return AvaloniaPriority.Input;
                case EnginePriority.Background: return AvaloniaPriority.Background;
                case EnginePriority.ContextIdle: return AvaloniaPriority.ContextIdle;
                case EnginePriority.ApplicationIdle: return AvaloniaPriority.ApplicationIdle;
                case EnginePriority.SystemIdle: return AvaloniaPriority.SystemIdle;
                case EnginePriority.Inactive: return AvaloniaPriority.SystemIdle;
                default: return AvaloniaPriority.Normal;
            }
        }

        /// <summary>
        /// Runs the UI's event loop until <paramref name="done"/> is set, without returning to the caller's
        /// caller: what a modal dialog needs when a script waits for its answer (ShowDialog in WPF).
        /// </summary>
        public static void RunUntil(CancellationToken done) {
            var frame = new Avalonia.Threading.DispatcherFrame();
            using (done.Register(() => AvaloniaDispatcher.UIThread.Post(() => frame.Continue = false, AvaloniaPriority.Send))) {
                if (done.IsCancellationRequested) return;
                AvaloniaDispatcher.UIThread.PushFrame(frame);
            }
        }
    }
}

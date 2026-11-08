// /* This Source Code Form is subject to the terms of the Mozilla Public
//  * License, v. 2.0. If a copy of the MPL was not distributed with this
//  * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace Octgn.Portable
{
    /// <summary>The values of WPF's DispatcherPriority. A dispatcher may run everything in order instead.</summary>
    public enum DispatcherPriority
    {
        Inactive = 0,
        SystemIdle = 1,
        ApplicationIdle = 2,
        ContextIdle = 3,
        Background = 4,
        Input = 5,
        Loaded = 6,
        Render = 7,
        DataBind = 8,
        Normal = 9,
        Send = 10,
    }

    /// <summary>
    /// The thread the game model lives on, in place of WPF's System.Windows.Threading.Dispatcher
    /// (<c>Program.Dispatcher</c>): the engine posts there the messages it receives from the server,
    /// and the script engine runs there the game operations of the scripts. A user interface gives its
    /// own UI thread (Avalonia's); <see cref="DispatcherThread"/> is a thread of its own, for tests and
    /// programs without a user interface. Same method names as WPF's, so the shared code is unchanged.
    /// </summary>
    public abstract class Dispatcher
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>True on the dispatcher's thread.</summary>
        public abstract bool CheckAccess();

        /// <summary>Runs <paramref name="action"/> later, on the dispatcher's thread.</summary>
        protected abstract void Post(Action action, DispatcherPriority priority);

        /// <summary>
        /// An action given to BeginInvoke threw. WPF raises Dispatcher.UnhandledException; here the
        /// exception is logged, then given to this handler (on the dispatcher's thread).
        /// </summary>
        public event Action<Exception> UnhandledException;

        public void VerifyAccess() {
            if (!CheckAccess())
                throw new InvalidOperationException("The calling thread cannot access this object because a different thread owns it.");
        }

        public void BeginInvoke(Delegate method, params object[] args) {
            BeginInvoke(method, DispatcherPriority.Normal, args);
        }

        public void BeginInvoke(DispatcherPriority priority, Delegate method) {
            BeginInvoke(method, priority);
        }

        public void BeginInvoke(Delegate method, DispatcherPriority priority, params object[] args) {
            if (method == null) throw new ArgumentNullException(nameof(method));
            Post(() => {
                try {
                    Call(method, args);
                } catch (Exception ex) {
                    Log.Error("Unhandled exception in a dispatched operation", ex);
                    UnhandledException?.Invoke(ex);
                }
            }, priority);
        }

        public void Invoke(Action action) {
            Invoke<object>(() => {
                action();
                return null;
            });
        }

        public T Invoke<T>(Func<T> func) {
            if (CheckAccess()) return func();
            return InvokeAsync(func).GetAwaiter().GetResult();
        }

        public object Invoke(Delegate method, params object[] args) {
            return Invoke(() => Call(method, args));
        }

        public Task InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal) {
            return InvokeAsync<object>(() => {
                action();
                return null;
            }, priority);
        }

        public Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal) {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() => {
                try {
                    completion.SetResult(func());
                } catch (Exception ex) {
                    completion.SetException(ex);
                }
            }, priority);
            return completion.Task;
        }

        /// <summary>As WPF's Dispatcher.Yield: lets the game dispatcher run what is waiting, then continues there.</summary>
        public static Task Yield(DispatcherPriority priority) {
            var dispatcher = Program.Dispatcher ?? throw new InvalidOperationException("Program.Dispatcher is not set.");
            return dispatcher.InvokeAsync(() => { }, priority);
        }

        private static object Call(Delegate method, object[] args) {
            if (method is Action action && (args == null || args.Length == 0)) {
                action();
                return null;
            }
            try {
                return method.DynamicInvoke(args);
            } catch (TargetInvocationException ex) when (ex.InnerException != null) {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }

    /// <summary>
    /// A dispatcher with a thread of its own, for programs without a user interface and for tests.
    /// Operations run in the order they are posted (priorities are ignored); <c>await</c> on this thread
    /// continues on it.
    /// </summary>
    public sealed class DispatcherThread : Dispatcher, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _thread;

        public DispatcherThread(string name = "OCTGN game dispatcher") {
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
        }

        public override bool CheckAccess() {
            return Thread.CurrentThread == _thread;
        }

        protected override void Post(Action action, DispatcherPriority priority) {
            try {
                _queue.Add(action);
            } catch (InvalidOperationException) {
                // Disposed: the operation is dropped, as WPF does after its dispatcher shut down.
            }
        }

        private void Run() {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(this));
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        }

        public void Dispose() {
            _queue.CompleteAdding();
            if (!CheckAccess()) _thread.Join(TimeSpan.FromSeconds(5));
        }

        private sealed class DispatcherSynchronizationContext : SynchronizationContext
        {
            private readonly DispatcherThread _owner;

            public DispatcherSynchronizationContext(DispatcherThread owner) {
                _owner = owner;
            }

            public override void Post(SendOrPostCallback d, object state) {
                _owner.Post(() => d(state), DispatcherPriority.Normal);
            }

            public override void Send(SendOrPostCallback d, object state) {
                _owner.Invoke(() => d(state));
            }

            public override SynchronizationContext CreateCopy() {
                return this;
            }
        }
    }

    /// <summary>
    /// WPF's DispatcherTimer: raises <see cref="Tick"/> every <see cref="Interval"/> on the game dispatcher
    /// (the one given, or Program.Dispatcher when the timer starts).
    /// </summary>
    public sealed class DispatcherTimer
    {
        private readonly object _lock = new object();
        private Dispatcher _dispatcher;
        private Timer _timer;
        private TimeSpan _interval;

        public DispatcherTimer() {
        }

        public DispatcherTimer(Dispatcher dispatcher) {
            _dispatcher = dispatcher;
        }

        public event EventHandler Tick;

        public bool IsEnabled { get; private set; }

        public TimeSpan Interval {
            get { return _interval; }
            set {
                lock (_lock) {
                    _interval = value;
                    _timer?.Change(value, value);
                }
            }
        }

        public void Start() {
            lock (_lock) {
                _dispatcher = _dispatcher ?? Program.Dispatcher ?? throw new InvalidOperationException("Program.Dispatcher is not set.");
                IsEnabled = true;
                _timer?.Dispose();
                _timer = new Timer(_ => _dispatcher.BeginInvoke(new Action(OnTick)), null, _interval, _interval);
            }
        }

        public void Stop() {
            lock (_lock) {
                IsEnabled = false;
                _timer?.Dispose();
                _timer = null;
            }
        }

        private void OnTick() {
            if (IsEnabled) Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

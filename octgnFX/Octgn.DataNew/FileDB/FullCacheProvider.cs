namespace Octgn.DataNew.FileDB
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    using log4net;

    public class FullCacheProvider : ICacheProvider
    {
        internal static ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        internal Dictionary<string, CacheObject> Cache { get; set; }
        internal readonly object CacheLocker = new object();
        public FullCacheProvider()
        {
            Cache = new Dictionary<string, CacheObject>();
        }

        public bool ObjectExistsFromPath(string path)
        {
            lock(CacheLocker)
                return Cache.ContainsKey(path);
        }

        public T GetObjectFromPath<T>(string path) where T : class
        {
            lock (CacheLocker)
            {
                if (!Cache.ContainsKey(path)) return default(T);
                var ret = Cache[path].Object as T;
                return ret;
            }
        }

        public void AddObjectToCache(string path, object obj)
        {
            lock (CacheLocker)
            {
                if (Cache.ContainsKey(path))
                {
                    Cache[path].Dispose();
                    Cache.Remove(path);
                }
#if NETCOREAPP
                Cache.Add(path, new CacheObject(this, path, obj, Watched(path)));
#else
                Cache.Add(path,new CacheObject(this,path,obj));
#endif
            }
        }
        public void InvalidatePath(string path)
        {
            lock (CacheLocker)
            {
                if (Cache.ContainsKey(path))
                {
                    Log.DebugFormat("Invalidating Path {0}", path);
                    Cache[path].Dispose();
                    Cache.Remove(path);
                    Log.DebugFormat("Path Invalidated {0}",path);
                }
            }
        }
        public void InvalidateObject(object obj)
        {
            lock (CacheLocker)
            {
                foreach (var o in Cache.Where(x => x.Value.Object == obj).ToArray())
                {
                    Cache[o.Key].Dispose();
                    Cache.Remove(o.Key);
                }
            }
        }
#if NETCOREAPP

        // One watcher for the game database, not one per file read. On Linux each FileSystemWatcher takes an
        // inotify instance, and a user has 128 of them: a game of a hundred sets (Marvel Champions) took them
        // all, and the files read after that failed (their sets were missing from the game).
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        /// <summary>
        /// Whether a watcher of the provider watches the file: true under the game database (the watcher is
        /// made the first time), false elsewhere (the cached object watches its file, as before).
        /// </summary>
        private bool Watched(string path)
        {
            string root;
            try
            {
                root = Library.Config.Instance?.Paths?.DatabasePath;
            }
            catch (Exception)
            {
                return false;
            }
            if (string.IsNullOrEmpty(root) || !IsUnder(path, root) || !Directory.Exists(root)) return false;
            if (_watchers.Any(x => IsUnder(path, x.Path))) return true;
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName
                };
                watcher.Changed += (sender, args) => InvalidateUnder(args.FullPath);
                watcher.Deleted += (sender, args) => InvalidateUnder(args.FullPath);
                watcher.Renamed += (sender, args) =>
                {
                    InvalidateUnder(args.OldFullPath);
                    InvalidateUnder(args.FullPath);
                };
                // Events lost (too many at once): forget all that is under the watcher.
                watcher.Error += (sender, args) => InvalidateUnder(root);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("Could not watch " + root, e);
                return false;
            }
        }

        /// <summary>Forgets the cached files at a path, or under it when it is a folder.</summary>
        private void InvalidateUnder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (CacheLocker)
            {
                foreach (var key in Cache.Keys.Where(x => IsUnder(x, path)).ToArray())
                    InvalidatePath(key);
            }
        }

        /// <summary>The path is the folder or the file, or under it (letter case aside).</summary>
        private static bool IsUnder(string path, string folder)
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            return p.Equals(f, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
#endif
    }
    internal class CacheObject : IDisposable
    {
        internal string Path { get; set; }
        internal object Object { get; set; }
        internal FileSystemWatcher Watcher { get; set; }
        internal ICacheProvider Provider { get; set; }
        internal bool Disposed;
#if NETCOREAPP
        /// <param name="watched">A watcher of the provider watches the file: the object makes none.</param>
        public CacheObject(ICacheProvider provider, string path, object obj, bool watched)
        {
            Provider = provider;
            Path = path;
            Object = obj;
            if (!watched) Watch(path);
        }

        private void Watch(string path)
        {
            var fi = new FileInfo(path);
#else
        public CacheObject(ICacheProvider provider, string path, object obj)
        {
            var fi = new FileInfo(path);
            Provider = provider;
            Path = path;
            Object = obj;
#endif
            Watcher = new FileSystemWatcher(fi.Directory.FullName,fi.Name);
            Watcher.NotifyFilter =  NotifyFilters.LastWrite | NotifyFilters.Size
                                   | NotifyFilters.FileName | NotifyFilters.DirectoryName ;
            Watcher.Changed += WatcherOnChanged;
            Watcher.Deleted += WatcherOnDeleted;
            Watcher.Renamed += WatcherOnRenamed;
            Watcher.EnableRaisingEvents = true;
        }

        private void WatcherOnRenamed(object sender, RenamedEventArgs renamedEventArgs)
        {
            Provider.InvalidatePath(Path);
        }

        private void WatcherOnDeleted(object sender, FileSystemEventArgs fileSystemEventArgs)
        {
            Provider.InvalidatePath(Path);
        }

        private void WatcherOnChanged(object sender, FileSystemEventArgs fileSystemEventArgs)
        {
            Provider.InvalidatePath(Path);
        }

        public void Dispose()
        {
            if (Disposed) return;
#if NETCOREAPP
            Disposed = true;
            Object = null;
            Path = null;
            if (Watcher == null) return;
#endif
            Watcher.EnableRaisingEvents = false;
            Watcher.Changed -= this.WatcherOnChanged;
            Watcher.Deleted -= this.WatcherOnDeleted;
            Watcher.Renamed -= this.WatcherOnRenamed;
            Disposed = true;
            Watcher.Dispose();
            Object = null;
            Path = null;
        }
    }
}
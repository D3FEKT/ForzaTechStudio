using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Dispatching;

namespace ForzaTechStudio.Helpers
{
    // Monitors a set of files for external changes (outside the app).
    internal sealed class FileChangeWatcher : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastFired = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _suppressUntil = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherQueue _dispatcher;
        private readonly object _lock = new();

        private const int DebounceMs = 800;
        private const int SuppressMs = 3000;

        // Fired on the UI thread when a watched file changes externally.
        public event Action<string>? FileChanged;

        public FileChangeWatcher(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Watch(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            lock (_lock)
            {
                if (_watchers.ContainsKey(path)) return;

                var dir = Path.GetDirectoryName(path);
                var file = Path.GetFileName(path);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

                try
                {
                    var watcher = new FileSystemWatcher(dir, file)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    watcher.Changed += OnChanged;
                    _watchers[path] = watcher;
                }
                catch { }
            }
        }

        public void Unwatch(string path)
        {
            lock (_lock)
            {
                if (_watchers.TryGetValue(path, out var w))
                {
                    w.EnableRaisingEvents = false;
                    w.Changed -= OnChanged;
                    w.Dispose();
                    _watchers.Remove(path);
                }
                _lastFired.Remove(path);
                _suppressUntil.Remove(path);
            }
        }

        public void UnwatchAll()
        {
            lock (_lock)
            {
                foreach (var w in _watchers.Values)
                {
                    w.EnableRaisingEvents = false;
                    w.Changed -= OnChanged;
                    w.Dispose();
                }
                _watchers.Clear();
                _lastFired.Clear();
                _suppressUntil.Clear();
            }
        }

        // Call before saving to suppress the resulting change event.
        public void Suppress(string path)
        {
            lock (_lock)
            {
                _suppressUntil[path] = DateTime.UtcNow.AddMilliseconds(SuppressMs);
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            var path = e.FullPath;
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                // Debounce rapid repeated events
                if (_lastFired.TryGetValue(path, out var last) && (now - last).TotalMilliseconds < DebounceMs)
                    return;
                _lastFired[path] = now;

                // Skip if we triggered this change ourselves (e.g., a save)
                if (_suppressUntil.TryGetValue(path, out var until) && now < until)
                    return;
            }

            _dispatcher.TryEnqueue(() => FileChanged?.Invoke(path));
        }

        public void Dispose() => UnwatchAll();
    }
}

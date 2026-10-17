using CairoDesktop.AppGrabber;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CairoDesktop.MenuBar.Launcher
{
    /// <summary>
    /// One launchable program.
    /// </summary>
    public class LauncherEntry
    {
        public ApplicationInfo App { get; }

        /// <summary>Primary match key: the display name.</summary>
        public string Name => App.Name;

        /// <summary>Secondary match key: the target's file name without extension.</summary>
        public string ExeName { get; }

        /// <summary>Identifies the program in the launch history.</summary>
        public string Key { get; }

        public LauncherEntry(ApplicationInfo app)
        {
            App = app;
            string target = string.IsNullOrEmpty(app.Target) ? app.Path : app.Target;
            ExeName = app.IsStoreApp || string.IsNullOrEmpty(target) ? null : SafeFileNameWithoutExtension(target);
            Key = $"{app.Name}|{target}".ToLowerInvariant();
        }

        private static string SafeFileNameWithoutExtension(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Installed programs only: the Start Menu folders and Store apps, from <see cref="IAppGrabber.GetApps"/>.
    /// Built in the background and refreshed when the Start Menu changes or the index gets old.
    /// </summary>
    public sealed class ProgramIndex : IDisposable
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
        private const int WatcherDebounceMs = 2000;

        private readonly IAppGrabber _appGrabber;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Timer _debounceTimer;
        private readonly object _lock = new object();

        private IReadOnlyList<LauncherEntry> _entries = new LauncherEntry[0];
        private DateTime _builtAt = DateTime.MinValue;
        private Task _buildTask;

        public ProgramIndex(IAppGrabber appGrabber)
        {
            _appGrabber = appGrabber;
            _debounceTimer = new Timer(_ => RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>An immutable snapshot of the index.</summary>
        public IReadOnlyList<LauncherEntry> Entries => _entries;

        public event EventHandler Updated;

        public void Start()
        {
            WatchStartMenu();
            RefreshAsync();
        }

        public void RefreshIfStale()
        {
            if (DateTime.UtcNow - _builtAt > MaxAge)
            {
                RefreshAsync();
            }
        }

        public Task RefreshAsync()
        {
            lock (_lock)
            {
                if (_buildTask != null && !_buildTask.IsCompleted)
                {
                    return _buildTask;
                }

                _buildTask = Task.Run(Build);
                return _buildTask;
            }
        }

        private void Build()
        {
            try
            {
                List<ApplicationInfo> apps = _appGrabber.GetApps();
                HashSet<string> seen = new HashSet<string>();
                List<LauncherEntry> entries = new List<LauncherEntry>(apps.Count);

                foreach (ApplicationInfo app in apps)
                {
                    if (app == null || string.IsNullOrWhiteSpace(app.Name))
                    {
                        continue;
                    }

                    LauncherEntry entry = new LauncherEntry(app);
                    if (seen.Add(entry.Key))
                    {
                        entries.Add(entry);
                    }
                }

                _entries = entries;
                _builtAt = DateTime.UtcNow;
                ShellLogger.Debug($"ProgramIndex: Indexed {entries.Count} programs");
                Updated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"ProgramIndex: Unable to index programs: {ex.Message}");
            }
        }

        private void WatchStartMenu()
        {
            foreach (string path in new[] { ShellHelper.UsersStartMenuPath, ShellHelper.AllUsersStartMenuPath })
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    {
                        continue;
                    }

                    FileSystemWatcher watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                    };
                    watcher.Changed += Watcher_Changed;
                    watcher.Created += Watcher_Changed;
                    watcher.Deleted += Watcher_Changed;
                    watcher.Renamed += Watcher_Changed;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"ProgramIndex: Unable to watch {path}: {ex.Message}");
                }
            }
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            _debounceTimer.Change(WatcherDebounceMs, Timeout.Infinite);
        }

        public void Dispose()
        {
            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            _debounceTimer.Dispose();
        }
    }
}

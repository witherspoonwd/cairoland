using CairoDesktop.Interop;
using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Persists what the tiling manager changed outside Cairo (hidden windows and the original Win+L lock policy)
    /// so it can be undone after a crash. Thread-safe: crash handlers may call it from any thread.
    /// </summary>
    public sealed class HiddenWindowJournal : IDisposable
    {
        public class JournalState
        {
            public List<long> Hidden { get; set; } = new List<long>();

            /// <summary>True when <see cref="LockPolicyExisted"/> and <see cref="LockPolicyValue"/> hold the original policy.</summary>
            public bool LockPolicySaved { get; set; }

            public bool LockPolicyExisted { get; set; }

            public int LockPolicyValue { get; set; }
        }

        private const int SaveDelayMs = 250;

        private static readonly string DefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cairo Desktop", "tiling-hidden.json");

        private readonly object _lock = new object();
        private readonly string _path;
        private readonly Timer _saveTimer;
        private JournalState _state;

        /// <summary>
        /// Called by <see cref="RestoreAll"/> with the saved lock policy, so it can be written back.
        /// </summary>
        public Action<JournalState> RestoreLockPolicy { get; set; }

        public HiddenWindowJournal() : this(DefaultPath)
        {
        }

        public HiddenWindowJournal(string path)
        {
            _path = path;
            _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
            _state = Load();
        }

        public JournalState State
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        public void AddHidden(IntPtr hwnd)
        {
            lock (_lock)
            {
                long id = hwnd.ToInt64();
                if (!_state.Hidden.Contains(id))
                {
                    _state.Hidden.Add(id);
                    ScheduleSave();
                }
            }
        }

        public void RemoveHidden(IntPtr hwnd)
        {
            lock (_lock)
            {
                if (_state.Hidden.Remove(hwnd.ToInt64()))
                {
                    ScheduleSave();
                }
            }
        }

        public void SaveLockPolicy(bool existed, int value)
        {
            lock (_lock)
            {
                _state.LockPolicySaved = true;
                _state.LockPolicyExisted = existed;
                _state.LockPolicyValue = value;
                Save();
            }
        }

        public void ClearLockPolicy()
        {
            lock (_lock)
            {
                _state.LockPolicySaved = false;
                _state.LockPolicyExisted = false;
                _state.LockPolicyValue = 0;
                Save();
            }
        }

        /// <summary>
        /// Shows every journaled window that still exists, restores the lock policy, and clears the journal.
        /// </summary>
        public void RestoreAll()
        {
            lock (_lock)
            {
                foreach (long id in _state.Hidden.ToList())
                {
                    IntPtr hwnd = new IntPtr(id);
                    if (User32.IsWindow(hwnd) && !User32.IsWindowVisible(hwnd))
                    {
                        ShellLogger.Debug($"HiddenWindowJournal: Restoring window 0x{id:X}");
                        User32.ShowWindow(hwnd, User32.SW_SHOWNA);
                    }
                }

                _state.Hidden.Clear();

                if (_state.LockPolicySaved)
                {
                    try
                    {
                        RestoreLockPolicy?.Invoke(_state);
                    }
                    catch (Exception ex)
                    {
                        ShellLogger.Error($"HiddenWindowJournal: Unable to restore lock policy: {ex.Message}");
                    }

                    _state.LockPolicySaved = false;
                }

                Save();
            }
        }

        private void ScheduleSave()
        {
            _saveTimer.Change(SaveDelayMs, Timeout.Infinite);
        }

        public void Flush()
        {
            Save();
        }

        private void Save()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    string tempPath = _path + ".tmp";
                    File.WriteAllText(tempPath, JsonSerializer.Serialize(_state));
                    if (File.Exists(_path))
                    {
                        File.Delete(_path);
                    }
                    File.Move(tempPath, _path);
                }
                catch (Exception ex)
                {
                    ShellLogger.Error($"HiddenWindowJournal: Unable to save journal: {ex.Message}");
                }
            }
        }

        private JournalState Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    return JsonSerializer.Deserialize<JournalState>(File.ReadAllText(_path)) ?? new JournalState();
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"HiddenWindowJournal: Unable to load journal: {ex.Message}");
            }

            return new JournalState();
        }

        public void Dispose()
        {
            _saveTimer.Dispose();
            Save();
        }
    }
}

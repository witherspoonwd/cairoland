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
        /// <summary>A window moved off screen instead of hidden (<see cref="HideMode.Offscreen"/>), and where it was.</summary>
        public class ParkedWindow
        {
            public long Hwnd { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
        }

        public class JournalState
        {
            public List<long> Hidden { get; set; } = new List<long>();

            public List<ParkedWindow> Parked { get; set; } = new List<ParkedWindow>();

            /// <summary>True when <see cref="LockPolicyExisted"/> and <see cref="LockPolicyValue"/> hold the original policy.</summary>
            public bool LockPolicySaved { get; set; }

            public bool LockPolicyExisted { get; set; }

            public int LockPolicyValue { get; set; }

            /// <summary>True when <see cref="MinAnimate"/> holds the original window animation setting.</summary>
            public bool AnimationSaved { get; set; }

            public int MinAnimate { get; set; }
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

        public void AddParked(IntPtr hwnd, User32.RECT rect)
        {
            lock (_lock)
            {
                long id = hwnd.ToInt64();
                _state.Parked.RemoveAll(parked => parked.Hwnd == id);
                _state.Parked.Add(new ParkedWindow { Hwnd = id, X = rect.Left, Y = rect.Top, W = rect.Width, H = rect.Height });
                // saved straight away: a crash now would otherwise leave the window off screen
                Save();
            }
        }

        /// <summary>Forgets a parked window and returns where it was, or null if it isn't parked.</summary>
        public ParkedWindow RemoveParked(IntPtr hwnd)
        {
            lock (_lock)
            {
                long id = hwnd.ToInt64();
                ParkedWindow parked = _state.Parked.Find(item => item.Hwnd == id);
                if (parked != null)
                {
                    _state.Parked.Remove(parked);
                    ScheduleSave();
                }

                return parked;
            }
        }

        public bool IsParked(IntPtr hwnd)
        {
            lock (_lock)
            {
                long id = hwnd.ToInt64();
                return _state.Parked.Exists(item => item.Hwnd == id);
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

        public void SaveAnimation(int minAnimate)
        {
            lock (_lock)
            {
                _state.AnimationSaved = true;
                _state.MinAnimate = minAnimate;
                Save();
            }
        }

        /// <summary>
        /// Writes back the saved window animation setting, if any, and forgets it.
        /// </summary>
        public void RestoreAnimation()
        {
            lock (_lock)
            {
                if (!_state.AnimationSaved)
                {
                    return;
                }

                try
                {
                    WindowAnimations.Set(_state.MinAnimate);
                }
                catch (Exception ex)
                {
                    ShellLogger.Error($"HiddenWindowJournal: Unable to restore window animations: {ex.Message}");
                }

                _state.AnimationSaved = false;
                Save();
            }
        }

        /// <summary>
        /// Shows every journaled window that still exists, moves parked windows back, restores the lock policy and
        /// window animations, and clears the journal.
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

                foreach (ParkedWindow parked in _state.Parked)
                {
                    IntPtr hwnd = new IntPtr(parked.Hwnd);
                    if (User32.IsWindow(hwnd))
                    {
                        ShellLogger.Debug($"HiddenWindowJournal: Moving parked window 0x{parked.Hwnd:X} back");
                        User32.SetWindowPos(hwnd, IntPtr.Zero, parked.X, parked.Y, parked.W, parked.H, User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER);
                    }
                }

                _state.Parked.Clear();

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

                RestoreAnimation();

                Save();
            }
        }

        private void ScheduleSave()
        {
            try
            {
                _saveTimer.Change(SaveDelayMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // shutting down
                Save();
            }
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

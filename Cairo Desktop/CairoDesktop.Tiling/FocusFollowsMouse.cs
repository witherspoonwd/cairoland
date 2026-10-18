using CairoDesktop.Common;
using CairoDesktop.Interop;
using ManagedShell.Common.Logging;
using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Focus follows mouse. "Native" mode turns on Windows' active window tracking (X-mouse) for this session
    /// only; "Polling" mode activates the managed window under the cursor from a timer.
    /// </summary>
    public sealed class FocusFollowsMouse : IDisposable
    {
        private const int PollIntervalMs = 50;

        private readonly Settings _settings;
        private readonly Func<IntPtr, bool> _isManaged;
        private readonly uint _ownPid;

        private bool _savedValid;
        private bool _savedTracking;
        private bool _savedZOrder;
        private uint _savedTimeout;

        private DispatcherTimer _pollTimer;
        private IntPtr _pollCandidate;
        private DateTime _pollCandidateSince;
        private int _suspendCount;

        public bool IsEnabled { get; private set; }

        private bool IsNative => IsEnabled && _pollTimer == null;

        public FocusFollowsMouse(Settings settings, Func<IntPtr, bool> isManaged)
        {
            _settings = settings;
            _isManaged = isManaged;
            using (Process process = Process.GetCurrentProcess())
            {
                _ownPid = (uint)process.Id;
            }
        }

        public void Enable()
        {
            if (IsEnabled)
            {
                Disable();
            }

            if (!_settings.EnableFocusFollowsMouse)
            {
                return;
            }

            IsEnabled = true;

            if (string.Equals(_settings.FocusFollowsMouseMode, "Polling", StringComparison.OrdinalIgnoreCase))
            {
                _pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
                _pollTimer.Tick += PollTimer_Tick;
                _pollTimer.Start();
                return;
            }

            SaveSystemValues();

            // fWinIni = 0 so nothing is written to the user profile
            Set(User32.SPI_SETACTIVEWINDOWTRACKING, _suspendCount == 0);
            Set(User32.SPI_SETACTIVEWNDTRKZORDER, _settings.FocusFollowsMouseRaise);
            User32.SystemParametersInfo(User32.SPI_SETACTIVEWNDTRKTIMEOUT, 0, new IntPtr(_settings.FocusFollowsMouseDelayMs), 0);
        }

        public void Disable()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Tick -= PollTimer_Tick;
                _pollTimer = null;
            }

            RestoreSystemValues();
            IsEnabled = false;
        }

        /// <summary>
        /// Stops focus from following the mouse until a matching <see cref="Resume"/>, so a transient Cairo window
        /// such as the program launcher is not deactivated when the cursor lands on another window. Calls nest.
        /// </summary>
        public void Suspend()
        {
            if (_suspendCount++ == 0 && IsNative)
            {
                Set(User32.SPI_SETACTIVEWINDOWTRACKING, false);
            }
        }

        public void Resume()
        {
            if (_suspendCount == 0)
            {
                return;
            }

            if (--_suspendCount == 0 && IsNative)
            {
                Set(User32.SPI_SETACTIVEWINDOWTRACKING, true);
            }
        }

        private void SaveSystemValues()
        {
            if (_savedValid)
            {
                return;
            }

            bool tracking = false;
            bool zOrder = false;
            uint timeout = 0;
            _savedValid = User32.SystemParametersInfo(User32.SPI_GETACTIVEWINDOWTRACKING, 0, ref tracking, 0)
                & User32.SystemParametersInfo(User32.SPI_GETACTIVEWNDTRKZORDER, 0, ref zOrder, 0)
                & User32.SystemParametersInfo(User32.SPI_GETACTIVEWNDTRKTIMEOUT, 0, ref timeout, 0);

            _savedTracking = tracking;
            _savedZOrder = zOrder;
            _savedTimeout = timeout;

            if (!_savedValid)
            {
                ShellLogger.Warning("FocusFollowsMouse: Unable to read active window tracking settings");
            }
        }

        private void RestoreSystemValues()
        {
            if (!_savedValid)
            {
                return;
            }

            Set(User32.SPI_SETACTIVEWINDOWTRACKING, _savedTracking);
            Set(User32.SPI_SETACTIVEWNDTRKZORDER, _savedZOrder);
            User32.SystemParametersInfo(User32.SPI_SETACTIVEWNDTRKTIMEOUT, 0, new IntPtr(_savedTimeout), 0);
            _savedValid = false;
        }

        private static void Set(uint action, bool value)
        {
            User32.SystemParametersInfo(action, 0, value ? new IntPtr(1) : IntPtr.Zero, 0);
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (_suspendCount > 0)
            {
                return;
            }

            if (User32.GetAsyncKeyState(User32.VK_LBUTTON) < 0 || User32.GetAsyncKeyState(User32.VK_RBUTTON) < 0 || User32.GetAsyncKeyState(User32.VK_MBUTTON) < 0)
            {
                return;
            }

            IntPtr foreground = User32.GetForegroundWindow();
            User32.GetWindowThreadProcessId(foreground, out uint foregroundPid);
            if (foregroundPid == _ownPid && !_isManaged(foreground))
            {
                // a Cairo menu or popup is open
                return;
            }

            if (!User32.GetCursorPos(out User32.POINT point))
            {
                return;
            }

            IntPtr hwnd = User32.GetAncestor(User32.WindowFromPoint(point), User32.GA_ROOT);
            if (hwnd == IntPtr.Zero || hwnd == foreground || !_isManaged(hwnd))
            {
                _pollCandidate = IntPtr.Zero;
                return;
            }

            if (hwnd != _pollCandidate)
            {
                _pollCandidate = hwnd;
                _pollCandidateSince = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - _pollCandidateSince).TotalMilliseconds >= _settings.FocusFollowsMouseDelayMs)
            {
                WindowArranger.Focus(hwnd, _settings.FocusFollowsMouseRaise);
                _pollCandidate = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Disable();
        }
    }
}

using CairoDesktop.Infrastructure.Services;
using CairoDesktop.Interop;
using ManagedShell.AppBar;
using ManagedShell.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;

namespace CairoDesktop.Services
{
    /// <summary>
    /// Keeps <see cref="FullScreenHelper.FullScreenApps"/> up to date so the menu bar drops behind fullscreen
    /// videos and games. ManagedShell normally fills that list from its TasksService, which Cairo doesn't start
    /// because the tiling engine tracks windows itself.
    /// </summary>
    public sealed class FullScreenWatcher : IDisposable
    {
        private static readonly string[] IgnoredClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        private readonly ILogger<FullScreenWatcher> _logger;
        private readonly FullScreenHelper _fullScreenHelper;
        private readonly uint _processId = (uint)Process.GetCurrentProcess().Id;

        // kept in a field so the delegate isn't collected while the hooks use it
        private readonly User32.WinEventDelegate _winEventProc;

        private IntPtr _foregroundHook;
        private IntPtr _showHideHook;
        private IntPtr _locationHook;

        public FullScreenWatcher(ILogger<FullScreenWatcher> logger, ShellManagerService shellManagerService)
        {
            _logger = logger;
            _fullScreenHelper = shellManagerService.ShellManager.FullScreenHelper;
            _winEventProc = WinEventProc;
        }

        public void Initialize()
        {
            if (_foregroundHook != IntPtr.Zero)
            {
                return;
            }

            // out-of-context hooks call back on this (the UI) thread, where the app bars watch the list
            _foregroundHook = User32.SetWinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
            _showHideHook = User32.SetWinEventHook(User32.EVENT_OBJECT_DESTROY, User32.EVENT_OBJECT_HIDE, IntPtr.Zero, _winEventProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
            _locationHook = User32.SetWinEventHook(User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventProc, 0, 0, User32.WINEVENT_OUTOFCONTEXT);

            if (_foregroundHook == IntPtr.Zero || _showHideHook == IntPtr.Zero || _locationHook == IntPtr.Zero)
            {
                _logger.LogWarning("Unable to hook window events; the menu bar will not hide behind fullscreen apps");
            }

            Update();
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF || hwnd == IntPtr.Zero)
            {
                return;
            }

            // location changes are constant; only the foreground window and known fullscreen windows matter
            if (eventType == User32.EVENT_OBJECT_LOCATIONCHANGE
                && hwnd != User32.GetForegroundWindow()
                && !_fullScreenHelper.FullScreenApps.Any(app => app.hWnd == hwnd))
            {
                return;
            }

            Update();
        }

        private void Update()
        {
            IntPtr foreground = User32.GetForegroundWindow();
            FullScreenApp foregroundApp = GetFullScreenApp(foreground);
            string foregroundScreen = foreground != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(foreground).DeviceName : null;

            // drop windows that left fullscreen, and ones the user switched away from on the same screen
            var apps = _fullScreenHelper.FullScreenApps;
            for (int i = apps.Count - 1; i >= 0; i--)
            {
                FullScreenApp app = apps[i];
                bool stale = app.hWnd == foreground
                    ? foregroundApp == null
                    : app.screen.DeviceName == foregroundScreen || GetFullScreenApp(app.hWnd) == null;

                if (stale)
                {
                    apps.RemoveAt(i);
                }
            }

            if (foregroundApp != null && !apps.Any(app => app.hWnd == foreground))
            {
                apps.Add(foregroundApp);
            }
        }

        /// <summary>
        /// Returns the window as a fullscreen app if it is visible and covers its whole monitor, or null.
        /// </summary>
        private FullScreenApp GetFullScreenApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == User32.GetShellWindow()
                || !User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd) || Dwmapi.IsCloaked(hwnd)
                || IgnoredClasses.Contains(User32.GetClassName(hwnd)))
            {
                return null;
            }

            User32.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == _processId)
            {
                return null;
            }

            User32.MONITORINFO monitorInfo = User32.MONITORINFO.Create();
            if (!User32.GetMonitorInfo(User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST), ref monitorInfo))
            {
                return null;
            }

            if (!Dwmapi.TryGetExtendedFrameBounds(hwnd, out User32.RECT rect) || rect.Width <= 0)
            {
                User32.GetWindowRect(hwnd, out rect);
            }

            User32.RECT monitor = monitorInfo.rcMonitor;
            if (rect.Left > monitor.Left || rect.Top > monitor.Top || rect.Right < monitor.Right || rect.Bottom < monitor.Bottom)
            {
                return null;
            }

            return new FullScreenApp
            {
                hWnd = hwnd,
                screen = ScreenInfo.Create(System.Windows.Forms.Screen.FromHandle(hwnd)),
                rect = new NativeMethods.Rect { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom },
                title = "",
                fromTasksService = false
            };
        }

        public void Dispose()
        {
            foreach (IntPtr hook in new[] { _foregroundHook, _showHideHook, _locationHook })
            {
                if (hook != IntPtr.Zero)
                {
                    User32.UnhookWinEvent(hook);
                }
            }

            _foregroundHook = _showHideHook = _locationHook = IntPtr.Zero;
        }
    }
}

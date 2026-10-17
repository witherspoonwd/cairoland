using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using CairoDesktop.Interop;
using System;
using System.Collections.Generic;

namespace CairoDesktop.MenuBar.Launcher
{
    /// <summary>
    /// Opens the program launcher on the focused screen, or closes it if it is open. Works with tiling on or off.
    /// </summary>
    public class ToggleProgramLauncherCommand : ICairoCommand
    {
        public ICairoCommandInfo Info => _info;

        private readonly ToggleProgramLauncherCommandInfo _info = new ToggleProgramLauncherCommandInfo();
        private readonly ProgramIndex _index;
        private readonly LaunchHistory _history;
        private readonly IAppGrabber _appGrabber;
        private readonly Settings _settings;
        private ProgramLauncherWindow _window;

        public ToggleProgramLauncherCommand(ProgramIndex index, LaunchHistory history, IAppGrabber appGrabber, Settings settings)
        {
            _index = index;
            _history = history;
            _appGrabber = appGrabber;
            _settings = settings;

            // build the index in the background so the first open is instant
            _index.Start();
        }

        public bool Execute(params (string name, object value)[] parameters)
        {
            if (_window != null)
            {
                _window.CloseLauncher();
                return true;
            }

            _window = new ProgramLauncherWindow(_index, _history, _appGrabber, _settings);
            _window.Closed += (sender, e) => _window = null;
            _window.ShowOn(GetFocusedWorkArea());
            return true;
        }

        /// <summary>The work area of the foreground window's monitor, or the cursor's.</summary>
        private static User32.RECT GetFocusedWorkArea()
        {
            IntPtr monitor = IntPtr.Zero;
            IntPtr foreground = User32.GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                monitor = User32.MonitorFromWindow(foreground, User32.MONITOR_DEFAULTTONULL);
            }

            if (monitor == IntPtr.Zero && User32.GetCursorPos(out User32.POINT point))
            {
                monitor = User32.MonitorFromPoint(point, User32.MONITOR_DEFAULTTONEAREST);
            }

            User32.MONITORINFO info = User32.MONITORINFO.Create();
            if (monitor != IntPtr.Zero && User32.GetMonitorInfo(monitor, ref info))
            {
                return info.rcWork;
            }

            System.Drawing.Rectangle area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            return new User32.RECT { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        }
    }

    public class ToggleProgramLauncherCommandInfo : ICairoCommandInfo
    {
        public string Identifier => "ToggleProgramLauncher";

        public string Description => "Toggles the program launcher.";

        public string Label => DisplayString.sCommand_ToggleProgramLauncher;

        public bool IsAvailable => true;

        public IReadOnlyCollection<CairoCommandParameter> Parameters => null;
    }
}

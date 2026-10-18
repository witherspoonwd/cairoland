using CairoDesktop.Common;
using CairoDesktop.Interop;
using System;
using System.Diagnostics;
using System.Linq;

namespace CairoDesktop.Tiling
{
    public enum FilterResult
    {
        Ignore,
        Float,
        Manage
    }

    /// <summary>
    /// Decides whether a top-level window is tiled, floated, or ignored.
    /// </summary>
    public class WindowFilter
    {
        private static readonly string[] IgnoredClasses =
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow",
            "ApplicationFrameInputSinkWindow"
        };

        private readonly Settings _settings;
        private readonly uint _ownPid;

        public WindowFilter(Settings settings)
        {
            _settings = settings;
            using (Process process = Process.GetCurrentProcess())
            {
                _ownPid = (uint)process.Id;
            }
        }

        public FilterResult ShouldManage(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !User32.IsWindowVisible(hwnd) || Dwmapi.IsCloaked(hwnd))
            {
                return FilterResult.Ignore;
            }

            long style = User32.GetWindowLongPtr(hwnd, User32.GWL_STYLE);
            long exStyle = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);

            if ((style & User32.WS_CHILD) != 0)
            {
                return FilterResult.Ignore;
            }

            if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0 && (exStyle & User32.WS_EX_APPWINDOW) == 0)
            {
                return FilterResult.Ignore;
            }

            if ((exStyle & User32.WS_EX_NOACTIVATE) != 0)
            {
                return FilterResult.Ignore;
            }

            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _ownPid && !TilingWindow.IsOwnWindowManaged(hwnd))
            {
                return FilterResult.Ignore;
            }

            string className = User32.GetClassName(hwnd);
            if (IgnoredClasses.Contains(className))
            {
                return FilterResult.Ignore;
            }

            // a UWP frame with neither an app attached nor a title is a leftover from a closed app
            bool isFrame = UwpWindowHelper.IsFrame(className);
            uint appPid = isFrame ? UwpWindowHelper.GetAppPid(hwnd) : pid;
            if (isFrame && appPid == 0 && User32.GetWindowTextLength(hwnd) == 0)
            {
                return FilterResult.Ignore;
            }

            bool hasCaption = (style & User32.WS_CAPTION) == User32.WS_CAPTION;
            if (!hasCaption && User32.GetWindowTextLength(hwnd) == 0)
            {
                return FilterResult.Ignore;
            }

            if (!User32.GetWindowRect(hwnd, out User32.RECT rect) || rect.Width <= 0 || rect.Height <= 0)
            {
                return FilterResult.Ignore;
            }

            // dialogs and other owned windows
            if (User32.GetWindow(hwnd, User32.GW_OWNER) != IntPtr.Zero || className == "#32770")
            {
                return FilterResult.Float;
            }

            // fixed-size windows
            if ((style & User32.WS_THICKFRAME) == 0)
            {
                return FilterResult.Float;
            }

            if ((style & User32.WS_POPUP) != 0 && !hasCaption)
            {
                return FilterResult.Float;
            }

            if (Matches(_settings.TilingFloatClasses, className))
            {
                return FilterResult.Float;
            }

            if (_settings.TilingFloatExes != null && _settings.TilingFloatExes.Length > 0)
            {
                // for UWP frames, the hosted app (Calculator.exe, or its AppUserModelID), not ApplicationFrameHost.exe
                string exe = appPid != 0 ? ManagedWindow.GetExeName(appPid) : ManagedWindow.GetExeName(pid);
                if (Matches(_settings.TilingFloatExes, exe) || Matches(_settings.TilingFloatExes, System.IO.Path.GetFileNameWithoutExtension(exe)))
                {
                    return FilterResult.Float;
                }

                if (isFrame && Matches(_settings.TilingFloatExes, UwpWindowHelper.GetAppUserModelId(hwnd, appPid)))
                {
                    return FilterResult.Float;
                }
            }

            return FilterResult.Manage;
        }

        private static bool Matches(string[] list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return list.Any(item => !string.IsNullOrWhiteSpace(item) && string.Equals(item.Trim(), value, StringComparison.OrdinalIgnoreCase));
        }
    }
}

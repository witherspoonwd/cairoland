using CairoDesktop.Interop;
using System;
using System.Windows;
using System.Windows.Interop;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Cairo's own windows are ignored by the tiling manager unless they opt in with
    /// <c>tiling:TilingWindow.IsManaged="True"</c>, which makes them live on a tag like any other app window.
    /// Windows that paint the desktop set <c>IsDesktop</c> so they never take focus from app windows.
    /// </summary>
    public static class TilingWindow
    {
        public static readonly DependencyProperty IsManagedProperty =
            DependencyProperty.RegisterAttached("IsManaged", typeof(bool), typeof(TilingWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty IsDesktopProperty =
            DependencyProperty.RegisterAttached("IsDesktop", typeof(bool), typeof(TilingWindow), new PropertyMetadata(false));

        public static bool GetIsManaged(DependencyObject element) => (bool)element.GetValue(IsManagedProperty);

        public static void SetIsManaged(DependencyObject element, bool value) => element.SetValue(IsManagedProperty, value);

        public static bool GetIsDesktop(DependencyObject element) => (bool)element.GetValue(IsDesktopProperty);

        public static void SetIsDesktop(DependencyObject element, bool value) => element.SetValue(IsDesktopProperty, value);

        /// <summary>Whether a window of this process opted in. Must be called on the UI thread.</summary>
        internal static bool IsOwnWindowManaged(IntPtr hwnd)
        {
            return HwndSource.FromHwnd(hwnd)?.RootVisual is Window window && GetIsManaged(window);
        }

        /// <summary>
        /// Whether a top-level window is the desktop: the shell window, Explorer's desktop windows, or one of ours that
        /// set <c>IsDesktop</c>. Must be called on the UI thread.
        /// </summary>
        internal static bool IsDesktopWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (hwnd == User32.GetShellWindow())
            {
                return true;
            }

            string className = User32.GetClassName(hwnd);
            if (className == "Progman" || className == "WorkerW")
            {
                return true;
            }

            return HwndSource.FromHwnd(hwnd)?.RootVisual is Window window && GetIsDesktop(window);
        }

        /// <summary>Whether the cursor is over the desktop rather than a window. Must be called on the UI thread.</summary>
        internal static bool IsDesktopUnderCursor()
        {
            return User32.GetCursorPos(out User32.POINT point)
                && IsDesktopWindow(User32.GetAncestor(User32.WindowFromPoint(point), User32.GA_ROOT));
        }
    }
}

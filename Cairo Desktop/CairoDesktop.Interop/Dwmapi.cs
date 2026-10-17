using System;
using System.Runtime.InteropServices;

namespace CairoDesktop.Interop
{
    public class Dwmapi
    {
        const string Dwmapi_DllName = "dwmapi.dll";

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        [DllImport(Dwmapi_DllName)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out User32.RECT pvAttribute, int cbAttribute);

        [DllImport(Dwmapi_DllName)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport(Dwmapi_DllName)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        /// <summary>
        /// Turns off DWM show/hide transitions for a window. Best effort: DWM may refuse for other processes' windows.
        /// </summary>
        public static bool DisableTransitions(IntPtr hwnd)
        {
            try
            {
                int disabled = 1;
                return DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        public static bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        public static bool TryGetExtendedFrameBounds(IntPtr hwnd, out User32.RECT rect)
        {
            try
            {
                return DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(User32.RECT))) == 0;
            }
            catch (DllNotFoundException)
            {
                rect = default(User32.RECT);
                return false;
            }
        }
    }
}

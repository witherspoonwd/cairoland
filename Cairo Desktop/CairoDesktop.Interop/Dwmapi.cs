using System;
using System.Runtime.InteropServices;

namespace CairoDesktop.Interop
{
    public class Dwmapi
    {
        const string Dwmapi_DllName = "dwmapi.dll";

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        [DllImport(Dwmapi_DllName)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out User32.RECT pvAttribute, int cbAttribute);

        [DllImport(Dwmapi_DllName)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

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

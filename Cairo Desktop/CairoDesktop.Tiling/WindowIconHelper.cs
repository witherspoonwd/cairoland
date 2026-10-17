using CairoDesktop.Interop;
using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CairoDesktop.Tiling
{
    public static class WindowIconHelper
    {
        private static readonly Dictionary<IntPtr, ImageSource> Cache = new Dictionary<IntPtr, ImageSource>();

        public static ImageSource GetIcon(IntPtr hwnd, string className)
        {
            if (Cache.TryGetValue(hwnd, out ImageSource cached))
            {
                return cached;
            }

            ImageSource icon = null;

            // UWP frame windows don't expose their icon this way; they get no icon in v1
            if (className != "ApplicationFrameWindow")
            {
                IntPtr hIcon = GetIconHandle(hwnd);
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        icon = source;
                    }
                    catch (Exception ex)
                    {
                        ShellLogger.Debug($"WindowIconHelper: Unable to convert icon for 0x{hwnd.ToInt64():X}: {ex.Message}");
                    }
                }
            }

            Cache[hwnd] = icon;
            return icon;
        }

        public static void Forget(IntPtr hwnd)
        {
            Cache.Remove(hwnd);
        }

        private static IntPtr GetIconHandle(IntPtr hwnd)
        {
            foreach (int type in new[] { User32.ICON_SMALL2, User32.ICON_SMALL, User32.ICON_BIG })
            {
                if (User32.SendMessageTimeout(hwnd, User32.WM_GETICON, new IntPtr(type), IntPtr.Zero, User32.SMTO_ABORTIFHUNG, 100, out IntPtr result) != IntPtr.Zero && result != IntPtr.Zero)
                {
                    return result;
                }
            }

            IntPtr classIcon = User32.GetClassLongPtr(hwnd, User32.GCLP_HICONSM);
            if (classIcon == IntPtr.Zero)
            {
                classIcon = User32.GetClassLongPtr(hwnd, User32.GCLP_HICON);
            }

            return classIcon;
        }
    }
}

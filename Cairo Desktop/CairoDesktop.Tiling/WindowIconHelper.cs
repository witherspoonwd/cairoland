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
        private const int AppIconSize = 32;

        private static readonly Dictionary<IntPtr, ImageSource> Cache = new Dictionary<IntPtr, ImageSource>();
        private static readonly Dictionary<string, ImageSource> AppCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource GetIcon(ManagedWindow window)
        {
            // UWP frames don't expose their icon through the window; the app's Start menu entry has it
            if (window.IsFrameWindow)
            {
                return string.IsNullOrEmpty(window.AppUserModelId) ? null : GetAppIcon(window.AppUserModelId);
            }

            return GetIcon(window.Hwnd, window.ClassName);
        }

        /// <summary>The icon of shell:AppsFolder\&lt;aumid&gt;, cached per app.</summary>
        public static ImageSource GetAppIcon(string appUserModelId)
        {
            if (AppCache.TryGetValue(appUserModelId, out ImageSource cached))
            {
                return cached;
            }

            ImageSource icon = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                bitmap = Shell32.GetAppIconBitmap(appUserModelId, AppIconSize);
                int width = 0;
                int height = 0;
                byte[] pixels = bitmap != IntPtr.Zero ? Shell32.GetBitmapPixels(bitmap, out width, out height) : null;
                if (pixels != null)
                {
                    BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
                    source.Freeze();
                    icon = source;
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Debug($"WindowIconHelper: Unable to load the icon for {appUserModelId}: {ex.Message}");
            }
            finally
            {
                if (bitmap != IntPtr.Zero)
                {
                    Shell32.DeleteObject(bitmap);
                }
            }

            AppCache[appUserModelId] = icon;
            return icon;
        }

        public static ImageSource GetIcon(IntPtr hwnd, string className)
        {
            if (Cache.TryGetValue(hwnd, out ImageSource cached))
            {
                return cached;
            }

            ImageSource icon = null;

            if (!UwpWindowHelper.IsFrame(className))
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

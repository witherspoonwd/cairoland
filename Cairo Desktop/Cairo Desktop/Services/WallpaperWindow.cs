using CairoDesktop.Common;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NativeMethods = ManagedShell.Interop.NativeMethods;

namespace CairoDesktop.Services
{
    /// <summary>
    /// Borderless, bottom-most window that paints the desktop background on one screen: Cairo's own image when
    /// <see cref="Settings.DesktopBackgroundPath"/> is set, otherwise the Windows wallpaper. It is a child of the
    /// shell window when Cairo owns one; under Shell Launcher, where CustomShellHost owns it, it is a top-level
    /// window kept at the bottom of the z-order.
    /// </summary>
    public class WallpaperWindow : Window
    {
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SWP_NOZORDER = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private readonly IntPtr _parentHandle;
        private readonly Settings _settings;

        public AppBarScreen Screen { get; private set; }

        public IntPtr Handle { get; private set; }

        public bool AllowClose { get; set; }

        public WallpaperWindow(AppBarScreen screen, IntPtr parentHandle, Settings settings)
        {
            Screen = screen;
            _parentHandle = parentHandle;
            _settings = settings;

            Title = "";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            UseLayoutRounding = true;
            Background = Brushes.Black;

            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            Handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);

            int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.WindowLongFlags.GWL_EXSTYLE);
            exStyle |= (int)NativeMethods.ExtendedWindowStyles.WS_EX_TOOLWINDOW | (int)NativeMethods.ExtendedWindowStyles.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(Handle, NativeMethods.WindowLongFlags.GWL_EXSTYLE, exStyle);

            if (_parentHandle != IntPtr.Zero)
            {
                // become a child of the shell window, same as the old desktop window did
                int style = NativeMethods.GetWindowLong(Handle, NativeMethods.WindowLongFlags.GWL_STYLE);
                style = (style | (int)NativeMethods.WindowStyles.WS_CHILD) & ~unchecked((int)NativeMethods.WindowStyles.WS_OVERLAPPED);
                NativeMethods.SetWindowLong(Handle, NativeMethods.WindowLongFlags.GWL_STYLE, style);
                NativeMethods.SetParent(Handle, _parentHandle);
            }

            WindowHelper.HideWindowFromTasks(Handle);
            ReloadBackground();
            ResetPosition();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case (int)NativeMethods.WM.MOUSEACTIVATE:
                    handled = true;
                    return (IntPtr)NativeMethods.MA_NOACTIVATE;
                case WM_WINDOWPOSCHANGING when _parentHandle == IntPtr.Zero:
                    // top-level: whoever moves us in the z-order, stay at the bottom
                    WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                    if ((pos.flags & SWP_NOZORDER) == 0 && pos.hwndInsertAfter != (IntPtr)NativeMethods.WindowZOrder.HWND_BOTTOM)
                    {
                        pos.hwndInsertAfter = (IntPtr)NativeMethods.WindowZOrder.HWND_BOTTOM;
                        Marshal.StructureToPtr(pos, lParam, false);
                    }
                    break;
                case WM_SETTINGCHANGE when _parentHandle == IntPtr.Zero && wParam.ToInt32() == SPI_SETDESKWALLPAPER:
                    // without our own shell window, nothing else tells us the Windows wallpaper changed
                    ReloadBackground();
                    break;
            }

            return IntPtr.Zero;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
            }

            base.OnClosing(e);
        }

        public void SetScreen(AppBarScreen screen)
        {
            Screen = screen;
            ResetPosition();
        }

        public void ResetPosition()
        {
            if (Handle == IntPtr.Zero)
            {
                return;
            }

            int left = Screen.Bounds.Left;
            int top = Screen.Bounds.Top;

            if (_parentHandle != IntPtr.Zero)
            {
                // child window coordinates are relative to the shell window, which spans the virtual screen
                left -= System.Windows.Forms.SystemInformation.VirtualScreen.Left;
                top -= System.Windows.Forms.SystemInformation.VirtualScreen.Top;
            }

            NativeMethods.SetWindowPos(Handle, (IntPtr)NativeMethods.WindowZOrder.HWND_BOTTOM, left, top, Screen.Bounds.Width, Screen.Bounds.Height,
                (int)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE);
        }

        public void ReloadBackground()
        {
            try
            {
                Background = GetCairoBackgroundBrush() ?? GetWallpaperBrush() ?? GetBackgroundColorBrush();
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"WallpaperWindow: Failed setting wallpaper: {ex.Message}");
                Background = GetBackgroundColorBrush();
            }
        }

        private Brush GetCairoBackgroundBrush()
        {
            string path = Environment.ExpandEnvironmentVariables(_settings.DesktopBackgroundPath ?? "");
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!File.Exists(path))
            {
                ShellLogger.Warning($"WallpaperWindow: Desktop background {path} doesn't exist; using the Windows wallpaper");
                return null;
            }

            return CreateImageBrush(path, _settings.DesktopBackgroundStyle);
        }

        private static Brush GetWallpaperBrush()
        {
            string wallpaper = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", "") as string;
            string regWallpaperStyle = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallpaperStyle", "") as string;
            string regTileWallpaper = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "TileWallpaper", "") as string;

            if (string.IsNullOrWhiteSpace(wallpaper) || !ShellHelper.Exists(wallpaper))
            {
                // slideshows and Spotlight leave the registry value empty but keep the current image here
                wallpaper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (!File.Exists(wallpaper))
                {
                    return null;
                }
            }

            // https://docs.microsoft.com/en-us/windows/desktop/Controls/themesfileformat-overview
            string style;
            switch ($"{regWallpaperStyle}{regTileWallpaper}")
            {
                case "01":
                    style = "Tile";
                    break;
                case "00":
                    style = "Center";
                    break;
                case "60":
                    style = "Fit";
                    break;
                case "100":
                case "220": // Span (per-screen fill for now)
                    style = "Fill";
                    break;
                case "20":
                default:
                    style = "Stretch";
                    break;
            }

            return CreateImageBrush(wallpaper, style);
        }

        private static Brush CreateImageBrush(string path, string style)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            bitmap.EndInit();
            bitmap.Freeze();

            ImageBrush brush = new ImageBrush(bitmap)
            {
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };

            switch (style)
            {
                case "Tile":
                    brush.AlignmentX = AlignmentX.Left;
                    brush.AlignmentY = AlignmentY.Top;
                    brush.TileMode = TileMode.Tile;
                    brush.Stretch = Stretch.Fill;
                    brush.Viewport = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    break;
                case "Center":
                    brush.Stretch = Stretch.None;
                    break;
                case "Fit":
                    brush.Stretch = Stretch.Uniform;
                    break;
                case "Stretch":
                    brush.Stretch = Stretch.Fill;
                    break;
                case "Fill":
                default:
                    brush.Stretch = Stretch.UniformToFill;
                    break;
            }

            brush.Freeze();
            return brush;
        }

        private static Brush GetBackgroundColorBrush()
        {
            byte[] rgb = { 0, 0, 0 };

            try
            {
                string regColor = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Colors", "Background", "0 0 0") as string;
                string[] parts = regColor?.Split(' ') ?? new string[0];

                if (parts.Length == 3)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (byte.TryParse(parts[i], out byte value))
                        {
                            rgb[i] = value;
                        }
                    }
                }
            }
            catch { }

            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2]));
            brush.Freeze();
            return brush;
        }
    }
}

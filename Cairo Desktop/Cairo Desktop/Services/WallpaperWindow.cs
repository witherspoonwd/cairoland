using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NativeMethods = ManagedShell.Interop.NativeMethods;

namespace CairoDesktop.Services
{
    /// <summary>
    /// Borderless, bottom-most window that paints the system wallpaper on one screen.
    /// It is parented to the shell window, so it is only used when Cairo is the shell.
    /// </summary>
    public class WallpaperWindow : Window
    {
        private readonly IntPtr _parentHandle;

        public AppBarScreen Screen { get; private set; }

        public IntPtr Handle { get; private set; }

        public bool AllowClose { get; set; }

        public WallpaperWindow(AppBarScreen screen, IntPtr parentHandle)
        {
            Screen = screen;
            _parentHandle = parentHandle;

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
            if (msg == (int)NativeMethods.WM.MOUSEACTIVATE)
            {
                handled = true;
                return (IntPtr)NativeMethods.MA_NOACTIVATE;
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
                Background = GetWallpaperBrush() ?? GetBackgroundColorBrush();
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"WallpaperWindow: Failed setting wallpaper: {ex.Message}");
                Background = GetBackgroundColorBrush();
            }
        }

        private static Brush GetWallpaperBrush()
        {
            string wallpaper = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", "") as string;
            string regWallpaperStyle = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallpaperStyle", "") as string;
            string regTileWallpaper = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "TileWallpaper", "") as string;

            if (string.IsNullOrWhiteSpace(wallpaper) || !ShellHelper.Exists(wallpaper))
            {
                return null;
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(wallpaper, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            bitmap.EndInit();
            bitmap.Freeze();

            ImageBrush brush = new ImageBrush(bitmap)
            {
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };

            // https://docs.microsoft.com/en-us/windows/desktop/Controls/themesfileformat-overview
            switch ($"{regWallpaperStyle}{regTileWallpaper}")
            {
                case "01": // Tiled
                    brush.AlignmentX = AlignmentX.Left;
                    brush.AlignmentY = AlignmentY.Top;
                    brush.TileMode = TileMode.Tile;
                    brush.Stretch = Stretch.Fill;
                    brush.Viewport = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    break;
                case "00": // Centered
                    brush.Stretch = Stretch.None;
                    break;
                case "60": // Fit
                    brush.Stretch = Stretch.Uniform;
                    break;
                case "100": // Fill
                case "220": // Span (per-screen fill for now)
                    brush.Stretch = Stretch.UniformToFill;
                    break;
                case "20": // Stretched
                default:
                    brush.Stretch = Stretch.Fill;
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

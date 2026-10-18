using CairoDesktop.Common;
using CairoDesktop.Infrastructure.ObjectModel;
using CairoDesktop.Interop;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.SupportingClasses;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace CairoDesktop.Services
{
    /// <summary>
    /// When Cairo is the shell, registers the shell window and paints the desktop background on every screen.
    /// </summary>
    public sealed class ShellWindowService : IDisposable
    {
        private readonly ILogger<ShellWindowService> _logger;
        private readonly IWindowManager _windowManager;
        private readonly Settings _settings;
        private readonly List<WallpaperWindow> _wallpaperWindows = new List<WallpaperWindow>();

        // CustomShellHost's desktop window, hidden while ours paint the background
        private IntPtr _hiddenHostDesktop;

        public ShellWindow ShellWindow { get; private set; }

        public ShellWindowService(ILogger<ShellWindowService> logger, IWindowManager windowManager, Settings settings)
        {
            _logger = logger;
            _windowManager = windowManager;
            _settings = settings;
        }

        public void Initialize()
        {
            if (!EnvironmentHelper.IsAppRunningAsShell || ShellWindow != null)
            {
                return;
            }

            // create native shell window; we must pass a native window's handle to SetShellWindow
            ShellWindow = new ShellWindow();
            ShellWindow.WallpaperChanged += WallpaperChanged;
            ShellWindow.WorkAreaChanged += WorkAreaChanged;

            if (ShellWindow.IsShellWindow)
            {
                _logger.LogDebug("Successfully set as shell window");
            }

            if (ShellHost.IsShellLauncher)
            {
                HideHostDesktop();
            }

            _windowManager.ScreensChanged += ScreensChanged;
            _settings.PropertyChanged += Settings_PropertyChanged;

            CreateWallpaperWindows();
        }

        /// <summary>
        /// Under Shell Launcher, CustomShellHost owns the shell window and a Progman that covers every screen with
        /// a plain background. Our wallpaper windows can't be its children (it's another process), so hide it and
        /// let them sit at the bottom of the z-order instead.
        /// </summary>
        private void HideHostDesktop()
        {
            uint ownPid;
            using (Process current = Process.GetCurrentProcess())
            {
                ownPid = (uint)current.Id;
            }

            IntPtr progman = IntPtr.Zero;
            while ((progman = User32.FindWindowEx(IntPtr.Zero, progman, "Progman", null)) != IntPtr.Zero)
            {
                User32.GetWindowThreadProcessId(progman, out uint pid);
                if (pid != ownPid && User32.IsWindowVisible(progman))
                {
                    User32.ShowWindow(progman, User32.SW_HIDE);
                    _hiddenHostDesktop = progman;
                    _logger.LogInformation($"Hid the Shell Launcher desktop window (pid {pid}) so Cairo can paint the background");
                    return;
                }
            }
        }

        private void RestoreHostDesktop()
        {
            if (_hiddenHostDesktop != IntPtr.Zero && User32.IsWindow(_hiddenHostDesktop))
            {
                User32.ShowWindow(_hiddenHostDesktop, User32.SW_SHOWNA);
            }

            _hiddenHostDesktop = IntPtr.Zero;
        }

        private void CreateWallpaperWindows()
        {
            // under Shell Launcher our ShellWindow isn't the shell window, so don't hide the wallpaper inside it
            IntPtr parent = ShellHost.IsShellLauncher ? IntPtr.Zero : ShellWindow.Handle;

            foreach (AppBarScreen screen in AppBarScreen.FromAllScreens())
            {
                WallpaperWindow window = new WallpaperWindow(screen, parent, _settings);
                window.Show();
                _wallpaperWindows.Add(window);
            }
        }

        private void DestroyWallpaperWindows()
        {
            foreach (WallpaperWindow window in _wallpaperWindows)
            {
                window.AllowClose = true;
                window.Close();
            }

            _wallpaperWindows.Clear();
        }

        private void ResetPosition(bool displaysChanged)
        {
            ShellWindow?.SetSize();

            if (displaysChanged)
            {
                DestroyWallpaperWindows();
                CreateWallpaperWindows();
                return;
            }

            List<AppBarScreen> screens = AppBarScreen.FromAllScreens();
            foreach (WallpaperWindow window in _wallpaperWindows)
            {
                AppBarScreen screen = screens.FirstOrDefault(s => s.DeviceName == window.Screen.DeviceName);
                if (screen != null)
                {
                    window.SetScreen(screen);
                }
            }
        }

        private void WallpaperChanged(object sender, EventArgs e)
        {
            ReloadBackgrounds();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == nameof(Settings.DesktopBackgroundPath) || e?.PropertyName == nameof(Settings.DesktopBackgroundStyle))
            {
                ReloadBackgrounds();
            }
        }

        private void ReloadBackgrounds()
        {
            foreach (WallpaperWindow window in _wallpaperWindows)
            {
                window.ReloadBackground();
            }
        }

        private void WorkAreaChanged(object sender, EventArgs e)
        {
            ResetPosition(false);
        }

        private void ScreensChanged(object sender, WindowManagerEventArgs e)
        {
            // treat dpi change as display change
            ResetPosition(e.DisplaysChanged || e.Reason == WindowManagerEventReason.DpiChange);
        }

        public void Dispose()
        {
            if (ShellWindow == null)
            {
                return;
            }

            _windowManager.ScreensChanged -= ScreensChanged;
            _settings.PropertyChanged -= Settings_PropertyChanged;
            DestroyWallpaperWindows();
            RestoreHostDesktop();

            ShellWindow.WallpaperChanged -= WallpaperChanged;
            ShellWindow.WorkAreaChanged -= WorkAreaChanged;
            ShellWindow.Dispose();
            ShellWindow = null;
        }
    }
}

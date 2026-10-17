using CairoDesktop.Infrastructure.ObjectModel;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.SupportingClasses;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Services
{
    /// <summary>
    /// When Cairo is the shell, registers the shell window and paints the wallpaper on every screen.
    /// </summary>
    public sealed class ShellWindowService : IDisposable
    {
        private readonly ILogger<ShellWindowService> _logger;
        private readonly IWindowManager _windowManager;
        private readonly List<WallpaperWindow> _wallpaperWindows = new List<WallpaperWindow>();

        public ShellWindow ShellWindow { get; private set; }

        public ShellWindowService(ILogger<ShellWindowService> logger, IWindowManager windowManager)
        {
            _logger = logger;
            _windowManager = windowManager;
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

            _windowManager.ScreensChanged += ScreensChanged;

            CreateWallpaperWindows();
        }

        private void CreateWallpaperWindows()
        {
            foreach (AppBarScreen screen in AppBarScreen.FromAllScreens())
            {
                WallpaperWindow window = new WallpaperWindow(screen, ShellWindow.Handle);
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
            DestroyWallpaperWindows();

            ShellWindow.WallpaperChanged -= WallpaperChanged;
            ShellWindow.WorkAreaChanged -= WorkAreaChanged;
            ShellWindow.Dispose();
            ShellWindow = null;
        }
    }
}

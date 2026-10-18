using CairoDesktop.Tiling.Core;
using CairoDesktop.Tiling.ViewModels;
using ManagedShell.AppBar;
using System;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// The tiling window manager. Methods that take a screen act on the focused screen when it is null.
    /// All members must be used on the UI thread, except <see cref="EmergencyRestore"/>.
    /// </summary>
    public interface ITilingManager
    {
        bool IsEnabled { get; }

        event EventHandler EnabledChanged;

        /// <summary>
        /// The view model for a screen's tag bar and window list, matched by device name.
        /// </summary>
        ScreenTagsViewModel GetScreenModel(AppBarScreen screen);

        void ViewTag(AppBarScreen screen, int index);

        void ViewPreviousTag(AppBarScreen screen);

        void MoveFocusedToTag(int index);

        void MoveWindowToTag(IntPtr hwnd, int index);

        void MoveFocusedToScreen(bool next);

        void FocusDirection(Direction direction);

        void FocusNext();

        void FocusPrev();

        void FocusScreen(bool next);

        void SwapDirection(Direction direction);

        void MoveDirection(Direction direction);

        void Resize(Direction direction);

        void ToggleOrientation();

        void Equalize();

        void ToggleFloating();

        void ToggleFloating(IntPtr hwnd);

        void ToggleFullscreen();

        void CloseFocused();

        void CloseWindow(IntPtr hwnd);

        void FocusWindow(IntPtr hwnd);

        /// <summary>
        /// Pauses focus follows mouse until the result is disposed, for transient windows that close when
        /// deactivated. Must be called on the UI thread.
        /// </summary>
        IDisposable SuspendFocusFollowsMouse();

        /// <summary>
        /// Shows every window hidden by the tiling manager and restores the lock policy. Safe to call from any
        /// thread, including crash handlers.
        /// </summary>
        void EmergencyRestore();
    }
}

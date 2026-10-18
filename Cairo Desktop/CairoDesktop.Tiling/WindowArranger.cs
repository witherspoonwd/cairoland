using CairoDesktop.Interop;
using CairoDesktop.Tiling.Core;
using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Moves, hides, shows and focuses windows.
    /// </summary>
    public class WindowArranger
    {
        private static readonly TimeSpan ExpectedEventLifetime = TimeSpan.FromSeconds(2);

        // where Windows itself puts minimized windows; off every monitor
        private const int ParkedPosition = -32000;

        private readonly HiddenWindowJournal _journal;
        private readonly Dictionary<IntPtr, DateTime> _expectedShow = new Dictionary<IntPtr, DateTime>();
        private readonly Dictionary<IntPtr, DateTime> _expectedHide = new Dictionary<IntPtr, DateTime>();

        public WindowArranger(HiddenWindowJournal journal)
        {
            _journal = journal;
        }

        #region Arranging

        /// <summary>
        /// Positions windows so their visible frames match the given rects. Returns windows that refused to move
        /// because of access denied (elevated windows under UIPI).
        /// </summary>
        public List<IntPtr> Apply(IReadOnlyList<(IntPtr Hwnd, RectI Rect)> placements)
        {
            List<IntPtr> denied = new List<IntPtr>();
            if (placements.Count == 0)
            {
                return denied;
            }

            List<(IntPtr Hwnd, RectI Outer)> targets = new List<(IntPtr, RectI)>(placements.Count);
            foreach (var placement in placements)
            {
                if (!User32.IsWindow(placement.Hwnd))
                {
                    continue;
                }

                if (User32.IsZoomed(placement.Hwnd))
                {
                    User32.ShowWindow(placement.Hwnd, User32.SW_RESTORE);
                }

                targets.Add((placement.Hwnd, ToOuterRect(placement.Hwnd, placement.Rect)));
            }

            const uint flags = User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER | User32.SWP_FRAMECHANGED;

            if (TryApplyBatch(targets, flags))
            {
                return denied;
            }

            // the batch fails as a whole if any window refuses, so fall back to one at a time
            foreach (var target in targets)
            {
                if (!User32.SetWindowPos(target.Hwnd, IntPtr.Zero, target.Outer.X, target.Outer.Y, target.Outer.W, target.Outer.H, flags))
                {
                    int error = Marshal.GetLastWin32Error();
                    ShellLogger.Debug($"WindowArranger: SetWindowPos failed for 0x{target.Hwnd.ToInt64():X} with error {error}");
                    if (error == User32.ERROR_ACCESS_DENIED)
                    {
                        denied.Add(target.Hwnd);
                    }
                }
            }

            return denied;
        }

        private static bool TryApplyBatch(List<(IntPtr Hwnd, RectI Outer)> targets, uint flags)
        {
            IntPtr hdwp = User32.BeginDeferWindowPos(targets.Count);
            if (hdwp == IntPtr.Zero)
            {
                return false;
            }

            foreach (var target in targets)
            {
                hdwp = User32.DeferWindowPos(hdwp, target.Hwnd, IntPtr.Zero, target.Outer.X, target.Outer.Y, target.Outer.W, target.Outer.H, flags);
                if (hdwp == IntPtr.Zero)
                {
                    // the system freed the batch
                    ShellLogger.Debug($"WindowArranger: DeferWindowPos failed for 0x{target.Hwnd.ToInt64():X} with error {Marshal.GetLastWin32Error()}");
                    return false;
                }
            }

            if (!User32.EndDeferWindowPos(hdwp))
            {
                ShellLogger.Debug($"WindowArranger: EndDeferWindowPos failed with error {Marshal.GetLastWin32Error()}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Windows 10+ windows have invisible resize borders. Expands the visible rect by those borders so the
        /// visible frame lands where we want.
        /// </summary>
        public static RectI ToOuterRect(IntPtr hwnd, RectI visible)
        {
            if (!User32.GetWindowRect(hwnd, out User32.RECT window) || !Dwmapi.TryGetExtendedFrameBounds(hwnd, out User32.RECT frame))
            {
                return visible;
            }

            int left = frame.Left - window.Left;
            int top = frame.Top - window.Top;
            int right = window.Right - frame.Right;
            int bottom = window.Bottom - frame.Bottom;

            // guard against garbage from minimized or hidden windows
            if (left < 0 || top < 0 || right < 0 || bottom < 0 || left > 32 || top > 32 || right > 32 || bottom > 32)
            {
                return visible;
            }

            return new RectI(visible.X - left, visible.Y - top, visible.W + left + right, visible.H + top + bottom);
        }

        /// <summary>
        /// The visible frame of a window, without invisible borders.
        /// </summary>
        public static RectI GetVisibleRect(IntPtr hwnd)
        {
            if (Dwmapi.TryGetExtendedFrameBounds(hwnd, out User32.RECT frame) && frame.Width > 0)
            {
                return RectI.FromEdges(frame.Left, frame.Top, frame.Right, frame.Bottom);
            }

            User32.GetWindowRect(hwnd, out User32.RECT rect);
            return RectI.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        public static void MoveTo(IntPtr hwnd, RectI visible)
        {
            RectI outer = ToOuterRect(hwnd, visible);
            User32.SetWindowPos(hwnd, IntPtr.Zero, outer.X, outer.Y, outer.W, outer.H, User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER);
        }

        #endregion

        #region Visibility

        public void Hide(ManagedWindow window)
        {
            if (window.HideMode == HideMode.Offscreen)
            {
                Park(window.Hwnd);
                return;
            }

            Hide(window.Hwnd);
        }

        public void Show(ManagedWindow window)
        {
            // also covers a window whose hide mode changed while it was parked
            if (_journal.IsParked(window.Hwnd))
            {
                Unpark(window.Hwnd);
                return;
            }

            Show(window.Hwnd);
        }

        /// <summary>
        /// Moves a window far off screen, keeping it shown. The original rect goes into the journal so a crash
        /// can't leave it there.
        /// </summary>
        private void Park(IntPtr hwnd)
        {
            if (!User32.GetWindowRect(hwnd, out User32.RECT rect))
            {
                return;
            }

            _journal.AddParked(hwnd, rect);
            ShellLogger.Debug($"WindowArranger: Parking 0x{hwnd.ToInt64():X} off screen");
            User32.SetWindowPos(hwnd, IntPtr.Zero, ParkedPosition, ParkedPosition, 0, 0,
                User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER);
        }

        private void Unpark(IntPtr hwnd)
        {
            HiddenWindowJournal.ParkedWindow parked = _journal.RemoveParked(hwnd);
            if (parked == null)
            {
                return;
            }

            // the next layout places tiled windows; this puts floating ones back where they were
            ShellLogger.Debug($"WindowArranger: Bringing 0x{hwnd.ToInt64():X} back on screen");
            User32.SetWindowPos(hwnd, IntPtr.Zero, parked.X, parked.Y, parked.W, parked.H,
                User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER);
        }

        private void Hide(IntPtr hwnd)
        {
            Expect(_expectedHide, hwnd);
            _journal.AddHidden(hwnd);
            ShellLogger.Debug($"WindowArranger: Hiding 0x{hwnd.ToInt64():X}");
            // tag switches should be instant; WindowAnimations covers windows DWM won't let us change
            Dwmapi.DisableTransitions(hwnd);
            User32.ShowWindow(hwnd, User32.SW_HIDE);
        }

        private void Show(IntPtr hwnd)
        {
            Expect(_expectedShow, hwnd);
            ShellLogger.Debug($"WindowArranger: Showing 0x{hwnd.ToInt64():X}");
            Dwmapi.DisableTransitions(hwnd);
            User32.ShowWindow(hwnd, User32.SW_SHOWNA);
            _journal.RemoveHidden(hwnd);
        }

        /// <summary>
        /// Returns true (once) if a show event for this window was caused by <see cref="Show"/>.
        /// </summary>
        public bool ConsumeExpectedShow(IntPtr hwnd) => Consume(_expectedShow, hwnd);

        /// <summary>
        /// Returns true (once) if a hide event for this window was caused by <see cref="Hide"/>.
        /// </summary>
        public bool ConsumeExpectedHide(IntPtr hwnd) => Consume(_expectedHide, hwnd);

        private static void Expect(Dictionary<IntPtr, DateTime> expected, IntPtr hwnd)
        {
            expected[hwnd] = DateTime.UtcNow;
        }

        private static bool Consume(Dictionary<IntPtr, DateTime> expected, IntPtr hwnd)
        {
            DateTime now = DateTime.UtcNow;
            foreach (IntPtr stale in expected.Where(pair => now - pair.Value > ExpectedEventLifetime).Select(pair => pair.Key).ToList())
            {
                expected.Remove(stale);
            }

            return expected.Remove(hwnd);
        }

        public void Forget(IntPtr hwnd)
        {
            _expectedShow.Remove(hwnd);
            _expectedHide.Remove(hwnd);
            _journal.RemoveHidden(hwnd);

            // a window we stop managing must not stay off screen
            if (_journal.IsParked(hwnd) && User32.IsWindow(hwnd))
            {
                Unpark(hwnd);
            }
            else
            {
                _journal.RemoveParked(hwnd);
            }
        }

        #endregion

        #region Focus

        /// <summary>
        /// Activates and raises a window, working around the foreground lock if needed.
        /// </summary>
        public static void Focus(IntPtr hwnd, bool raise = true)
        {
            if (!User32.IsWindow(hwnd))
            {
                return;
            }

            if (User32.IsIconic(hwnd))
            {
                User32.ShowWindow(hwnd, User32.SW_RESTORE);
            }

            if (!User32.SetForegroundWindow(hwnd))
            {
                // A synthetic Alt tap makes this process the last input source, which lifts the foreground lock.
                // Alt goes down before the switch and up after it, so neither window sees a whole tap and
                // enters menu mode.
                SendAlt(keyUp: false);
                User32.SetForegroundWindow(hwnd);
                SendAlt(keyUp: true);
            }

            if (raise)
            {
                User32.SetWindowPos(hwnd, User32.HWND_TOP, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
            }
        }

        private static void SendAlt(bool keyUp)
        {
            User32.INPUT[] inputs =
            {
                new User32.INPUT { type = User32.INPUT_KEYBOARD, U = new User32.InputUnion { ki = new User32.KEYBDINPUT { wVk = User32.VK_MENU, dwFlags = keyUp ? User32.KEYEVENTF_KEYUP : 0 } } }
            };

            User32.SendInput((uint)inputs.Length, inputs, User32.INPUT.Size);
        }

        #endregion
    }
}

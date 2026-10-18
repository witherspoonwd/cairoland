using CairoDesktop.Common;
using CairoDesktop.Interop;
using ManagedShell.Common.Logging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Holding the tiling modifier and dragging with the left button moves a window from anywhere inside it, as
    /// if it were dragged by its title bar. The click is swallowed so the app never sees it.
    /// </summary>
    public sealed class WindowDragger : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int LLMHF_INJECTED = 0x01;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const ushort VK_MASK = 0xE8; // unassigned; used the same way AutoHotkey masks the Win key

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public User32.POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly Settings _settings;
        private readonly Func<IntPtr, bool> _canDrag;
        private readonly Action<IntPtr> _dragStarted;
        private readonly Action<IntPtr> _dragEnded;
        private readonly LowLevelMouseProc _proc;

        private IntPtr _hook;
        private Dispatcher _dispatcher;

        private IntPtr _dragHwnd;
        private User32.POINT _dragStart;
        private User32.RECT _startRect;
        private User32.POINT _latest;
        private bool _movePending;

        public WindowDragger(Settings settings, Func<IntPtr, bool> canDrag, Action<IntPtr> dragStarted, Action<IntPtr> dragEnded)
        {
            _settings = settings;
            _canDrag = canDrag;
            _dragStarted = dragStarted;
            _dragEnded = dragEnded;
            _proc = HookCallback;
        }

        public void Enable()
        {
            if (_hook != IntPtr.Zero)
            {
                return;
            }

            _dispatcher = Dispatcher.CurrentDispatcher;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module.ModuleName), 0);
            }

            if (_hook == IntPtr.Zero)
            {
                ShellLogger.Warning($"WindowDragger: Unable to hook the mouse: {Marshal.GetLastWin32Error()}");
            }
        }

        public void Disable()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            if (_dragHwnd != IntPtr.Zero)
            {
                EndDrag();
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
            {
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            if ((data.flags & LLMHF_INJECTED) != 0)
            {
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            int message = wParam.ToInt32();

            // this runs inside every mouse event system-wide, so only cheap work here; the rest is dispatched
            if (_dragHwnd == IntPtr.Zero)
            {
                if (message == WM_LBUTTONDOWN && ModifierHeld())
                {
                    IntPtr hwnd = User32.GetAncestor(User32.WindowFromPoint(data.pt), User32.GA_ROOT);
                    if (hwnd != IntPtr.Zero && _canDrag(hwnd) && User32.GetWindowRect(hwnd, out _startRect))
                    {
                        BeginDrag(hwnd, data.pt);
                        return new IntPtr(1);
                    }
                }
            }
            else if (message == WM_MOUSEMOVE)
            {
                _latest = data.pt;
                QueueMove();
            }
            else if (message == WM_LBUTTONUP)
            {
                _latest = data.pt;
                EndDrag();
                return new IntPtr(1);
            }
            else if (message == WM_RBUTTONDOWN)
            {
                // the button up never arrived (another hook ate it, or the desktop switched); stop here
                EndDrag();
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool ModifierHeld()
        {
            HotKeyModifier modifiers = TilingKeymap.ParseBaseModifier(_settings.TilingModifier);
            return (!modifiers.HasFlag(HotKeyModifier.Win) || IsDown(VK_LWIN) || IsDown(VK_RWIN))
                && (!modifiers.HasFlag(HotKeyModifier.Alt) || IsDown(User32.VK_MENU))
                && (!modifiers.HasFlag(HotKeyModifier.Ctrl) || IsDown(VK_CONTROL))
                && (!modifiers.HasFlag(HotKeyModifier.Shift) || IsDown(VK_SHIFT));
        }

        private static bool IsDown(int vKey) => User32.GetAsyncKeyState(vKey) < 0;

        private void BeginDrag(IntPtr hwnd, User32.POINT point)
        {
            _dragHwnd = hwnd;
            _dragStart = point;
            _latest = point;

            // the shell saw only the modifier go down and up, so it would open Start (or an app's menu bar for Alt)
            SendMaskKey();

            _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                WindowArranger.Focus(hwnd);
                _dragStarted(hwnd);
            }));
        }

        private void QueueMove()
        {
            // coalesce: the window only needs to catch up to the latest point
            if (_movePending)
            {
                return;
            }

            _movePending = true;
            IntPtr hwnd = _dragHwnd;
            _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _movePending = false;
                if (hwnd == _dragHwnd)
                {
                    MoveWindow(hwnd, async: true);
                }
            }));
        }

        private void MoveWindow(IntPtr hwnd, bool async)
        {
            int x = _startRect.Left + _latest.X - _dragStart.X;
            int y = _startRect.Top + _latest.Y - _dragStart.Y;

            // async while dragging so a hung app can't stall the hook's thread
            uint flags = User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER;
            User32.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, async ? flags | User32.SWP_ASYNCWINDOWPOS : flags);
        }

        private void EndDrag()
        {
            IntPtr hwnd = _dragHwnd;
            _dragHwnd = IntPtr.Zero;

            _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                // in place before the drop is handled, which reads the window's position
                MoveWindow(hwnd, async: false);
                _dragEnded(hwnd);
            }));
        }

        private static void SendMaskKey()
        {
            User32.INPUT[] inputs =
            {
                new User32.INPUT { type = User32.INPUT_KEYBOARD, U = new User32.InputUnion { ki = new User32.KEYBDINPUT { wVk = VK_MASK } } },
                new User32.INPUT { type = User32.INPUT_KEYBOARD, U = new User32.InputUnion { ki = new User32.KEYBDINPUT { wVk = VK_MASK, dwFlags = User32.KEYEVENTF_KEYUP } } }
            };

            User32.SendInput((uint)inputs.Length, inputs, User32.INPUT.Size);
        }

        public void Dispose()
        {
            Disable();
        }
    }
}

using CairoDesktop.Interop;
using ManagedShell.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace CairoDesktop.Common
{
    /// <remarks>
    /// Created from:
    /// http://www.dylansweb.com/2014/10/low-level-global-keyboard-hook-sink-in-c-net/
    /// https://www.codeproject.com/Articles/19004/A-Simple-C-Global-Low-Level-Keyboard-Hook
    ///
    /// When an OnKeyDown handler swallows a key, the matching key up is swallowed too, so no key is left
    /// half-pressed. Swallowing a key while Win is held masks the Win key up with a dummy keystroke, so
    /// the shell doesn't treat it as a lone Win press.
    /// </remarks>
    public class LowLevelKeyboardListener
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int LLKHF_INJECTED = 0x10;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_ESCAPE = 0x1B;
        private const ushort VK_MASK = 0xE8; // unassigned; used the same way AutoHotkey masks the Win key

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<KeyEventArgs> OnKeyPressed;
        public event EventHandler<KeyEventArgs> OnKeyDown;
        public event EventHandler<KeyEventArgs> OnKeyUp;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private List<int> keysPressed = new List<int>();
        private readonly HashSet<int> _keysDown = new HashSet<int>();
        private readonly HashSet<int> _swallowed = new HashSet<int>();
        private bool _maskWinUp;

        public bool IsHooked => _hookID != IntPtr.Zero;

        /// <summary>
        /// When set, a lone Win press (and Ctrl+Esc) doesn't reach the shell, so the Start menu never opens. Used
        /// under Shell Launcher, where Start opens but can't be used.
        /// </summary>
        public bool MaskLoneWin { get; set; }

        public LowLevelKeyboardListener()
        {
            _proc = HookCallback;
        }

        public void HookKeyboard()
        {
            if (_hookID == IntPtr.Zero)
            {
                _hookID = SetHook(_proc);
            }
        }

        public void UnHookKeyboard()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }

            _keysDown.Clear();
            _swallowed.Clear();
            keysPressed.Clear();
            _maskWinUp = false;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static readonly int[] ModifierKeys = { VK_LWIN, VK_RWIN, 0xA0, 0xA1, 0x10, 0xA2, 0xA3, 0x11, 0xA4, 0xA5, 0x12 };

        /// <summary>
        /// Drops modifiers we think are down but aren't. The hook misses key ups when its callback times out (the UI
        /// thread was busy, say opening the launcher right after Win+Space), and a stale Win turns every letter typed
        /// afterwards into a Win+letter hotkey.
        /// </summary>
        private void DropReleasedModifiers()
        {
            foreach (int vk in ModifierKeys)
            {
                if (_keysDown.Contains(vk) && User32.GetAsyncKeyState(vk) >= 0)
                {
                    _keysDown.Remove(vk);
                    keysPressed.Remove(vk);
                }
            }
        }

        private HotKeyModifier CurrentModifiers()
        {
            DropReleasedModifiers();

            HotKeyModifier modifiers = HotKeyModifier.None;
            if (_keysDown.Contains(VK_LWIN) || _keysDown.Contains(VK_RWIN)) modifiers |= HotKeyModifier.Win;
            if (_keysDown.Contains(0xA0) || _keysDown.Contains(0xA1) || _keysDown.Contains(0x10)) modifiers |= HotKeyModifier.Shift;
            if (_keysDown.Contains(0xA2) || _keysDown.Contains(0xA3) || _keysDown.Contains(0x11)) modifiers |= HotKeyModifier.Ctrl;
            if (_keysDown.Contains(0xA4) || _keysDown.Contains(0xA5) || _keysDown.Contains(0x12)) modifiers |= HotKeyModifier.Alt;
            return modifiers;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int vkCode = data.vkCode;

                // ignore synthetic input, including our own Win mask
                if ((data.flags & LLKHF_INJECTED) != 0)
                {
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);
                }

                bool isDown = wParam == (IntPtr)NativeMethods.WM.KEYDOWN || wParam == (IntPtr)NativeMethods.WM.SYSKEYDOWN;
                bool isUp = wParam == (IntPtr)NativeMethods.WM.KEYUP || wParam == (IntPtr)NativeMethods.WM.SYSKEYUP;

                if (isDown)
                {
                    // keep track of pressed keys so we don't intercept hotkeys
                    if (!keysPressed.Contains(vkCode))
                        keysPressed.Add(vkCode);

                    HotKeyModifier modifiers = CurrentModifiers();
                    _keysDown.Add(vkCode);

                    if (_swallowed.Contains(vkCode))
                    {
                        // auto-repeat of a swallowed key
                        return new IntPtr(1);
                    }

                    if (MaskLoneWin && vkCode == VK_ESCAPE && modifiers == HotKeyModifier.Ctrl)
                    {
                        // Ctrl+Esc opens Start too
                        _swallowed.Add(vkCode);
                        return new IntPtr(1);
                    }

                    var kpa = new KeyEventArgs(KeyInterop.KeyFromVirtualKey(vkCode), modifiers);
                    OnKeyDown?.Invoke(this, kpa);
                    if (kpa.Handled)
                    {
                        _swallowed.Add(vkCode);
                        if ((modifiers & HotKeyModifier.Win) != 0)
                        {
                            _maskWinUp = true;
                        }

                        return new IntPtr(1);
                    }
                }

                // act only when key is raised
                if (isUp)
                {
                    _keysDown.Remove(vkCode);

                    if (_swallowed.Remove(vkCode))
                    {
                        keysPressed.Clear();
                        return new IntPtr(1);
                    }

                    if ((vkCode == VK_LWIN || vkCode == VK_RWIN) && _maskWinUp)
                    {
                        _maskWinUp = false;
                        SendMaskKey();
                    }
                    else if ((vkCode == VK_LWIN || vkCode == VK_RWIN) && MaskLoneWin && keysPressed.Count == 1)
                    {
                        // nothing else was pressed since Win went down, so this would open Start
                        SendMaskKey();
                    }

                    // if more than one key was pressed before a key was raised, user attempted hotkey
                    if (keysPressed.Count == 1 && OnKeyPressed != null)
                    {
                        var kpaPressed = new KeyEventArgs(KeyInterop.KeyFromVirtualKey(vkCode));
                        OnKeyPressed?.Invoke(this, kpaPressed);
                        if (kpaPressed.Handled)
                            return new IntPtr(1);
                    }

                    // reset pressed keys
                    keysPressed.Clear();

                    var kpaUp = new KeyEventArgs(KeyInterop.KeyFromVirtualKey(vkCode));
                    OnKeyUp?.Invoke(this, kpaUp);
                    if (kpaUp.Handled)
                        return new IntPtr(1);
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
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
    }

    public class KeyEventArgs : EventArgs
    {
        public Key Key { get; private set; }
        public HotKeyModifier Modifiers { get; private set; }
        public bool Handled { get; set; }

        public KeyEventArgs(Key key) : this(key, HotKeyModifier.None)
        {
        }

        public KeyEventArgs(Key key, HotKeyModifier modifiers)
        {
            Key = key;
            Modifiers = modifiers;
            Handled = false;
        }
    }
}

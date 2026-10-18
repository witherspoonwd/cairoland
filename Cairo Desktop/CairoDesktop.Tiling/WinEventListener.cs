using CairoDesktop.Interop;
using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CairoDesktop.Tiling
{
    public enum WinEventKind
    {
        Shown,
        Hidden,
        Destroyed,
        Foreground,
        MoveSizeStart,
        MoveSizeEnd,
        MinimizeStart,
        MinimizeEnd,
        NameChanged,
        LocationChanged,
        Cloaked,
        Uncloaked
    }

    public class WinEventArgs : EventArgs
    {
        public WinEventKind Kind { get; }

        public IntPtr Hwnd { get; }

        public WinEventArgs(WinEventKind kind, IntPtr hwnd)
        {
            Kind = kind;
            Hwnd = hwnd;
        }
    }

    /// <summary>
    /// Out-of-context WinEvent hooks for top-level window events. Must be started on a thread with a message loop
    /// (the WPF dispatcher thread); events are raised on that thread.
    /// </summary>
    public sealed class WinEventListener : IDisposable
    {
        private static readonly (uint Min, uint Max)[] Ranges =
        {
            (User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND),
            (User32.EVENT_SYSTEM_MOVESIZESTART, User32.EVENT_SYSTEM_MOVESIZEEND),
            (User32.EVENT_SYSTEM_MINIMIZESTART, User32.EVENT_SYSTEM_MINIMIZEEND),
            (User32.EVENT_OBJECT_DESTROY, User32.EVENT_OBJECT_HIDE),
            (User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_NAMECHANGE),
            (User32.EVENT_OBJECT_CLOAKED, User32.EVENT_OBJECT_UNCLOAKED)
        };

        // kept in a field so the delegate is not garbage collected while hooked
        private readonly User32.WinEventDelegate _callback;
        private readonly List<IntPtr> _hooks = new List<IntPtr>();
        private readonly HashSet<IntPtr> _ownHooks = new HashSet<IntPtr>();

        public event EventHandler<WinEventArgs> WindowEvent;

        public bool IsStarted => _hooks.Count > 0;

        public WinEventListener()
        {
            _callback = OnWinEvent;
        }

        public void Start()
        {
            if (IsStarted)
            {
                return;
            }

            uint ownPid;
            using (Process process = Process.GetCurrentProcess())
            {
                ownPid = (uint)process.Id;
            }

            foreach (var range in Ranges)
            {
                Hook(range, 0, User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS, own: false);

                // Cairo's own windows only reach the manager if they opted in with TilingWindow.IsManaged
                Hook(range, ownPid, User32.WINEVENT_OUTOFCONTEXT, own: true);
            }
        }

        private void Hook((uint Min, uint Max) range, uint processId, uint flags, bool own)
        {
            IntPtr hook = User32.SetWinEventHook(range.Min, range.Max, IntPtr.Zero, _callback, processId, 0, flags);

            if (hook == IntPtr.Zero)
            {
                ShellLogger.Warning($"WinEventListener: Unable to hook events 0x{range.Min:X}-0x{range.Max:X}");
                return;
            }

            _hooks.Add(hook);
            if (own)
            {
                _ownHooks.Add(hook);
            }
        }

        public void Stop()
        {
            foreach (IntPtr hook in _hooks)
            {
                User32.UnhookWinEvent(hook);
            }

            _hooks.Clear();
            _ownHooks.Clear();
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF)
            {
                return;
            }

            WinEventKind kind;
            switch (eventType)
            {
                case User32.EVENT_OBJECT_SHOW: kind = WinEventKind.Shown; break;
                case User32.EVENT_OBJECT_HIDE: kind = WinEventKind.Hidden; break;
                case User32.EVENT_OBJECT_DESTROY: kind = WinEventKind.Destroyed; break;
                case User32.EVENT_SYSTEM_FOREGROUND: kind = WinEventKind.Foreground; break;
                case User32.EVENT_SYSTEM_MOVESIZESTART: kind = WinEventKind.MoveSizeStart; break;
                case User32.EVENT_SYSTEM_MOVESIZEEND: kind = WinEventKind.MoveSizeEnd; break;
                case User32.EVENT_SYSTEM_MINIMIZESTART: kind = WinEventKind.MinimizeStart; break;
                case User32.EVENT_SYSTEM_MINIMIZEEND: kind = WinEventKind.MinimizeEnd; break;
                case User32.EVENT_OBJECT_NAMECHANGE: kind = WinEventKind.NameChanged; break;
                case User32.EVENT_OBJECT_LOCATIONCHANGE: kind = WinEventKind.LocationChanged; break;
                case User32.EVENT_OBJECT_CLOAKED: kind = WinEventKind.Cloaked; break;
                case User32.EVENT_OBJECT_UNCLOAKED: kind = WinEventKind.Uncloaked; break;
                default: return;
            }

            // a destroyed window can no longer be queried, so let the manager decide whether it knew it
            if (kind != WinEventKind.Destroyed && User32.GetAncestor(hwnd, User32.GA_ROOT) != hwnd)
            {
                return;
            }

            // a destroyed window's HwndSource may already be gone; the manager ignores windows it never knew
            if (kind != WinEventKind.Destroyed && _ownHooks.Contains(hWinEventHook) && !TilingWindow.IsOwnWindowManaged(hwnd))
            {
                return;
            }

            try
            {
                WindowEvent?.Invoke(this, new WinEventArgs(kind, hwnd));
            }
            catch (Exception ex)
            {
                // never let an exception escape into the native callback
                ShellLogger.Error($"WinEventListener: Error handling {kind} for 0x{hwnd.ToInt64():X}: {ex}");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

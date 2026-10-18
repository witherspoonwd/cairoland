using CairoDesktop.Common;
using CairoDesktop.Infrastructure.ObjectModel;
using CairoDesktop.Interop;
using CairoDesktop.Tiling.Core;
using CairoDesktop.Tiling.ViewModels;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;

namespace CairoDesktop.Tiling
{
    public sealed class TilingManager : ITilingManager, IDisposable
    {
        private const double ResizeStep = 0.05;
        private const int CursorPollIntervalMs = 100;

        // how long the user must be at the computer with a monitor gone before its windows move elsewhere
        private const int ReconnectGraceMs = 15000;

        private readonly ILogger<TilingManager> _logger;
        private readonly Settings _settings;
        private readonly IWindowManager _windowManager;
        private readonly HiddenWindowJournal _journal;
        private readonly WindowArranger _arranger;
        private readonly WindowFilter _filter;
        private readonly WinEventListener _listener = new WinEventListener();
        private readonly FocusFollowsMouse _focusFollowsMouse;
        private readonly WindowDragger _dragger;
        private readonly WorkstationLockManager _lockManager;
        private readonly WindowAnimations _animations;

        private readonly List<MonitorState> _monitors = new List<MonitorState>();
        // disconnected monitors, kept with their tags so a monitor that wakes from sleep gets its windows back
        private readonly List<MonitorState> _parkedMonitors = new List<MonitorState>();
        private readonly Dictionary<IntPtr, ManagedWindow> _windows = new Dictionary<IntPtr, ManagedWindow>();
        private readonly Dictionary<string, ScreenTagsViewModel> _viewModels = new Dictionary<string, ScreenTagsViewModel>();
        private readonly HashSet<MonitorState> _dirty = new HashSet<MonitorState>();
        private bool _layoutQueued;
        private IntPtr _moveSizeHwnd;
        private int _lastDisplayChangeTick;
        private Dispatcher _dispatcher;

        // the mouse moved onto a screen showing an empty tag or onto its desktop, so that screen has focus until
        // something is activated
        private MonitorState _mouseFocusedMonitor;
        private MonitorState _lastCursorMonitor;
        private DispatcherTimer _cursorTimer;

        // the last app window to have focus, given it back when the desktop is activated
        private IntPtr _lastForeground;
        private IntPtr _restoringForeground;

        public bool IsEnabled { get; private set; }

        public event EventHandler EnabledChanged;

        public TilingManager(ILogger<TilingManager> logger, Settings settings, IWindowManager windowManager, HiddenWindowJournal journal, WorkstationLockManager lockManager)
        {
            _logger = logger;
            _settings = settings;
            _windowManager = windowManager;
            _journal = journal;
            _lockManager = lockManager;
            _arranger = new WindowArranger(journal);
            _animations = new WindowAnimations(journal);
            _filter = new WindowFilter(settings);
            _focusFollowsMouse = new FocusFollowsMouse(settings, hwnd => _windows.ContainsKey(hwnd));

            // a modifier drag goes through the same paths as a title bar drag
            _dragger = new WindowDragger(settings, CanDrag,
                hwnd => _moveSizeHwnd = hwnd,
                hwnd =>
                {
                    _moveSizeHwnd = IntPtr.Zero;
                    OnMoveSizeEnd(hwnd);
                });

            _listener.WindowEvent += Listener_WindowEvent;
        }

        public bool ShouldBeEnabled => _settings.EnableTiling && (EnvironmentHelper.IsAppRunningAsShell || _settings.EnableTilingOutsideShell);

        #region Start and stop

        /// <summary>
        /// Called once at startup on the UI thread. Restores anything left over from a crash, then starts tiling
        /// if it is enabled.
        /// </summary>
        public void Initialize()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            // recover from a previous crash before touching any window
            _journal.RestoreAll();

            _settings.PropertyChanged += Settings_PropertyChanged;
            _windowManager.ScreensChanged += WindowManager_ScreensChanged;

            if (ShouldBeEnabled)
            {
                Start();
            }
        }

        private void Start()
        {
            if (IsEnabled)
            {
                return;
            }

            _logger.LogInformation("Starting tiling window manager");
            _journal.RestoreAll();
            _animations.Suppress();

            BuildMonitors();

            List<IntPtr> found = new List<IntPtr>();
            User32.EnumWindows((hwnd, lParam) =>
            {
                found.Add(hwnd);
                return true;
            }, IntPtr.Zero);

            // EnumWindows is in z-order, top first; insert bottom-up so the focused window ends up last
            found.Reverse();
            foreach (IntPtr hwnd in found)
            {
                TryManage(hwnd, focus: false, atCursor: false);
            }

            IsEnabled = true;

            IntPtr foreground = User32.GetForegroundWindow();
            if (_windows.ContainsKey(foreground))
            {
                OnForeground(foreground);
            }

            _listener.Start();
            // when the lock policy is unwritable, Cairo's Setup check offers the fix at startup
            _lockManager.Enable();
            _focusFollowsMouse.Enable();
            _dragger.Enable();

            _lastCursorMonitor = GetCursorMonitor();
            _cursorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(CursorPollIntervalMs) };
            _cursorTimer.Tick += CursorTimer_Tick;
            _cursorTimer.Start();

            foreach (MonitorState monitor in _monitors)
            {
                MarkDirty(monitor);
                monitor.ViewModel.IsActive = true;
            }

            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Stop()
        {
            if (!IsEnabled)
            {
                return;
            }

            _logger.LogInformation("Stopping tiling window manager");
            IsEnabled = false;

            _listener.Stop();
            _focusFollowsMouse.Disable();
            _dragger.Disable();
            _lockManager.Disable();

            if (_cursorTimer != null)
            {
                _cursorTimer.Stop();
                _cursorTimer.Tick -= CursorTimer_Tick;
                _cursorTimer = null;
            }

            _mouseFocusedMonitor = null;
            _lastCursorMonitor = null;
            _lastForeground = IntPtr.Zero;
            _restoringForeground = IntPtr.Zero;

            foreach (ManagedWindow window in _windows.Values)
            {
                if (window.IsHiddenByUs)
                {
                    _arranger.Show(window);
                }
            }

            _animations.Restore();
            _journal.RestoreAll();

            foreach (MonitorState monitor in _monitors)
            {
                ClearViewModel(monitor.ViewModel);
            }

            _windows.Clear();
            _monitors.Clear();
            _parkedMonitors.Clear();
            _dirty.Clear();

            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateEnabled()
        {
            if (ShouldBeEnabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public void EmergencyRestore()
        {
            try
            {
                _focusFollowsMouse.Disable();
            }
            catch
            {
                // best effort; the journal restore below matters more
            }

            // the journal restores window animations too
            _journal.RestoreAll();
        }

        public void Dispose()
        {
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _windowManager.ScreensChanged -= WindowManager_ScreensChanged;
            Stop();
            _listener.Dispose();
            _journal.RestoreAll();
        }

        #endregion

        #region Monitors

        private void BuildMonitors()
        {
            _monitors.Clear();
            foreach (AppBarScreen screen in GetScreens())
            {
                _monitors.Add(new MonitorState(screen, GetOrCreateViewModel(screen.DeviceName)));
            }
        }

        private List<AppBarScreen> GetScreens()
        {
            List<AppBarScreen> screens = _windowManager.ScreenState;
            if (screens == null || screens.Count == 0)
            {
                screens = AppBarScreen.FromAllScreens();
            }

            return screens;
        }

        private MonitorState PrimaryMonitor => _monitors.FirstOrDefault(m => m.Screen.Primary) ?? _monitors.FirstOrDefault();

        private MonitorState GetMonitorByHandle(IntPtr hmonitor)
        {
            return _monitors.FirstOrDefault(m => m.HMonitor == hmonitor);
        }

        private MonitorState GetMonitor(AppBarScreen screen)
        {
            return screen == null ? GetFocusedMonitor() : _monitors.FirstOrDefault(m => m.DeviceName == screen.DeviceName);
        }

        private MonitorState GetMonitorForWindow(IntPtr hwnd)
        {
            IntPtr hmonitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONULL);
            if (hmonitor == IntPtr.Zero)
            {
                // offscreen: use the monitor under the cursor
                return GetCursorMonitor();
            }

            return GetMonitorByHandle(hmonitor) ?? GetCursorMonitor();
        }

        private MonitorState GetCursorMonitor()
        {
            if (User32.GetCursorPos(out User32.POINT point))
            {
                MonitorState monitor = GetMonitorByHandle(User32.MonitorFromPoint(point, User32.MONITOR_DEFAULTTONEAREST));
                if (monitor != null)
                {
                    return monitor;
                }
            }

            return PrimaryMonitor;
        }

        /// <summary>
        /// The empty screen the mouse moved onto, else the monitor of the foreground window, falling back to the
        /// cursor's monitor.
        /// </summary>
        private MonitorState GetFocusedMonitor()
        {
            if (_mouseFocusedMonitor != null)
            {
                return _mouseFocusedMonitor;
            }

            IntPtr foreground = User32.GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                if (_windows.TryGetValue(foreground, out ManagedWindow window))
                {
                    MonitorState owner = FindOwner(window.Hwnd);
                    if (owner != null)
                    {
                        return owner;
                    }
                }

                MonitorState monitor = GetMonitorByHandle(User32.MonitorFromWindow(foreground, User32.MONITOR_DEFAULTTONULL));
                if (monitor != null)
                {
                    return monitor;
                }
            }

            return GetCursorMonitor();
        }

        private MonitorState FindOwner(IntPtr hwnd)
        {
            long id = hwnd.ToInt64();
            return _monitors.FirstOrDefault(m => m.TagSet.Contains(id));
        }

        /// <summary>Monitors ordered left to right, then top to bottom.</summary>
        private List<MonitorState> OrderedMonitors()
        {
            return _monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
        }

        private MonitorState CycleMonitor(MonitorState from, bool next)
        {
            List<MonitorState> ordered = OrderedMonitors();
            if (ordered.Count < 2 || from == null)
            {
                return null;
            }

            int index = ordered.IndexOf(from);
            index = (index + (next ? 1 : -1) + ordered.Count) % ordered.Count;
            return ordered[index];
        }

        private MonitorState MonitorInDirection(MonitorState from, Direction direction)
        {
            (int cx, int cy) = from.Bounds.Center;
            MonitorState best = null;
            int bestDistance = int.MaxValue;

            foreach (MonitorState monitor in _monitors)
            {
                if (monitor == from)
                {
                    continue;
                }

                RectI b = monitor.Bounds;
                bool inDirection;
                switch (direction)
                {
                    case Direction.Left: inDirection = b.Right <= from.Bounds.X && b.Y < from.Bounds.Bottom && from.Bounds.Y < b.Bottom; break;
                    case Direction.Right: inDirection = b.X >= from.Bounds.Right && b.Y < from.Bounds.Bottom && from.Bounds.Y < b.Bottom; break;
                    case Direction.Up: inDirection = b.Bottom <= from.Bounds.Y && b.X < from.Bounds.Right && from.Bounds.X < b.Right; break;
                    default: inDirection = b.Y >= from.Bounds.Bottom && b.X < from.Bounds.Right && from.Bounds.X < b.Right; break;
                }

                if (!inDirection)
                {
                    continue;
                }

                (int mx, int my) = b.Center;
                int distance = Math.Abs(mx - cx) + Math.Abs(my - cy);
                if (distance < bestDistance)
                {
                    best = monitor;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Focus follows mouse for screens: moving onto a screen whose tag is empty, or onto its desktop, focuses that
        /// screen, so tag shortcuts act on it. The desktop never takes focus itself, so this is tracked here.
        /// </summary>
        private void CursorTimer_Tick(object sender, EventArgs e)
        {
            MergeParkedMonitors();

            if (!_settings.EnableFocusFollowsMouse
                || User32.GetAsyncKeyState(User32.VK_LBUTTON) < 0 || User32.GetAsyncKeyState(User32.VK_RBUTTON) < 0)
            {
                return;
            }

            MonitorState cursor = GetCursorMonitor();
            if (cursor == null || cursor == _lastCursorMonitor)
            {
                return;
            }

            _lastCursorMonitor = cursor;

            // only on entering a screen, so keyboard focus changes (Alt+Tab) while the mouse rests here still win
            _mouseFocusedMonitor = !cursor.TagSet.SelectedTag.VisibleWindows.Any() || TilingWindow.IsDesktopUnderCursor() ? cursor : null;
        }

        private void WindowManager_ScreensChanged(object sender, WindowManagerEventArgs e)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (e.Reason == WindowManagerEventReason.WorkArea && !e.DisplaysChanged)
            {
                foreach (MonitorState monitor in _monitors)
                {
                    monitor.RefreshWorkArea();
                    MarkDirty(monitor);
                }

                return;
            }

            ReconcileMonitors();
        }

        private void ReconcileMonitors()
        {
            _lastDisplayChangeTick = Environment.TickCount;
            List<AppBarScreen> screens = GetScreens();
            HashSet<MonitorState> present = new HashSet<MonitorState>();

            foreach (AppBarScreen screen in screens)
            {
                // match the physical monitor, not the device name, which can move to another monitor on reconnect
                string key = MonitorState.GetKey(screen.DeviceName);
                MonitorState monitor = _monitors.FirstOrDefault(m => !present.Contains(m) && m.Key == key);
                if (monitor != null)
                {
                    monitor.SetScreen(screen);
                }
                else if ((monitor = _parkedMonitors.FirstOrDefault(m => m.Key == key)) != null)
                {
                    _logger.LogDebug($"Tiling: monitor reconnected {screen.DeviceName}");
                    _parkedMonitors.Remove(monitor);
                    monitor.SetScreen(screen);
                    _monitors.Add(monitor);
                    Unpark(monitor);
                }
                else
                {
                    _logger.LogDebug($"Tiling: monitor added {screen.DeviceName}");
                    monitor = new MonitorState(screen, GetOrCreateViewModel(screen.DeviceName));
                    _monitors.Add(monitor);
                }

                present.Add(monitor);
            }

            foreach (MonitorState removed in _monitors.Where(m => !present.Contains(m)).ToList())
            {
                // monitors drop off while asleep or locked; keep their tags until they come back
                _logger.LogDebug($"Tiling: monitor removed {removed.DeviceName}");
                _monitors.Remove(removed);
                _parkedMonitors.Add(removed);

                if (_mouseFocusedMonitor == removed)
                {
                    _mouseFocusedMonitor = null;
                }

                if (_lastCursorMonitor == removed)
                {
                    _lastCursorMonitor = null;
                }
            }

            RebindViewModels();

            foreach (MonitorState monitor in _monitors)
            {
                MarkDirty(monitor);
            }
        }

        /// <summary>Points each monitor at the menu bar model for its current device name.</summary>
        private void RebindViewModels()
        {
            foreach (MonitorState monitor in _monitors)
            {
                monitor.ViewModel = GetOrCreateViewModel(monitor.DeviceName);
                monitor.ViewModel.IsActive = true;
            }

            foreach (ScreenTagsViewModel model in _viewModels.Values)
            {
                if (_monitors.All(m => m.ViewModel != model))
                {
                    ClearViewModel(model);
                }
            }
        }

        /// <summary>
        /// Puts a reconnected monitor's windows back: Windows moved them onto the remaining screens while it was gone.
        /// </summary>
        private void Unpark(MonitorState monitor)
        {
            foreach (long id in monitor.TagSet.Windows.ToList())
            {
                IntPtr hwnd = new IntPtr(id);
                if (!_windows.TryGetValue(hwnd, out ManagedWindow window) || _monitors.Any(m => m != monitor && m.TagSet.Contains(id)))
                {
                    monitor.TagSet.RemoveWindow(id);
                    continue;
                }

                window.HMonitor = monitor.HMonitor;
                SyncVisibility(monitor, id);

                if (window.IsFloating && window.FloatingRect.HasValue && monitor.TagSet.GetTagIndex(id) == monitor.TagSet.SelectedIndex)
                {
                    WindowArranger.MoveTo(hwnd, window.FloatingRect.Value);
                }
            }
        }

        /// <summary>
        /// A monitor that stays gone while the user is at the computer was unplugged: move its windows to the same
        /// tags on the primary monitor. Waiting for input keeps monitors that sleep for hours parked until they wake.
        /// </summary>
        private void MergeParkedMonitors()
        {
            if (_parkedMonitors.Count == 0 || Environment.TickCount - _lastDisplayChangeTick < ReconnectGraceMs)
            {
                return;
            }

            User32.LASTINPUTINFO input = User32.LASTINPUTINFO.Create();
            if (!User32.GetLastInputInfo(ref input) || unchecked((int)input.dwTime - _lastDisplayChangeTick) < ReconnectGraceMs)
            {
                return;
            }

            MonitorState primary = PrimaryMonitor;
            if (primary == null)
            {
                return;
            }

            foreach (MonitorState removed in _parkedMonitors)
            {
                _logger.LogDebug($"Tiling: monitor {removed.DeviceName} didn't come back; moving its windows to {primary.DeviceName}");

                foreach (Tag tag in removed.TagSet.Tags)
                {
                    foreach (long id in tag.AllWindows.ToList())
                    {
                        WindowState state = removed.TagSet.GetState(id) ?? WindowState.Tiled;
                        removed.TagSet.RemoveWindow(id);
                        if (!_windows.TryGetValue(new IntPtr(id), out ManagedWindow window) || primary.TagSet.Contains(id))
                        {
                            continue;
                        }

                        window.HMonitor = primary.HMonitor;
                        primary.TagSet.AddWindow(id, tag.Index, state);
                        SyncVisibility(primary, id);
                    }
                }
            }

            _parkedMonitors.Clear();
            MarkDirty(primary);
        }

        #endregion

        #region Window events

        private void Listener_WindowEvent(object sender, WinEventArgs e)
        {
            if (!IsEnabled)
            {
                return;
            }

            switch (e.Kind)
            {
                case WinEventKind.Shown:
                    OnShown(e.Hwnd);
                    break;
                case WinEventKind.Uncloaked:
                    OnUncloaked(e.Hwnd);
                    break;
                case WinEventKind.Hidden:
                    OnHidden(e.Hwnd);
                    break;
                case WinEventKind.Cloaked:
                    OnCloaked(e.Hwnd);
                    break;
                case WinEventKind.Destroyed:
                    OnDestroyed(e.Hwnd);
                    break;
                case WinEventKind.Foreground:
                    if (TilingWindow.IsDesktopWindow(e.Hwnd))
                    {
                        OnDesktopForeground();
                        break;
                    }

                    // handing focus back from the desktop keeps the screen the mouse chose
                    if (e.Hwnd != _restoringForeground)
                    {
                        _mouseFocusedMonitor = null;
                    }

                    _restoringForeground = IntPtr.Zero;
                    _lastForeground = e.Hwnd;
                    OnForeground(e.Hwnd);
                    break;
                case WinEventKind.MoveSizeStart:
                    _moveSizeHwnd = e.Hwnd;
                    break;
                case WinEventKind.MoveSizeEnd:
                    _moveSizeHwnd = IntPtr.Zero;
                    OnMoveSizeEnd(e.Hwnd);
                    break;
                case WinEventKind.MinimizeStart:
                    OnMinimizeStart(e.Hwnd);
                    break;
                case WinEventKind.MinimizeEnd:
                    OnMinimizeEnd(e.Hwnd);
                    break;
                case WinEventKind.NameChanged:
                    OnNameChanged(e.Hwnd);
                    break;
                case WinEventKind.LocationChanged:
                    OnLocationChanged(e.Hwnd);
                    break;
            }
        }

        private void OnShown(IntPtr hwnd, bool ignoreExpected = false)
        {
            if (!ignoreExpected && _arranger.ConsumeExpectedShow(hwnd))
            {
                return;
            }

            if (_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                if (window.IsHiddenByUs)
                {
                    // the app showed itself while its tag is hidden
                    window.IsHiddenByUs = false;
                    _journal.RemoveHidden(hwnd);
                    HandleActivationOnHiddenTag(window);
                }

                return;
            }

            TryManage(hwnd, focus: true, atCursor: true);
        }

        /// <summary>
        /// Windows cloaks windows on other virtual desktops and suspended UWP apps. Treat that like a minimize, so
        /// the window keeps its tag; a UWP frame whose app is gone is a closed app, though.
        /// </summary>
        private void OnCloaked(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window) || window.IsSystemCloaked)
            {
                return;
            }

            if (!User32.IsWindow(hwnd) || (window.IsFrameWindow && !UwpWindowHelper.HasCoreWindow(hwnd)))
            {
                Unmanage(hwnd);
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null)
            {
                Unmanage(hwnd);
                return;
            }

            window.IsSystemCloaked = true;
            window.StateBeforeCloak = monitor.TagSet.GetState(window.Id);
            _logger.LogDebug($"Tiling: {window} was cloaked; keeping it as minimized");

            if (window.StateBeforeCloak == WindowState.Minimized)
            {
                UpdateViewModel(monitor);
                return;
            }

            bool wasFocused = IsFocusedOnSelectedTag(monitor, window.Id);
            long? next = wasFocused ? PickNextFocus(monitor, window.Id) : null;

            monitor.TagSet.SetState(window.Id, WindowState.Minimized);
            MarkDirty(monitor);

            if (next.HasValue)
            {
                FocusId(monitor, next.Value);
            }
        }

        private void OnUncloaked(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window) || !window.IsSystemCloaked)
            {
                // new UWP frames appear by uncloaking
                OnShown(hwnd, ignoreExpected: true);
                return;
            }

            window.IsSystemCloaked = false;
            WindowState? previous = window.StateBeforeCloak;
            window.StateBeforeCloak = null;

            if (window.IsFrameWindow && !UwpWindowHelper.HasCoreWindow(hwnd))
            {
                // the frame is being reused or is left over; its app is gone
                Unmanage(hwnd);
                OnShown(hwnd, ignoreExpected: true);
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null)
            {
                return;
            }

            window.ResolveAppIdentity();
            _logger.LogDebug($"Tiling: {window} was uncloaked");

            if (previous.HasValue && previous.Value != WindowState.Minimized && !User32.IsIconic(hwnd))
            {
                monitor.TagSet.SetState(window.Id, previous.Value);
                if (monitor.TagSet.GetTagIndex(window.Id) != monitor.TagSet.SelectedIndex)
                {
                    HandleActivationOnHiddenTag(window);
                }
            }

            MarkDirty(monitor);
        }

        private void OnDestroyed(IntPtr hwnd)
        {
            if (_windows.ContainsKey(hwnd))
            {
                Unmanage(hwnd);
                return;
            }

            // a UWP app closing destroys its CoreWindow; its frame may stay behind, cloaked
            foreach (ManagedWindow frame in _windows.Values.Where(w => w.IsFrameWindow && w.IsSystemCloaked).ToList())
            {
                if (!UwpWindowHelper.HasCoreWindow(frame.Hwnd))
                {
                    Unmanage(frame.Hwnd);
                }
            }
        }

        private void OnHidden(IntPtr hwnd)
        {
            if (_arranger.ConsumeExpectedHide(hwnd))
            {
                return;
            }

            if (!_windows.TryGetValue(hwnd, out ManagedWindow window) || window.IsHiddenByUs || User32.IsWindowVisible(hwnd))
            {
                return;
            }

            Unmanage(hwnd);
        }

        /// <summary>
        /// The desktop never keeps focus: hovering or clicking it gives focus back to the last app window, and only
        /// picks the screen tag shortcuts act on.
        /// </summary>
        private void OnDesktopForeground()
        {
            MonitorState cursor = GetCursorMonitor();
            if (cursor != null)
            {
                _mouseFocusedMonitor = cursor;
            }

            IntPtr hwnd = _lastForeground;
            if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd) || !User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd))
            {
                return;
            }

            // after the activation settles, or Windows may finish activating the desktop over us
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!IsEnabled || _lastForeground != hwnd || !TilingWindow.IsDesktopWindow(User32.GetForegroundWindow()))
                {
                    return;
                }

                _restoringForeground = hwnd;
                WindowArranger.Focus(hwnd, raise: false);
            }));
        }

        private void OnForeground(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null)
            {
                return;
            }

            int? tagIndex = monitor.TagSet.GetTagIndex(window.Id);
            if (tagIndex.HasValue && tagIndex.Value != monitor.TagSet.SelectedIndex)
            {
                HandleActivationOnHiddenTag(window);
                return;
            }

            monitor.TagSet.SetFocused(window.Id);
            UpdateFocus(monitor);
        }

        /// <summary>
        /// A window on a hidden tag activated itself: jump to its tag (awesome's urgent jump), or mark it urgent.
        /// </summary>
        private void HandleActivationOnHiddenTag(ManagedWindow window)
        {
            MonitorState monitor = FindOwner(window.Hwnd);
            int? tagIndex = monitor?.TagSet.GetTagIndex(window.Id);
            if (monitor == null || !tagIndex.HasValue || tagIndex.Value == monitor.TagSet.SelectedIndex)
            {
                return;
            }

            if (_settings.TilingFollowActivation)
            {
                monitor.TagSet.SetFocused(window.Id);
                ViewTag(monitor, tagIndex.Value);
            }
            else
            {
                monitor.TagSet.Tags[tagIndex.Value].IsUrgent = true;
                if (User32.IsWindowVisible(window.Hwnd))
                {
                    _arranger.Hide(window);
                    window.IsHiddenByUs = true;
                }

                UpdateTags(monitor);
            }
        }

        private void OnMinimizeStart(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null || monitor.TagSet.GetState(window.Id) == WindowState.Minimized)
            {
                return;
            }

            bool wasFocused = IsFocusedOnSelectedTag(monitor, window.Id);
            long? next = wasFocused ? PickNextFocus(monitor, window.Id) : null;

            monitor.TagSet.SetState(window.Id, WindowState.Minimized);
            MarkDirty(monitor);

            if (next.HasValue)
            {
                FocusId(monitor, next.Value);
            }
        }

        private void OnMinimizeEnd(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null || monitor.TagSet.GetState(window.Id) != WindowState.Minimized)
            {
                return;
            }

            monitor.TagSet.SetState(window.Id, window.IsFloating ? WindowState.Floating : WindowState.Tiled);

            if (monitor.TagSet.GetTagIndex(window.Id) != monitor.TagSet.SelectedIndex)
            {
                HandleActivationOnHiddenTag(window);
            }

            MarkDirty(monitor);
        }

        private void OnMoveSizeEnd(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null)
            {
                return;
            }

            RectI newRect = WindowArranger.GetVisibleRect(hwnd);
            MonitorState cursorMonitor = GetCursorMonitor();

            if (cursorMonitor != null && cursorMonitor != monitor)
            {
                // dropped on another screen: move it to that screen's selected tag
                if (window.IsFloating)
                {
                    window.FloatingRect = newRect;
                }

                MoveWindowToMonitor(window, monitor, cursorMonitor, translateFloating: false);
                return;
            }

            if (window.IsFloating)
            {
                window.FloatingRect = newRect;
                return;
            }

            if (!monitor.TagSet.SelectedTag.Tree.Contains(window.Id) || !monitor.LastLayout.TryGetValue(window.Id, out RectI oldRect))
            {
                return;
            }

            const int tolerance = 4;
            bool resized = Math.Abs(newRect.W - oldRect.W) > tolerance || Math.Abs(newRect.H - oldRect.H) > tolerance;

            if (resized)
            {
                monitor.TagSet.SelectedTag.Tree.SetRatioFromRect(window.Id, newRect, monitor.WorkArea, _settings.TilingGap);
            }
            else if (User32.GetCursorPos(out User32.POINT point))
            {
                // dragged: swap with the window under the cursor
                foreach (KeyValuePair<long, RectI> pair in monitor.LastLayout)
                {
                    if (pair.Key != window.Id && pair.Value.Contains(point.X, point.Y))
                    {
                        monitor.TagSet.SelectedTag.Tree.Swap(window.Id, pair.Key);
                        break;
                    }
                }
            }

            MarkDirty(monitor);
        }

        private bool CanDrag(IntPtr hwnd)
        {
            // elevated windows ignore our moves; maximized ones would drag their maximized frame around
            return IsEnabled
                && _windows.TryGetValue(hwnd, out ManagedWindow window)
                && !window.IsInaccessible
                && !User32.IsIconic(hwnd)
                && !User32.IsZoomed(hwnd);
        }

        private void OnNameChanged(IntPtr hwnd)
        {
            if (_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                window.RefreshTitle();

                // a UWP frame gets its app after it appears; float rules may match it now
                if (window.ResolveAppIdentity() && !window.IsFloating && _filter.ShouldManage(hwnd) == FilterResult.Float)
                {
                    ToggleFloating(hwnd);
                }

                return;
            }

            // some windows get their title after they are shown
            if (User32.IsWindowVisible(hwnd))
            {
                TryManage(hwnd, focus: false, atCursor: true);
            }
        }

        private void OnLocationChanged(IntPtr hwnd)
        {
            // parked windows (HideMode.Offscreen) move because we moved them
            if (hwnd == _moveSizeHwnd || !_windows.TryGetValue(hwnd, out ManagedWindow window) || window.IsHiddenByUs || window.IsSystemCloaked)
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null)
            {
                return;
            }

            Tag tag = monitor.TagSet.GetTag(window.Id);
            if (tag == null || monitor.TagSet.GetState(window.Id) == WindowState.Minimized)
            {
                return;
            }

            if (UpdateAutoFullscreen(window, monitor, tag))
            {
                MarkDirty(monitor);
                return;
            }

            // an app maximized itself: undo it unless it's the tag's fullscreen window
            if (!window.IsFloating && tag.FullscreenWindow != window.Id && User32.IsZoomed(hwnd))
            {
                User32.ShowWindow(hwnd, User32.SW_RESTORE);
                MarkDirty(monitor);
            }
        }

        #endregion

        #region Managing windows

        /// <summary>
        /// Games and video players that cover the monitor without a caption are treated as fullscreen and left
        /// alone. UWP frames keep WS_CAPTION when their app goes fullscreen, so for them covering the monitor is
        /// enough. Returns true if the window's fullscreen state changed.
        /// </summary>
        private static bool UpdateAutoFullscreen(ManagedWindow window, MonitorState monitor, Tag tag)
        {
            bool coversMonitor = WindowArranger.GetVisibleRect(window.Hwnd) == monitor.Bounds
                && (window.IsFrameWindow || (User32.GetWindowLongPtr(window.Hwnd, User32.GWL_STYLE) & User32.WS_CAPTION) != User32.WS_CAPTION);

            if (coversMonitor && !window.IsAutoFullscreen && tag.FullscreenWindow == null)
            {
                window.IsAutoFullscreen = true;
                tag.FullscreenWindow = window.Id;
                return true;
            }

            if (!coversMonitor && window.IsAutoFullscreen)
            {
                window.IsAutoFullscreen = false;
                if (tag.FullscreenWindow == window.Id)
                {
                    tag.FullscreenWindow = null;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Starts managing <paramref name="hwnd"/>. New windows (<paramref name="atCursor"/>) go to the selected tag of the
        /// monitor under the cursor wherever Windows opened them; windows adopted at startup stay on their monitor.
        /// </summary>
        private bool TryManage(IntPtr hwnd, bool focus, bool atCursor)
        {
            if (_windows.ContainsKey(hwnd))
            {
                return false;
            }

            FilterResult result = _filter.ShouldManage(hwnd);
            if (result == FilterResult.Ignore)
            {
                return false;
            }

            MonitorState opened = GetMonitorForWindow(hwnd);
            MonitorState monitor = atCursor ? GetCursorMonitor() : opened;
            if (monitor == null)
            {
                return false;
            }

            // a minimized window's position is its icon's, not where it restores to
            if (opened != null && opened != monitor && !User32.IsIconic(hwnd))
            {
                MoveOntoMonitor(hwnd, opened, monitor);
            }

            ManagedWindow window = new ManagedWindow(hwnd)
            {
                IsFloating = result == FilterResult.Float,
                HMonitor = monitor.HMonitor
            };
            window.HideMode = window.IsFrameWindow && _settings.TilingParkUwpWindows ? HideMode.Offscreen : HideMode.Hide;

            if (window.IsFloating)
            {
                window.FloatingRect = WindowArranger.GetVisibleRect(hwnd);
            }

            WindowState state = User32.IsIconic(hwnd) ? WindowState.Minimized : window.IsFloating ? WindowState.Floating : WindowState.Tiled;

            _windows[hwnd] = window;
            monitor.TagSet.AddWindow(window.Id, monitor.TagSet.SelectedIndex, state);
            _logger.LogDebug($"Tiling: managing {window} as {state} on {monitor.DeviceName}");

            if (state != WindowState.Minimized)
            {
                UpdateAutoFullscreen(window, monitor, monitor.TagSet.SelectedTag);
            }

            if (focus && state != WindowState.Minimized)
            {
                monitor.TagSet.SetFocused(window.Id);
                if (User32.GetForegroundWindow() != hwnd)
                {
                    WindowArranger.Focus(hwnd, raise: true);
                }
            }

            MarkDirty(monitor);
            return true;
        }

        /// <summary>
        /// Moves a window Windows opened on <paramref name="from"/> onto <paramref name="to"/>, keeping its offset in the
        /// work area but keeping it inside <paramref name="to"/>. Tiled windows are laid out again anyway, but floating
        /// ones keep wherever they are when managed. Windows covering their monitor are moved to cover the new one.
        /// </summary>
        private static void MoveOntoMonitor(IntPtr hwnd, MonitorState from, MonitorState to)
        {
            RectI rect = WindowArranger.GetVisibleRect(hwnd);
            RectI moved;

            if (rect == from.Bounds)
            {
                moved = to.Bounds;
            }
            else
            {
                RectI area = to.WorkArea;
                int w = Math.Min(rect.W, area.W);
                int h = Math.Min(rect.H, area.H);
                int x = Math.Max(area.X, Math.Min(rect.X - from.WorkArea.X + area.X, area.Right - w));
                int y = Math.Max(area.Y, Math.Min(rect.Y - from.WorkArea.Y + area.Y, area.Bottom - h));
                moved = new RectI(x, y, w, h);
            }

            WindowArranger.MoveTo(hwnd, moved);
        }

        private void Unmanage(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            long? next = null;
            if (monitor != null && IsFocusedOnSelectedTag(monitor, window.Id))
            {
                next = PickNextFocus(monitor, window.Id);
            }

            monitor?.TagSet.RemoveWindow(window.Id);
            foreach (MonitorState parked in _parkedMonitors)
            {
                parked.TagSet.RemoveWindow(window.Id);
            }

            _windows.Remove(hwnd);
            _arranger.Forget(hwnd);
            WindowIconHelper.Forget(hwnd);
            _logger.LogDebug($"Tiling: no longer managing {window}");

            if (monitor != null)
            {
                MarkDirty(monitor);
                if (next.HasValue)
                {
                    FocusId(monitor, next.Value);
                }
            }
        }

        /// <summary>Hides or shows a window to match whether its tag is selected.</summary>
        private void SyncVisibility(MonitorState monitor, long id)
        {
            IntPtr hwnd = new IntPtr(id);
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            bool visible = monitor.TagSet.GetTagIndex(id) == monitor.TagSet.SelectedIndex
                || monitor.TagSet.GetState(id) == WindowState.Minimized;

            if (visible && window.IsHiddenByUs)
            {
                window.IsHiddenByUs = false;
                _arranger.Show(window);
            }
            else if (!visible && !window.IsHiddenByUs)
            {
                window.IsHiddenByUs = true;
                _arranger.Hide(window);
            }
        }

        private void MoveWindowToMonitor(ManagedWindow window, MonitorState from, MonitorState to, bool translateFloating)
        {
            WindowState state = from.TagSet.GetState(window.Id) ?? WindowState.Tiled;
            from.TagSet.RemoveWindow(window.Id);
            to.TagSet.AddWindow(window.Id, to.TagSet.SelectedIndex, state);
            to.TagSet.SetFocused(window.Id);
            window.HMonitor = to.HMonitor;

            if (translateFloating && window.IsFloating && window.FloatingRect.HasValue)
            {
                RectI r = window.FloatingRect.Value;
                RectI moved = new RectI(r.X - from.WorkArea.X + to.WorkArea.X, r.Y - from.WorkArea.Y + to.WorkArea.Y, r.W, r.H);
                window.FloatingRect = moved;
                WindowArranger.MoveTo(window.Hwnd, moved);
            }

            SyncVisibility(to, window.Id);
            MarkDirty(from);
            MarkDirty(to);
            WindowArranger.Focus(window.Hwnd);
        }

        #endregion

        #region Focus helpers

        private ManagedWindow GetFocusedWindow()
        {
            // while an empty screen has focus, the foreground window on another screen isn't the focused window
            IntPtr foreground = User32.GetForegroundWindow();
            ManagedWindow window;
            if (_mouseFocusedMonitor == null && _windows.TryGetValue(foreground, out window))
            {
                return window;
            }

            MonitorState monitor = GetFocusedMonitor();
            long? focused = monitor?.TagSet.SelectedTag.FocusedWindow;
            if (focused.HasValue && _windows.TryGetValue(new IntPtr(focused.Value), out window))
            {
                return window;
            }

            return null;
        }

        private bool IsFocusedOnSelectedTag(MonitorState monitor, long id)
        {
            return monitor.TagSet.SelectedTag.FocusedWindow == id || User32.GetForegroundWindow().ToInt64() == id;
        }

        /// <summary>The window to focus after <paramref name="leaving"/> goes away from the selected tag.</summary>
        private static long? PickNextFocus(MonitorState monitor, long leaving)
        {
            Tag tag = monitor.TagSet.SelectedTag;
            List<long> visible = tag.VisibleWindows.ToList();
            int index = visible.IndexOf(leaving);
            visible.Remove(leaving);
            if (visible.Count == 0)
            {
                return null;
            }

            return visible[Math.Max(0, Math.Min(index, visible.Count - 1))];
        }

        private void FocusId(MonitorState monitor, long id)
        {
            monitor.TagSet.SetFocused(id);
            WindowArranger.Focus(new IntPtr(id));
            UpdateFocus(monitor);
        }

        /// <summary>Focuses the tag's last-focused window, or its first window.</summary>
        private void FocusTag(MonitorState monitor)
        {
            Tag tag = monitor.TagSet.SelectedTag;
            long? target = tag.FocusedWindow;
            if (!target.HasValue || !tag.VisibleWindows.Contains(target.Value))
            {
                target = tag.VisibleWindows.Cast<long?>().FirstOrDefault();
            }

            if (target.HasValue)
            {
                FocusId(monitor, target.Value);
            }
            else
            {
                UpdateFocus(monitor);
            }
        }

        private static void WarpCursor(MonitorState monitor)
        {
            (int x, int y) = monitor.WorkArea.Center;
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
        }

        #endregion

        #region Layout

        private void MarkDirty(MonitorState monitor)
        {
            if (monitor == null)
            {
                return;
            }

            _dirty.Add(monitor);
            if (!_layoutQueued)
            {
                _layoutQueued = true;
                (_dispatcher ?? Dispatcher.CurrentDispatcher).BeginInvoke(DispatcherPriority.Background, new Action(ProcessLayouts));
            }
        }

        private void ProcessLayouts()
        {
            _layoutQueued = false;
            if (!IsEnabled)
            {
                _dirty.Clear();
                return;
            }

            List<MonitorState> dirty = _dirty.ToList();
            _dirty.Clear();
            foreach (MonitorState monitor in dirty)
            {
                if (_monitors.Contains(monitor))
                {
                    Layout(monitor);
                }
            }
        }

        private void Layout(MonitorState monitor)
        {
            monitor.RefreshWorkArea();

            Tag tag = monitor.TagSet.SelectedTag;
            Dictionary<long, RectI> layout = tag.Tree.Layout(monitor.WorkArea, _settings.TilingGap);
            monitor.LastLayout = layout;

            List<(IntPtr, RectI)> placements = new List<(IntPtr, RectI)>();
            foreach (KeyValuePair<long, RectI> pair in layout)
            {
                if (pair.Key == tag.FullscreenWindow)
                {
                    continue;
                }

                placements.Add((new IntPtr(pair.Key), pair.Value));
            }

            ManagedWindow fullscreen = null;
            if (tag.FullscreenWindow.HasValue && _windows.TryGetValue(new IntPtr(tag.FullscreenWindow.Value), out fullscreen) && !fullscreen.IsAutoFullscreen)
            {
                placements.Add((fullscreen.Hwnd, monitor.Bounds));
            }

            List<IntPtr> denied = _arranger.Apply(placements);
            foreach (IntPtr hwnd in denied)
            {
                if (_windows.TryGetValue(hwnd, out ManagedWindow window) && !window.IsInaccessible)
                {
                    _logger.LogInformation($"Tiling: {window} can't be moved (probably elevated); floating it");
                    window.IsInaccessible = true;
                    window.IsFloating = true;
                    window.FloatingRect = WindowArranger.GetVisibleRect(hwnd);
                    monitor.TagSet.SetState(window.Id, WindowState.Floating);
                    MarkDirty(monitor);
                }
            }

            if (fullscreen != null && !fullscreen.IsAutoFullscreen)
            {
                User32.SetWindowPos(fullscreen.Hwnd, User32.HWND_TOP, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
            }

            UpdateViewModel(monitor);
        }

        #endregion

        #region View models

        public ScreenTagsViewModel GetScreenModel(AppBarScreen screen)
        {
            if (screen == null)
            {
                return null;
            }

            ScreenTagsViewModel model = GetOrCreateViewModel(screen.DeviceName);
            MonitorState monitor = _monitors.FirstOrDefault(m => m.DeviceName == screen.DeviceName);
            if (monitor != null)
            {
                UpdateViewModel(monitor);
            }

            return model;
        }

        private ScreenTagsViewModel GetOrCreateViewModel(string deviceName)
        {
            if (_viewModels.TryGetValue(deviceName, out ScreenTagsViewModel model))
            {
                return model;
            }

            model = new ScreenTagsViewModel(deviceName, next =>
            {
                MonitorState monitor = _monitors.FirstOrDefault(m => m.DeviceName == deviceName);
                if (monitor != null)
                {
                    int count = monitor.TagSet.Tags.Count;
                    ViewTag(monitor, (monitor.TagSet.SelectedIndex + (next ? 1 : -1) + count) % count);
                }
            });

            for (int i = 0; i < TagSet.DefaultTagCount; i++)
            {
                model.Tags.Add(new TagViewModel(i, TagSet.DefaultLabel(i),
                    index => ViewTag(_monitors.FirstOrDefault(m => m.DeviceName == deviceName), index),
                    index => MoveFocusedToTag(_monitors.FirstOrDefault(m => m.DeviceName == deviceName), index)));
            }

            _viewModels[deviceName] = model;
            return model;
        }

        private void ClearViewModel(ScreenTagsViewModel model)
        {
            model.IsActive = false;
            model.FocusedWindow = null;
            foreach (ManagedWindowViewModel item in model.Windows)
            {
                item.Detach();
            }

            model.Windows.Clear();
            foreach (TagViewModel tag in model.Tags)
            {
                tag.IsSelected = false;
                tag.IsOccupied = false;
                tag.IsUrgent = false;
            }
        }

        private void UpdateViewModel(MonitorState monitor)
        {
            UpdateTags(monitor);

            ScreenTagsViewModel model = monitor.ViewModel;
            // windows Windows cloaked (other virtual desktops, suspended apps) stay out of the list
            List<long> visible = monitor.TagSet.SelectedTag.VisibleWindows
                .Where(id => _windows.TryGetValue(new IntPtr(id), out ManagedWindow managed) && !managed.IsSystemCloaked).ToList();

            // keep existing items so icons and bindings survive
            for (int i = model.Windows.Count - 1; i >= 0; i--)
            {
                if (!visible.Contains(model.Windows[i].Window.Id))
                {
                    model.Windows[i].Detach();
                    model.Windows.RemoveAt(i);
                }
            }

            for (int i = 0; i < visible.Count; i++)
            {
                ManagedWindow window = _windows[new IntPtr(visible[i])];
                int existing = -1;
                for (int j = 0; j < model.Windows.Count; j++)
                {
                    if (model.Windows[j].Window == window)
                    {
                        existing = j;
                        break;
                    }
                }

                if (existing < 0)
                {
                    model.Windows.Insert(i, new ManagedWindowViewModel(window, this));
                }
                else if (existing != i)
                {
                    model.Windows.Move(existing, i);
                }

                model.Windows[i].IsFloating = window.IsFloating;
            }

            UpdateFocus(monitor);
        }

        private void UpdateTags(MonitorState monitor)
        {
            ScreenTagsViewModel model = monitor.ViewModel;
            for (int i = 0; i < model.Tags.Count && i < monitor.TagSet.Tags.Count; i++)
            {
                Tag tag = monitor.TagSet.Tags[i];
                model.Tags[i].Label = tag.Label;
                model.Tags[i].IsSelected = i == monitor.TagSet.SelectedIndex;
                model.Tags[i].IsOccupied = !tag.IsEmpty;
                model.Tags[i].IsUrgent = tag.IsUrgent;
            }
        }

        private void UpdateFocus(MonitorState monitor)
        {
            long? focused = monitor.TagSet.SelectedTag.FocusedWindow;
            monitor.ViewModel.FocusedWindow = focused.HasValue ? monitor.ViewModel.Windows.FirstOrDefault(w => w.Window.Id == focused.Value) : null;
        }

        #endregion

        #region Public API

        public void ViewTag(AppBarScreen screen, int index)
        {
            ViewTag(GetMonitor(screen), index);
        }

        private void ViewTag(MonitorState monitor, int index)
        {
            if (!IsEnabled || monitor == null || index < 0 || index >= monitor.TagSet.Tags.Count || index == monitor.TagSet.SelectedIndex)
            {
                return;
            }

            TagViewDelta delta = monitor.TagSet.View(index);

            // position the new tag's windows while they are still hidden, then swap
            Layout(monitor);

            foreach (long id in delta.ToShow)
            {
                if (_windows.TryGetValue(new IntPtr(id), out ManagedWindow window) && window.IsHiddenByUs)
                {
                    window.IsHiddenByUs = false;
                    _arranger.Show(window);
                }
            }

            foreach (long id in delta.ToHide)
            {
                if (_windows.TryGetValue(new IntPtr(id), out ManagedWindow window) && !window.IsHiddenByUs)
                {
                    window.IsHiddenByUs = true;
                    _arranger.Hide(window);
                }
            }

            FocusTag(monitor);
            UpdateViewModel(monitor);
        }

        public void ViewPreviousTag(AppBarScreen screen)
        {
            MonitorState monitor = GetMonitor(screen);
            if (monitor != null)
            {
                ViewTag(monitor, monitor.TagSet.PreviousIndex);
            }
        }

        public void MoveFocusedToTag(int index)
        {
            ManagedWindow window = GetFocusedWindow();
            if (window != null)
            {
                MoveWindowToTag(window, index);
            }
        }

        private void MoveFocusedToTag(MonitorState monitor, int index)
        {
            // from a tag bar right-click: the focused window of that screen
            long? focused = monitor?.TagSet.SelectedTag.FocusedWindow;
            if (focused.HasValue && _windows.TryGetValue(new IntPtr(focused.Value), out ManagedWindow window))
            {
                MoveWindowToTag(window, index);
            }
        }

        public void MoveWindowToTag(IntPtr hwnd, int index)
        {
            if (_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                MoveWindowToTag(window, index);
            }
        }

        private void MoveWindowToTag(ManagedWindow window, int index)
        {
            MonitorState monitor = FindOwner(window.Hwnd);
            if (!IsEnabled || monitor == null || index < 0 || index >= monitor.TagSet.Tags.Count)
            {
                return;
            }

            bool wasFocused = IsFocusedOnSelectedTag(monitor, window.Id);
            long? next = wasFocused ? PickNextFocus(monitor, window.Id) : null;

            if (!monitor.TagSet.MoveWindow(window.Id, index))
            {
                return;
            }

            SyncVisibility(monitor, window.Id);
            MarkDirty(monitor);

            // focus stays on the current tag
            if (next.HasValue)
            {
                FocusId(monitor, next.Value);
            }

            UpdateViewModel(monitor);
        }

        public void MoveFocusedToScreen(bool next)
        {
            ManagedWindow window = GetFocusedWindow();
            MonitorState from = window != null ? FindOwner(window.Hwnd) : null;
            MonitorState to = CycleMonitor(from, next);
            if (!IsEnabled || window == null || to == null)
            {
                return;
            }

            MoveWindowToMonitor(window, from, to, translateFloating: true);
        }

        public void FocusDirection(Direction direction)
        {
            if (!IsEnabled)
            {
                return;
            }

            ManagedWindow window = GetFocusedWindow();
            MonitorState monitor = window != null ? FindOwner(window.Hwnd) : GetFocusedMonitor();
            if (monitor == null)
            {
                return;
            }

            if (window != null && monitor.TagSet.SelectedTag.Tree.Contains(window.Id))
            {
                long? neighbor = BspTree.FindNeighbor(window.Id, direction, monitor.LastLayout);
                if (neighbor.HasValue)
                {
                    FocusId(monitor, neighbor.Value);
                    return;
                }
            }
            else if (window != null ? window.IsFloating : monitor.TagSet.SelectedTag.VisibleWindows.Any())
            {
                FocusTag(monitor);
                return;
            }

            // nothing in that direction on this screen: try the next screen over
            MonitorState other = MonitorInDirection(monitor, direction);
            if (other != null)
            {
                FocusTag(other);
                WarpCursor(other);
            }
        }

        public void FocusNext()
        {
            CycleFocus(1);
        }

        public void FocusPrev()
        {
            CycleFocus(-1);
        }

        private void CycleFocus(int step)
        {
            if (!IsEnabled)
            {
                return;
            }

            ManagedWindow window = GetFocusedWindow();
            MonitorState monitor = window != null ? FindOwner(window.Hwnd) : GetFocusedMonitor();
            if (monitor == null)
            {
                return;
            }

            List<long> visible = monitor.TagSet.SelectedTag.VisibleWindows.ToList();
            if (visible.Count == 0)
            {
                return;
            }

            int index = window != null ? visible.IndexOf(window.Id) : -1;
            index = index < 0 ? 0 : (index + step + visible.Count) % visible.Count;
            FocusId(monitor, visible[index]);
        }

        public void FocusScreen(bool next)
        {
            MonitorState to = CycleMonitor(GetFocusedMonitor(), next);
            if (!IsEnabled || to == null)
            {
                return;
            }

            FocusTag(to);
            WarpCursor(to);
        }

        public void SwapDirection(Direction direction)
        {
            WithFocusedTiled((monitor, window, tree) =>
            {
                long? neighbor = BspTree.FindNeighbor(window.Id, direction, monitor.LastLayout);
                return neighbor.HasValue && tree.Swap(window.Id, neighbor.Value);
            });
        }

        public void MoveDirection(Direction direction)
        {
            WithFocusedTiled((monitor, window, tree) => tree.Move(window.Id, direction, monitor.WorkArea, _settings.TilingGap));
        }

        public void Resize(Direction direction)
        {
            WithFocusedTiled((monitor, window, tree) => tree.Resize(window.Id, direction, ResizeStep));
        }

        public void ToggleOrientation()
        {
            WithFocusedTiled((monitor, window, tree) => tree.ToggleOrientation(window.Id));
        }

        private void WithFocusedTiled(Func<MonitorState, ManagedWindow, BspTree, bool> action)
        {
            if (!IsEnabled)
            {
                return;
            }

            ManagedWindow window = GetFocusedWindow();
            MonitorState monitor = window != null ? FindOwner(window.Hwnd) : null;
            if (monitor == null)
            {
                return;
            }

            BspTree tree = monitor.TagSet.SelectedTag.Tree;
            if (!tree.Contains(window.Id))
            {
                return;
            }

            if (action(monitor, window, tree))
            {
                MarkDirty(monitor);
            }
        }

        public void Equalize()
        {
            MonitorState monitor = GetFocusedMonitor();
            if (!IsEnabled || monitor == null)
            {
                return;
            }

            monitor.TagSet.SelectedTag.Tree.Equalize();
            MarkDirty(monitor);
        }

        public void ToggleFloating()
        {
            ManagedWindow window = GetFocusedWindow();
            if (window != null)
            {
                ToggleFloating(window.Hwnd);
            }
        }

        public void ToggleFloating(IntPtr hwnd)
        {
            if (!IsEnabled || !_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null || monitor.TagSet.GetState(window.Id) == WindowState.Minimized)
            {
                return;
            }

            if (window.IsFloating)
            {
                if (window.IsInaccessible)
                {
                    return;
                }

                window.FloatingRect = WindowArranger.GetVisibleRect(hwnd);
                window.IsFloating = false;
                monitor.TagSet.SetState(window.Id, WindowState.Tiled);
            }
            else
            {
                window.IsFloating = true;
                monitor.TagSet.SetState(window.Id, WindowState.Floating);

                RectI rect = window.FloatingRect ?? DefaultFloatingRect(monitor);
                if (!rect.Intersects(monitor.WorkArea))
                {
                    rect = DefaultFloatingRect(monitor);
                }

                WindowArranger.MoveTo(hwnd, rect);
                User32.SetWindowPos(hwnd, User32.HWND_TOP, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
            }

            MarkDirty(monitor);
        }

        private static RectI DefaultFloatingRect(MonitorState monitor)
        {
            RectI area = monitor.WorkArea;
            int w = area.W * 3 / 5;
            int h = area.H * 3 / 5;
            return new RectI(area.X + (area.W - w) / 2, area.Y + (area.H - h) / 2, w, h);
        }

        public void ToggleFullscreen()
        {
            ManagedWindow window = GetFocusedWindow();
            MonitorState monitor = window != null ? FindOwner(window.Hwnd) : null;
            if (!IsEnabled || monitor == null)
            {
                return;
            }

            Tag tag = monitor.TagSet.SelectedTag;
            if (tag.FullscreenWindow == window.Id)
            {
                tag.FullscreenWindow = null;
                if (window.IsFloating && window.FloatingRect.HasValue)
                {
                    WindowArranger.MoveTo(window.Hwnd, window.FloatingRect.Value);
                }
            }
            else
            {
                if (window.IsFloating)
                {
                    window.FloatingRect = WindowArranger.GetVisibleRect(window.Hwnd);
                }

                window.IsAutoFullscreen = false;
                tag.FullscreenWindow = window.Id;
            }

            MarkDirty(monitor);
        }

        public void CloseFocused()
        {
            ManagedWindow window = GetFocusedWindow();
            if (window != null)
            {
                CloseWindow(window.Hwnd);
            }
        }

        public void CloseWindow(IntPtr hwnd)
        {
            User32.PostMessage(hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public void FocusWindow(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out ManagedWindow window))
            {
                WindowArranger.Focus(hwnd);
                return;
            }

            MonitorState monitor = FindOwner(hwnd);
            if (monitor == null)
            {
                return;
            }

            int? tagIndex = monitor.TagSet.GetTagIndex(window.Id);
            monitor.TagSet.SetFocused(window.Id);
            if (tagIndex.HasValue && tagIndex.Value != monitor.TagSet.SelectedIndex)
            {
                ViewTag(monitor, tagIndex.Value);
                return;
            }

            FocusId(monitor, window.Id);
        }

        public IDisposable SuspendFocusFollowsMouse()
        {
            _focusFollowsMouse.Suspend();
            return new FocusFollowsMouseSuspension(_focusFollowsMouse);
        }

        private sealed class FocusFollowsMouseSuspension : IDisposable
        {
            private FocusFollowsMouse _focusFollowsMouse;

            public FocusFollowsMouseSuspension(FocusFollowsMouse focusFollowsMouse)
            {
                _focusFollowsMouse = focusFollowsMouse;
            }

            public void Dispose()
            {
                _focusFollowsMouse?.Resume();
                _focusFollowsMouse = null;
            }
        }

        #endregion

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.EnableTiling):
                case nameof(Settings.EnableTilingOutsideShell):
                    UpdateEnabled();
                    break;
                case nameof(Settings.TilingGap):
                    foreach (MonitorState monitor in _monitors)
                    {
                        MarkDirty(monitor);
                    }
                    break;
                case nameof(Settings.EnableFocusFollowsMouse):
                case nameof(Settings.FocusFollowsMouseRaise):
                case nameof(Settings.FocusFollowsMouseDelayMs):
                case nameof(Settings.FocusFollowsMouseMode):
                    if (IsEnabled)
                    {
                        _focusFollowsMouse.Enable();
                    }
                    break;
                case nameof(Settings.RemapWinL):
                    if (IsEnabled)
                    {
                        if (_settings.RemapWinL)
                        {
                            _lockManager.Enable();
                        }
                        else
                        {
                            _lockManager.Disable();
                        }
                    }
                    break;
            }
        }
    }
}

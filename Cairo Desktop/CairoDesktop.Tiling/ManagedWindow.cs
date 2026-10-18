using CairoDesktop.Interop;
using CairoDesktop.Tiling.Core;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CairoDesktop.Tiling
{
    public enum HideMode
    {
        /// <summary>ShowWindow(SW_HIDE)</summary>
        Hide,

        /// <summary>
        /// Moved far off screen instead, for UWP frames that come back blank or lost after SW_HIDE
        /// (Settings.TilingParkUwpWindows).
        /// </summary>
        Offscreen
    }

    /// <summary>
    /// A top-level window tracked by the tiling manager.
    /// </summary>
    public class ManagedWindow : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; }

        public long Id => Hwnd.ToInt64();

        public uint Pid { get; }

        public string ClassName { get; }

        /// <summary>True for UWP frames (ApplicationFrameWindow), whose app lives in another process.</summary>
        public bool IsFrameWindow => UwpWindowHelper.IsFrame(ClassName);

        private uint _appPid;

        /// <summary>
        /// The process of the app shown in the window: for UWP frames the hosted app (once attached), else <see cref="Pid"/>.
        /// </summary>
        public uint AppPid => _appPid != 0 ? _appPid : Pid;

        private string _appUserModelId;

        /// <summary>The hosted app's AppUserModelID, for UWP frames.</summary>
        public string AppUserModelId => _appUserModelId;

        private string _exeName;

        /// <summary>
        /// The app's exe, e.g. Calculator.exe for a UWP frame rather than ApplicationFrameHost.exe.
        /// </summary>
        public string ExeName
        {
            get
            {
                if (_exeName == null)
                {
                    _exeName = GetExeName(AppPid);
                }

                return _exeName;
            }
        }

        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsFloating { get; set; }

        public bool IsHiddenByUs { get; set; }

        /// <summary>How the window is hidden on tags that aren't shown.</summary>
        public HideMode HideMode { get; set; }

        /// <summary>
        /// Set while Windows cloaks the window (another virtual desktop, a suspended UWP app). It is treated like a
        /// minimized window and keeps its tag. The tiler never cloaks windows itself.
        /// </summary>
        public bool IsSystemCloaked { get; set; }

        /// <summary>The window's state before Windows cloaked it, put back when it is uncloaked.</summary>
        public WindowState? StateBeforeCloak { get; set; }

        /// <summary>
        /// Set when the window covers its monitor without a caption (games, video players). It is left alone.
        /// </summary>
        public bool IsAutoFullscreen { get; set; }

        /// <summary>
        /// Set when moving the window failed with access denied (an elevated window under UIPI).
        /// </summary>
        public bool IsInaccessible { get; set; }

        public IntPtr HMonitor { get; set; }

        /// <summary>
        /// Where the window was when it last floated, so it returns there.
        /// </summary>
        public RectI? FloatingRect { get; set; }

        public ManagedWindow(IntPtr hwnd)
        {
            Hwnd = hwnd;
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            Pid = pid;
            ClassName = User32.GetClassName(hwnd);
            _title = User32.GetWindowText(hwnd);
            ResolveAppIdentity();
        }

        public void RefreshTitle()
        {
            Title = User32.GetWindowText(Hwnd);
        }

        /// <summary>
        /// For UWP frames, finds the hosted app. The CoreWindow child is attached after the frame appears, so this
        /// runs again on title changes. Returns true when the app changed.
        /// </summary>
        public bool ResolveAppIdentity()
        {
            if (!IsFrameWindow)
            {
                return false;
            }

            uint appPid = UwpWindowHelper.GetAppPid(Hwnd);
            if (appPid == 0 || appPid == _appPid)
            {
                return false;
            }

            _appPid = appPid;
            _exeName = null;
            _appUserModelId = UwpWindowHelper.GetAppUserModelId(Hwnd, appPid);
            OnPropertyChanged(nameof(ExeName));
            OnPropertyChanged(nameof(AppUserModelId));
            return true;
        }

        internal static string GetExeName(uint pid)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                {
                    return process.ProcessName + ".exe";
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => $"0x{Hwnd.ToInt64():X} [{ClassName}] \"{Title}\"";
    }
}

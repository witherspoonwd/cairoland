using CairoDesktop.Interop;
using CairoDesktop.Tiling.Core;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// A top-level window tracked by the tiling manager.
    /// </summary>
    public class ManagedWindow : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; }

        public long Id => Hwnd.ToInt64();

        public uint Pid { get; }

        public string ClassName { get; }

        private string _exeName;
        public string ExeName
        {
            get
            {
                if (_exeName == null)
                {
                    _exeName = GetExeName(Pid);
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
        }

        public void RefreshTitle()
        {
            Title = User32.GetWindowText(Hwnd);
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

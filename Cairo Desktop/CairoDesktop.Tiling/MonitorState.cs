using CairoDesktop.Interop;
using CairoDesktop.Tiling.Core;
using CairoDesktop.Tiling.ViewModels;
using ManagedShell.AppBar;
using System;
using System.Collections.Generic;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Tiling state for one screen.
    /// </summary>
    public class MonitorState
    {
        public AppBarScreen Screen { get; private set; }

        public string DeviceName => Screen.DeviceName;

        public IntPtr HMonitor => Screen.HMonitor;

        public TagSet TagSet { get; } = new TagSet();

        public ScreenTagsViewModel ViewModel { get; }

        public RectI Bounds { get; private set; }

        /// <summary>Monitor area minus app bars (the menu bar), from GetMonitorInfo.</summary>
        public RectI WorkArea { get; private set; }

        /// <summary>The rects from the last layout of the selected tag.</summary>
        public Dictionary<long, RectI> LastLayout { get; set; } = new Dictionary<long, RectI>();

        public MonitorState(AppBarScreen screen, ScreenTagsViewModel viewModel)
        {
            ViewModel = viewModel;
            SetScreen(screen);
        }

        public void SetScreen(AppBarScreen screen)
        {
            Screen = screen;
            RefreshWorkArea();
        }

        /// <summary>
        /// Re-reads the work area. Returns true if it changed.
        /// </summary>
        public bool RefreshWorkArea()
        {
            RectI oldWorkArea = WorkArea;

            User32.MONITORINFO info = User32.MONITORINFO.Create();
            if (Screen.HMonitor != IntPtr.Zero && User32.GetMonitorInfo(Screen.HMonitor, ref info))
            {
                Bounds = RectI.FromEdges(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
                WorkArea = RectI.FromEdges(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            }
            else
            {
                Bounds = new RectI(Screen.Bounds.X, Screen.Bounds.Y, Screen.Bounds.Width, Screen.Bounds.Height);
                WorkArea = new RectI(Screen.WorkingArea.X, Screen.WorkingArea.Y, Screen.WorkingArea.Width, Screen.WorkingArea.Height);
            }

            TagSet.Area = WorkArea;
            return oldWorkArea != WorkArea;
        }

        public override string ToString() => $"{DeviceName} {Bounds}";
    }
}

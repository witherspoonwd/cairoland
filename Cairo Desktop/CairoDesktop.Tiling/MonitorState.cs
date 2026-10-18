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

        /// <summary>
        /// Identifies the physical monitor across reconnects, which can hand out device names (\\.\DISPLAYn) in a
        /// different order.
        /// </summary>
        public string Key { get; private set; }

        public TagSet TagSet { get; } = new TagSet();

        /// <summary>The menu bar model for <see cref="DeviceName"/>, rebound when the device name changes.</summary>
        public ScreenTagsViewModel ViewModel { get; set; }

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
            Key = GetKey(screen.DeviceName);
            RefreshWorkArea();
        }

        /// <summary>
        /// The device interface path of the monitor attached to <paramref name="deviceName"/>, which comes from its
        /// hardware ID and connector; the device name itself when that can't be read.
        /// </summary>
        public static string GetKey(string deviceName)
        {
            User32.DISPLAY_DEVICE device = User32.DISPLAY_DEVICE.Create();
            for (uint i = 0; User32.EnumDisplayDevices(deviceName, i, ref device, User32.EDD_GET_DEVICE_INTERFACE_NAME); i++)
            {
                if ((device.StateFlags & User32.DISPLAY_DEVICE_ACTIVE) != 0 && !string.IsNullOrEmpty(device.DeviceID))
                {
                    return device.DeviceID;
                }

                device = User32.DISPLAY_DEVICE.Create();
            }

            return deviceName;
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

using CairoDesktop.Interop;
using System;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// UWP apps draw into a Windows.UI.Core.CoreWindow owned by the app's process, hosted as a child of an
    /// ApplicationFrameWindow owned by ApplicationFrameHost.exe. The frame is what gets tiled; the child tells
    /// which app it is. The child is attached after the frame appears, and frames outlive their apps (cloaked).
    /// </summary>
    public static class UwpWindowHelper
    {
        public const string FrameClass = "ApplicationFrameWindow";
        public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

        public static bool IsFrame(string className) => className == FrameClass;

        public static IntPtr FindCoreWindow(IntPtr frame)
        {
            return User32.FindWindowEx(frame, IntPtr.Zero, CoreWindowClass, null);
        }

        public static bool HasCoreWindow(IntPtr frame) => FindCoreWindow(frame) != IntPtr.Zero;

        /// <summary>
        /// The process that draws the frame's content, or 0 while no app is attached.
        /// </summary>
        public static uint GetAppPid(IntPtr frame)
        {
            IntPtr core = FindCoreWindow(frame);
            if (core == IntPtr.Zero)
            {
                return 0;
            }

            User32.GetWindowThreadProcessId(core, out uint pid);
            return pid;
        }

        /// <summary>
        /// The hosted app's AppUserModelID, from the frame's shell properties or else the app's package.
        /// </summary>
        public static string GetAppUserModelId(IntPtr frame, uint appPid)
        {
            try
            {
                string id = Shell32.GetWindowAppUserModelId(frame);
                if (string.IsNullOrEmpty(id) && appPid != 0)
                {
                    id = Shell32.GetProcessAppUserModelId(appPid);
                }

                return string.IsNullOrEmpty(id) ? null : id;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

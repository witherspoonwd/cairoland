using CairoDesktop.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CairoDesktop
{
    /// <summary>
    /// Starts the UI access copy of Cairo (see <see cref="UIAccess"/>) in place of this process, when it is
    /// installed. CairoDesktop.exe itself never asks for UI access, so every way of starting it (Winlogon, the
    /// installer, Restart Cairo) keeps working; if the copy can't start, this process carries on without it.
    /// </summary>
    internal static class UIAccessLauncher
    {
        /// <summary>Pass /uiaccess=false to run without UI access.</summary>
        private const string DisableArgument = "/uiaccess=false";

        /// <summary>True when this process is the UI access copy.</summary>
        public static bool IsCopy
        {
            get
            {
                try
                {
                    return Path.GetFileName(GetExePath()).Equals(UIAccess.ExeName, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return UIAccess.IsActive;
                }
            }
        }

        /// <summary>
        /// Returns the UI access copy's process when it was started, and this process should exit (or, under Shell
        /// Launcher, wait for it); otherwise null.
        /// </summary>
        public static Process TryHandOff(string[] args)
        {
            if (UIAccess.IsActive || args.Any(arg => arg.Equals(DisableArgument, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            try
            {
                string exe = GetExePath();
                if (Path.GetFileName(exe).Equals(UIAccess.ExeName, StringComparison.OrdinalIgnoreCase))
                {
                    // we are the copy
                    return null;
                }

                string copy = Path.Combine(Path.GetDirectoryName(exe), UIAccess.ExeName);
                if (!File.Exists(copy))
                {
                    return null;
                }

                // CreateProcess refuses uiAccess exes (ERROR_ELEVATION_REQUIRED); ShellExecute goes through the
                // AppInfo service, which checks the signature and location and grants UI access
                return Process.Start(new ProcessStartInfo
                {
                    FileName = copy,
                    Arguments = GetArguments(),
                    WorkingDirectory = Environment.CurrentDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // e.g. the signing certificate isn't trusted on this computer
                Trace.WriteLine($"UIAccessLauncher: Unable to start {UIAccess.ExeName}: {ex.Message}");
                return null;
            }
        }

        private static string GetExePath()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                return current.MainModule.FileName;
            }
        }

        /// <summary>This process's command line, without the exe path.</summary>
        private static string GetArguments()
        {
            string commandLine = Environment.CommandLine.TrimStart();
            int end = commandLine.StartsWith("\"") ? commandLine.IndexOf('"', 1) + 1 : commandLine.IndexOf(' ');
            return end <= 0 ? "" : commandLine.Substring(end).TrimStart();
        }
    }
}

using ManagedShell.Common.Helpers;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;

namespace CairoDesktop.Common
{
    /// <summary>
    /// How Cairo is hosted as the shell. Under Shell Launcher v2 (Windows Enterprise/Education), CustomShellHost.exe
    /// replaces Explorer and starts Cairo with /shelllauncher=true. Unlike a plain Winlogon shell, it keeps the
    /// immersive shell running, so UWP apps, Settings and Action Center still work.
    /// </summary>
    public static class ShellHost
    {
        public const string ShellLauncherArgument = "/shelllauncher=true";

        /// <summary>ReturnCodeAction DoNothing: Cairo exited on purpose (or was killed), don't restart it.</summary>
        public const int StopExitCode = 1;

        /// <summary>ReturnCodeAction RestartShell: Cairo asked Shell Launcher to start it again.</summary>
        public const int RestartExitCode = 3;

        /// <summary>Written by Enable-CairoShellLauncher.ps1 and removed by Disable-CairoShellLauncher.ps1.</summary>
        public const string MarkerKeyPath = @"Software\CairoShell";
        public const string MarkerValueName = "ShellLauncher";

        private const string CustomShellHostProcess = "CustomShellHost";

        /// <summary>True when Shell Launcher started this Cairo session.</summary>
        public static bool IsShellLauncher { get; private set; }

        /// <summary>
        /// True when something hosts UWP windows: Explorer is still running, or Cairo runs under Shell Launcher.
        /// </summary>
        public static bool HasImmersiveShell => !EnvironmentHelper.IsAppRunningAsShell || IsShellLauncher;

        /// <summary>True when Shell Launcher is configured to start Cairo for this user (cheap; no SYSTEM access).</summary>
        public static bool IsConfiguredAsShellLauncher
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MarkerKeyPath))
                    {
                        return key?.GetValue(MarkerValueName) is int value && value == 1;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Sets <see cref="IsShellLauncher"/> from the command line option. Without it, falls back to looking for
        /// CustomShellHost.exe in this session; Explorer running there means CustomShellHost started Explorer (the
        /// default profile), not Cairo. Call once at startup.
        /// </summary>
        public static void Initialize(bool optionSet)
        {
            IsShellLauncher = optionSet || (IsRunningInSession(CustomShellHostProcess) && !IsRunningInSession("explorer"));
        }

        public static bool HasShellLauncherArgument(string[] args)
        {
            return args != null && args.Any(arg => arg.Equals(ShellLauncherArgument, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCustomShellHostRunning() => IsRunningInSession(CustomShellHostProcess);

        private static bool IsRunningInSession(string processName)
        {
            try
            {
                int sessionId;
                using (Process current = Process.GetCurrentProcess())
                {
                    sessionId = current.SessionId;
                }

                Process[] processes = Process.GetProcessesByName(processName);
                bool found = processes.Any(process => process.SessionId == sessionId);
                foreach (Process process in processes)
                {
                    process.Dispose();
                }

                return found;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

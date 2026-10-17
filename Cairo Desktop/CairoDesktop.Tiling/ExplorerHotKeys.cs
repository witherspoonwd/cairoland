using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Explorer skips its own Win+letter hotkeys for the letters in the per-user DisabledHotkeys value. It reads
    /// the value when it starts, so until Explorer restarts the keyboard hook covers these keys.
    /// </summary>
    public static class ExplorerHotKeys
    {
        private const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ValueName = "DisabledHotkeys";

        public static void EnsureDisabled(char letter)
        {
            letter = char.ToUpperInvariant(letter);

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedKeyPath))
                {
                    string current = key.GetValue(ValueName) as string ?? "";
                    if (current.ToUpperInvariant().IndexOf(letter) >= 0)
                    {
                        return;
                    }

                    key.SetValue(ValueName, current + letter, RegistryValueKind.String);
                    ShellLogger.Info($"ExplorerHotKeys: Disabled Explorer's Win+{letter}");
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ExplorerHotKeys: Unable to disable Explorer's Win+{letter}: {ex.Message}");
            }
        }
    }
}

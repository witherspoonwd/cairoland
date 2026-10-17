using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace CairoDesktop.Common
{
    /// <summary>
    /// A user-defined Super (Win) key shortcut that runs a Cairo command. <see cref="Value"/> is passed as the
    /// command's parameter, for commands that take one (e.g. the path for OpenLocationInWindow).
    /// </summary>
    public class SuperKeyShortcut
    {
        public bool Shift { get; set; }

        public bool Ctrl { get; set; }

        public bool Alt { get; set; }

        /// <summary>A key name such as "E", "1", "Enter" or "F5".</summary>
        public string Key { get; set; }

        /// <summary>The Cairo command identifier.</summary>
        public string Command { get; set; }

        public string Value { get; set; }

        /// <summary>Bump when adding a default, and give the new default this version.</summary>
        public const int DefaultsVersion = 2;

        private static (int Since, SuperKeyShortcut Shortcut)[] VersionedDefaults() => new[]
        {
            (1, new SuperKeyShortcut { Key = "E", Command = "OpenLocationInWindow", Value = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" }),
            (1, new SuperKeyShortcut { Key = "Enter", Command = "TilingLaunchTerminal" }),
            (1, new SuperKeyShortcut { Key = "Q", Command = "CloseActiveWindow" }),
            (2, new SuperKeyShortcut { Key = "W", Command = "RunCommand", Value = "chrome.exe --new-window" })
        };

        public static SuperKeyShortcut[] Defaults => VersionedDefaults().Select(d => d.Shortcut).ToArray();

        /// <summary>
        /// Appends the defaults added since the settings were last upgraded, unless their keys are already taken,
        /// so existing users get new defaults without losing their own shortcuts or regaining ones they removed.
        /// </summary>
        public static void UpgradeDefaults(Settings settings)
        {
            if (settings.SuperKeyShortcutsVersion >= DefaultsVersion)
            {
                return;
            }

            List<SuperKeyShortcut> shortcuts = (settings.SuperKeyShortcuts ?? new SuperKeyShortcut[0]).ToList();
            foreach (var (since, shortcut) in VersionedDefaults())
            {
                if (since > settings.SuperKeyShortcutsVersion && !shortcuts.Any(existing => existing != null && existing.SameKeys(shortcut)))
                {
                    shortcuts.Add(shortcut);
                }
            }

            settings.SuperKeyShortcuts = shortcuts.ToArray();
            settings.SuperKeyShortcutsVersion = DefaultsVersion;
        }

        public bool SameKeys(SuperKeyShortcut other)
        {
            return Shift == other.Shift && Ctrl == other.Ctrl && Alt == other.Alt &&
                   TryGetKey(out Key key) && other.TryGetKey(out Key otherKey) && key == otherKey;
        }

        public bool TryGetKey(out Key key)
        {
            key = System.Windows.Input.Key.None;
            string name = Key?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // digits are named D0-D9 in the Key enum
            if (name.Length == 1 && char.IsDigit(name[0]))
            {
                name = "D" + name;
            }

            return Enum.TryParse(name, true, out key) && key != System.Windows.Input.Key.None;
        }

        /// <summary>The modifiers to register, given the base (Super) modifier.</summary>
        public HotKeyModifier GetModifiers(HotKeyModifier baseModifiers)
        {
            HotKeyModifier modifiers = baseModifiers;
            if (Shift) modifiers |= HotKeyModifier.Shift;
            if (Ctrl) modifiers |= HotKeyModifier.Ctrl;
            if (Alt) modifiers |= HotKeyModifier.Alt;
            return modifiers;
        }

        public override string ToString()
        {
            return $"{(Ctrl ? "Ctrl+" : "")}{(Alt ? "Alt+" : "")}{(Shift ? "Shift+" : "")}{Key} -> {Command}";
        }
    }
}

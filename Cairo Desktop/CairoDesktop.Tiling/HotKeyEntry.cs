using CairoDesktop.Common;
using System.Collections.Generic;
using System.Windows.Input;

namespace CairoDesktop.Tiling
{
    public enum HotKeyState
    {
        /// <summary>Registered with RegisterHotKey.</summary>
        Active,

        /// <summary>Windows or another program owns the combo, so the keyboard hook takes it over.</summary>
        Hooked,

        /// <summary>A built-in binding replaced by one of the user's shortcuts.</summary>
        Overridden,

        /// <summary>A user shortcut on the same keys as an earlier one.</summary>
        Duplicate,

        /// <summary>A user shortcut without a key or action yet.</summary>
        Incomplete,

        /// <summary>Win+L while it still locks the computer.</summary>
        LockKey,

        /// <summary>Not active right now (the window manager is off, or Cairo isn't the shell).</summary>
        Inactive
    }

    /// <summary>
    /// One user shortcut or built-in binding, and what happened when registering it.
    /// </summary>
    public class HotKeyEntry
    {
        public Key Key { get; set; }

        public HotKeyModifier Modifiers { get; set; }

        public string CommandId { get; set; }

        public (string name, object value)[] Parameters { get; set; }

        /// <summary>Null for a user shortcut.</summary>
        public KeyBinding Binding { get; set; }

        /// <summary>Index in Settings.SuperKeyShortcuts, or -1 for a built-in binding.</summary>
        public int ShortcutIndex { get; set; } = -1;

        public HotKeyState State { get; set; }

        /// <summary>For a duplicate or overridden entry, the entry that has the keys.</summary>
        public HotKeyEntry Winner { get; set; }

        /// <summary>For a user shortcut, the built-in bindings it replaces.</summary>
        public List<HotKeyEntry> Replaces { get; } = new List<HotKeyEntry>();

        public bool IsShortcut => ShortcutIndex >= 0;

        public bool IsLive => State == HotKeyState.Active || State == HotKeyState.Hooked;

        public string Combo => Key == Key.None ? "" : TilingKeymap.FormatCombo(Key, Modifiers);
    }
}

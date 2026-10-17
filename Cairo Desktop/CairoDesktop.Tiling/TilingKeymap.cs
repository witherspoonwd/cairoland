using CairoDesktop.Common;
using CairoDesktop.Tiling.Core;
using System.Collections.Generic;
using System.Windows.Input;

namespace CairoDesktop.Tiling
{
    public enum KeyBindingScope
    {
        /// <summary>Only while the window manager is enabled.</summary>
        Tiling,

        /// <summary>Only while Cairo is the shell (Explorer provides these otherwise).</summary>
        Shell,

        /// <summary>While the window manager is enabled or Cairo is the shell.</summary>
        Any
    }

    public class KeyBinding
    {
        public Key Key { get; }

        /// <summary>Modifiers added to the base tiling modifier (Win or Alt+Win).</summary>
        public HotKeyModifier ExtraModifiers { get; }

        public string CommandId { get; }

        public (string name, object value)[] Parameters { get; }

        public KeyBindingScope Scope { get; }

        public KeyBinding(Key key, HotKeyModifier extraModifiers, string commandId, params (string name, object value)[] parameters)
            : this(KeyBindingScope.Tiling, key, extraModifiers, commandId, parameters) { }

        public KeyBinding(KeyBindingScope scope, Key key, HotKeyModifier extraModifiers, string commandId, params (string name, object value)[] parameters)
        {
            Scope = scope;
            Key = key;
            ExtraModifiers = extraModifiers;
            CommandId = commandId;
            Parameters = parameters;
        }

        public override string ToString() => $"{(ExtraModifiers == HotKeyModifier.None ? "" : ExtraModifiers + "+")}{Key} -> {CommandId}";
    }

    /// <summary>
    /// The built-in key bindings. User shortcuts (Settings.SuperKeyShortcuts) override any that collide.
    /// </summary>
    public static class TilingKeymap
    {
        public const string LockCommand = "Lock";

        private static readonly (Key Letter, Key Arrow, Direction Direction)[] Directions =
        {
            (Key.H, Key.Left, Direction.Left),
            (Key.J, Key.Down, Direction.Down),
            (Key.K, Key.Up, Direction.Up),
            (Key.L, Key.Right, Direction.Right)
        };

        private static readonly Key[] TagKeys = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };

        public static IReadOnlyList<KeyBinding> Bindings { get; } = Build();

        private static List<KeyBinding> Build()
        {
            const HotKeyModifier None = HotKeyModifier.None;
            const HotKeyModifier Shift = HotKeyModifier.Shift;
            const HotKeyModifier Ctrl = HotKeyModifier.Ctrl;

            List<KeyBinding> bindings = new List<KeyBinding>();

            for (int i = 0; i < TagKeys.Length; i++)
            {
                bindings.Add(new KeyBinding(TagKeys[i], None, "TilingViewTag", ("Index", i)));
                bindings.Add(new KeyBinding(TagKeys[i], Shift, "TilingMoveToTag", ("Index", i)));
            }

            bindings.Add(new KeyBinding(Key.Escape, None, "TilingViewPreviousTag"));

            foreach (var direction in Directions)
            {
                foreach (Key key in new[] { direction.Letter, direction.Arrow })
                {
                    bindings.Add(new KeyBinding(key, None, "TilingFocusDirection", ("Direction", direction.Direction)));
                    bindings.Add(new KeyBinding(key, Shift, "TilingSwapDirection", ("Direction", direction.Direction)));
                    bindings.Add(new KeyBinding(key, Ctrl | Shift, "TilingMoveDirection", ("Direction", direction.Direction)));
                    bindings.Add(new KeyBinding(key, Ctrl, "TilingResize", ("Direction", direction.Direction)));
                }
            }

            bindings.Add(new KeyBinding(Key.Tab, None, "TilingFocusNext"));
            bindings.Add(new KeyBinding(Key.Tab, Shift, "TilingFocusPrev"));
            bindings.Add(new KeyBinding(Key.P, None, LockCommand));
            bindings.Add(new KeyBinding(Key.T, None, "TilingToggleOrientation"));
            bindings.Add(new KeyBinding(Key.E, Shift, "TilingEqualize"));
            bindings.Add(new KeyBinding(Key.Space, Ctrl, "TilingToggleFloating"));
            bindings.Add(new KeyBinding(Key.F, None, "TilingToggleFullscreen"));
            bindings.Add(new KeyBinding(Key.C, Shift, "TilingCloseWindow"));
            bindings.Add(new KeyBinding(Key.OemPeriod, None, "TilingFocusScreen", ("Next", true)));
            bindings.Add(new KeyBinding(Key.OemComma, None, "TilingFocusScreen", ("Next", false)));
            bindings.Add(new KeyBinding(Key.O, None, "TilingMoveToScreen", ("Next", true)));
            bindings.Add(new KeyBinding(Key.R, Ctrl, "RestartCairo"));

            bindings.Add(new KeyBinding(KeyBindingScope.Any, Key.R, None, "ToggleProgramLauncher"));
            bindings.Add(new KeyBinding(KeyBindingScope.Shell, Key.I, None, "OpenControlPanel"));
            bindings.Add(new KeyBinding(KeyBindingScope.Shell, Key.Pause, None, "RunCommand", ("Command", "control.exe system")));

            return bindings;
        }

        public static HotKeyModifier ParseBaseModifier(string setting)
        {
            HotKeyModifier modifiers = HotKeyModifier.None;
            foreach (string part in (setting ?? "Win").Split('+'))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "win": modifiers |= HotKeyModifier.Win; break;
                    case "alt": modifiers |= HotKeyModifier.Alt; break;
                    case "ctrl": modifiers |= HotKeyModifier.Ctrl; break;
                    case "shift": modifiers |= HotKeyModifier.Shift; break;
                }
            }

            return modifiers == HotKeyModifier.None ? HotKeyModifier.Win : modifiers;
        }

        /// <summary>A readable key combo, e.g. "Win+Shift+1".</summary>
        public static string FormatCombo(Key key, HotKeyModifier modifiers)
        {
            return FormatModifiers(modifiers) + FormatKey(key);
        }

        /// <summary>The modifiers with a trailing "+", e.g. "Alt+Win+".</summary>
        public static string FormatModifiers(HotKeyModifier modifiers)
        {
            string text = "";
            if ((modifiers & HotKeyModifier.Win) != 0) text += "Win+";
            if ((modifiers & HotKeyModifier.Ctrl) != 0) text += "Ctrl+";
            if ((modifiers & HotKeyModifier.Alt) != 0) text += "Alt+";
            if ((modifiers & HotKeyModifier.Shift) != 0) text += "Shift+";
            return text;
        }

        private static string FormatKey(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((int)(key - Key.D0)).ToString();
            }

            switch (key)
            {
                case Key.Return: return "Enter";
                case Key.Escape: return "Esc";
                case Key.OemPeriod: return ".";
                case Key.OemComma: return ",";
                case Key.Back: return "Backspace";
                default: return key.ToString();
            }
        }
    }
}

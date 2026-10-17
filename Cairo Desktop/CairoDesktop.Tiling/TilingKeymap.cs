using CairoDesktop.Common;
using CairoDesktop.Tiling.Core;
using System.Collections.Generic;
using System.Windows.Input;

namespace CairoDesktop.Tiling
{
    public class KeyBinding
    {
        public Key Key { get; }

        /// <summary>Modifiers added to the base tiling modifier (Win or Alt+Win).</summary>
        public HotKeyModifier ExtraModifiers { get; }

        public string CommandId { get; }

        public (string name, object value)[] Parameters { get; }

        public KeyBinding(Key key, HotKeyModifier extraModifiers, string commandId, params (string name, object value)[] parameters)
        {
            Key = key;
            ExtraModifiers = extraModifiers;
            CommandId = commandId;
            Parameters = parameters;
        }

        public override string ToString() => $"{(ExtraModifiers == HotKeyModifier.None ? "" : ExtraModifiers + "+")}{Key} -> {CommandId}";
    }

    /// <summary>
    /// The default key bindings, in one place. Not user-configurable in v1.
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
            bindings.Add(new KeyBinding(Key.Enter, None, "TilingLaunchTerminal"));
            bindings.Add(new KeyBinding(Key.R, None, "ToggleProgramLauncher"));
            bindings.Add(new KeyBinding(Key.R, Ctrl, "RestartCairo"));

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
    }
}

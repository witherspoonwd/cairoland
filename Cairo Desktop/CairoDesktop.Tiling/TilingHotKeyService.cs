using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.DependencyInjection;
using ManagedShell.Common.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Registers the user's Super key shortcuts and the built-in key bindings (see <see cref="KeyBindingScope"/>)
    /// while the window manager is enabled or Cairo is the shell. Shortcuts win over built-ins on the same keys.
    /// Combos that Windows or another program already owns (Win+P, Win+W, ...) fall back to a low-level keyboard
    /// hook, which sees the keys first. <see cref="Entries"/> records the outcome for each one.
    /// </summary>
    public sealed class TilingHotKeyService : CairoBackgroundService
    {
        private readonly ICairoApplication _cairoApplication;
        private readonly ICommandService _commandService;
        private readonly ITilingManager _tilingManager;
        private readonly WorkstationLockManager _lockManager;
        private readonly ILogger<TilingHotKeyService> _logger;
        private readonly Settings _settings;

        private readonly List<HotKey> _hotKeys = new List<HotKey>();
        private readonly List<HotKeyEntry> _hookEntries = new List<HotKeyEntry>();
        private LowLevelKeyboardListener _keyboardListener;
        private bool _winPFreed;

        /// <summary>Every user shortcut (in settings order), then every built-in binding.</summary>
        public IReadOnlyList<HotKeyEntry> Entries { get; private set; } = new HotKeyEntry[0];

        /// <summary>Raised on the UI thread after <see cref="Entries"/> changes.</summary>
        public event EventHandler EntriesChanged;

        public TilingHotKeyService(ICairoApplication cairoApplication, ICommandService commandService, ITilingManager tilingManager, WorkstationLockManager lockManager, ILogger<TilingHotKeyService> logger, Settings settings)
        {
            _cairoApplication = cairoApplication;
            _commandService = commandService;
            _tilingManager = tilingManager;
            _lockManager = lockManager;
            _logger = logger;
            _settings = settings;

            ServiceStartTask = new Task(() => _cairoApplication.Dispatch(Initialize));
        }

        private void Initialize()
        {
            SuperKeyShortcut.UpgradeDefaults(_settings);

            _tilingManager.EnabledChanged += (sender, e) => Refresh();
            _lockManager.StateChanged += (sender, e) => Refresh();
            _settings.PropertyChanged += Settings_PropertyChanged;
            Refresh();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.TilingModifier) || e.PropertyName == nameof(Settings.SuperKeyShortcuts))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            Unregister();
            Register();
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Register()
        {
            bool isShell = EnvironmentHelper.IsAppRunningAsShell;
            bool isTiling = _tilingManager.IsEnabled;
            bool lockKeyFree = _lockManager.IsRemapActive;
            HotKeyModifier baseModifiers = TilingKeymap.ParseBaseModifier(_settings.TilingModifier);

            List<HotKeyEntry> entries = new List<HotKeyEntry>();
            Dictionary<(Key, HotKeyModifier), HotKeyEntry> claimed = new Dictionary<(Key, HotKeyModifier), HotKeyEntry>();

            SuperKeyShortcut[] shortcuts = _settings.SuperKeyShortcuts ?? new SuperKeyShortcut[0];
            for (int i = 0; i < shortcuts.Length; i++)
            {
                SuperKeyShortcut shortcut = shortcuts[i];
                HotKeyEntry entry = new HotKeyEntry { ShortcutIndex = i, CommandId = shortcut?.Command };
                entries.Add(entry);

                if (shortcut == null || string.IsNullOrEmpty(shortcut.Command) || !shortcut.TryGetKey(out Key key))
                {
                    entry.State = HotKeyState.Incomplete;
                    continue;
                }

                entry.Key = key;
                entry.Modifiers = shortcut.GetModifiers(baseModifiers);
                entry.Parameters = GetShortcutParameters(shortcut);

                if (claimed.TryGetValue((entry.Key, entry.Modifiers), out HotKeyEntry winner))
                {
                    entry.State = HotKeyState.Duplicate;
                    entry.Winner = winner;
                    continue;
                }

                claimed.Add((entry.Key, entry.Modifiers), entry);
                Activate(entry, isTiling || isShell, lockKeyFree);
            }

            foreach (KeyBinding binding in TilingKeymap.Bindings)
            {
                HotKeyEntry entry = new HotKeyEntry
                {
                    Binding = binding,
                    Key = binding.Key,
                    Modifiers = baseModifiers | binding.ExtraModifiers,
                    CommandId = binding.CommandId,
                    Parameters = binding.Parameters
                };
                entries.Add(entry);

                if (claimed.TryGetValue((entry.Key, entry.Modifiers), out HotKeyEntry winner))
                {
                    entry.State = HotKeyState.Overridden;
                    entry.Winner = winner;
                    winner.Replaces.Add(entry);
                    continue;
                }

                bool inScope = binding.Scope == KeyBindingScope.Tiling ? isTiling
                    : binding.Scope == KeyBindingScope.Shell ? isShell
                    : isTiling || isShell;
                if (inScope)
                {
                    claimed.Add((entry.Key, entry.Modifiers), entry);
                }

                Activate(entry, inScope, lockKeyFree);

                if (entry.IsLive && entry.CommandId == TilingKeymap.LockCommand && entry.Key == Key.P && !_winPFreed)
                {
                    // Explorer's display projection flyout owns Win+P
                    ExplorerHotKeys.EnsureDisabled('P');
                    _winPFreed = true;
                }
            }

            // under Shell Launcher a lone Win opens a Start menu that can't be used, so the hook also masks it
            if (_hookEntries.Count > 0 || ShellHost.IsShellLauncher)
            {
                _keyboardListener = new LowLevelKeyboardListener { MaskLoneWin = ShellHost.IsShellLauncher };
                _keyboardListener.OnKeyDown += KeyboardListener_OnKeyDown;
                _keyboardListener.HookKeyboard();
            }

            Entries = entries;
            _logger.LogDebug($"Hotkeys: registered {_hotKeys.Count}, {_hookEntries.Count} via keyboard hook");
        }

        private void Activate(HotKeyEntry entry, bool inScope, bool lockKeyFree)
        {
            if (!inScope)
            {
                entry.State = HotKeyState.Inactive;
                return;
            }

            if (entry.Key == Key.L && (entry.Modifiers & HotKeyModifier.Win) != 0 && !lockKeyFree)
            {
                // Windows acts on Win+L before any hook sees it; the arrow keys cover the built-in L bindings
                entry.State = HotKeyState.LockKey;
                return;
            }

            HotKeyEntry captured = entry;
            HotKey hotKey = new HotKey(entry.Key, entry.Modifiers | HotKeyModifier.NoRepeat, _ => Invoke(captured), register: false);
            if (hotKey.Register())
            {
                _hotKeys.Add(hotKey);
                entry.State = HotKeyState.Active;
            }
            else
            {
                _logger.LogInformation($"Hotkeys: {entry.Combo} is taken; using the keyboard hook for {entry.CommandId}");
                _hookEntries.Add(entry);
                entry.State = HotKeyState.Hooked;
            }
        }

        private void Unregister()
        {
            foreach (HotKey hotKey in _hotKeys)
            {
                hotKey.Dispose();
            }

            _hotKeys.Clear();
            _hookEntries.Clear();

            if (_keyboardListener != null)
            {
                _keyboardListener.OnKeyDown -= KeyboardListener_OnKeyDown;
                _keyboardListener.UnHookKeyboard();
                _keyboardListener = null;
            }
        }

        private void KeyboardListener_OnKeyDown(object sender, Common.KeyEventArgs e)
        {
            foreach (HotKeyEntry entry in _hookEntries)
            {
                if (entry.Key == e.Key && entry.Modifiers == e.Modifiers)
                {
                    e.Handled = true;
                    // leave the hook callback quickly
                    _cairoApplication.Dispatch(() => Invoke(entry));
                    return;
                }
            }
        }

        private void Invoke(HotKeyEntry entry)
        {
            try
            {
                _commandService.InvokeCommand(entry.CommandId, entry.Parameters ?? new (string, object)[0]);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Hotkeys: error running {entry.CommandId}: {ex.Message}");
            }
        }

        /// <summary>
        /// The shortcut's value goes to the command's first required parameter, else its first parameter.
        /// </summary>
        private (string name, object value)[] GetShortcutParameters(SuperKeyShortcut shortcut)
        {
            ICairoCommandInfo info = _commandService.Commands.FirstOrDefault(c => c.Identifier == shortcut.Command);
            List<CairoCommandParameter> commandParameters = info?.Parameters?.ToList() ?? new List<CairoCommandParameter>();
            if (commandParameters.Count == 0 || string.IsNullOrEmpty(shortcut.Value))
            {
                return new (string, object)[0];
            }

            string name = commandParameters.FirstOrDefault(p => p.IsRequired).Name ?? commandParameters[0].Name;
            return new (string, object)[] { (name, shortcut.Value) };
        }

        public override async Task StopAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            {
                Unregister();
            }

            await base.StopAsync(cancellationToken);
        }
    }
}

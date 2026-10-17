using CairoDesktop.Application.Interfaces;
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
    /// Registers the tiling key bindings while tiling is enabled. Bindings on L fall back to a low-level keyboard
    /// hook when RegisterHotKey can't take them (Windows may keep Win+L reserved).
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
        private readonly List<(KeyBinding Binding, HotKeyModifier Modifiers)> _hookBindings = new List<(KeyBinding, HotKeyModifier)>();
        private LowLevelKeyboardListener _keyboardListener;

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
            _tilingManager.EnabledChanged += (sender, e) => Refresh();
            _lockManager.StateChanged += (sender, e) => Refresh();
            _settings.PropertyChanged += Settings_PropertyChanged;
            Refresh();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.TilingModifier))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            Unregister();
            if (_tilingManager.IsEnabled)
            {
                Register();
            }
        }

        private void Register()
        {
            HotKeyModifier baseModifiers = TilingKeymap.ParseBaseModifier(_settings.TilingModifier);
            bool lockKeyFree = _lockManager.IsRemapActive;

            foreach (KeyBinding binding in TilingKeymap.Bindings)
            {
                if (binding.Key == Key.L && !lockKeyFree)
                {
                    // Win+L still locks; the arrow keys cover these bindings
                    continue;
                }

                if (binding.CommandId == TilingKeymap.LockCommand && !EnvironmentHelper.IsAppRunningAsShell && !lockKeyFree)
                {
                    continue;
                }

                HotKeyModifier modifiers = baseModifiers | binding.ExtraModifiers;
                KeyBinding captured = binding;
                HotKey hotKey = new HotKey(binding.Key, modifiers | HotKeyModifier.NoRepeat, _ => Invoke(captured), register: false);

                if (hotKey.Register())
                {
                    _hotKeys.Add(hotKey);
                }
                else if (binding.Key == Key.L)
                {
                    _logger.LogInformation($"Tiling: {modifiers}+{binding.Key} is reserved; using the keyboard hook");
                    _hookBindings.Add((binding, modifiers));
                }
                else
                {
                    _logger.LogWarning($"Tiling: unable to register hotkey {modifiers}+{binding.Key} for {binding.CommandId}");
                }
            }

            if (_hookBindings.Count > 0)
            {
                _keyboardListener = new LowLevelKeyboardListener();
                _keyboardListener.OnKeyDown += KeyboardListener_OnKeyDown;
                _keyboardListener.HookKeyboard();
            }

            _logger.LogDebug($"Tiling: registered {_hotKeys.Count} hotkeys, {_hookBindings.Count} via keyboard hook");
        }

        private void Unregister()
        {
            foreach (HotKey hotKey in _hotKeys)
            {
                hotKey.Dispose();
            }

            _hotKeys.Clear();
            _hookBindings.Clear();

            if (_keyboardListener != null)
            {
                _keyboardListener.OnKeyDown -= KeyboardListener_OnKeyDown;
                _keyboardListener.UnHookKeyboard();
                _keyboardListener = null;
            }
        }

        private void KeyboardListener_OnKeyDown(object sender, Common.KeyEventArgs e)
        {
            foreach (var hookBinding in _hookBindings)
            {
                if (hookBinding.Binding.Key == e.Key && hookBinding.Modifiers == e.Modifiers)
                {
                    e.Handled = true;
                    // leave the hook callback quickly
                    _cairoApplication.Dispatch(() => Invoke(hookBinding.Binding));
                    return;
                }
            }
        }

        private void Invoke(KeyBinding binding)
        {
            try
            {
                _commandService.InvokeCommand(binding.CommandId, binding.Parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Tiling: error running {binding.CommandId}: {ex.Message}");
            }
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

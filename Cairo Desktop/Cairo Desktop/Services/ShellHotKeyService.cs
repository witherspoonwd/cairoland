using System;
using System.Threading.Tasks;
using CairoDesktop.Common;
using System.Windows.Input;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Infrastructure.DependencyInjection;
using CairoDesktop.Tiling;
using ManagedShell.Common.Helpers;

namespace CairoDesktop.Services
{
    public sealed class ShellHotKeyService : CairoBackgroundService
    {
        private readonly ICairoApplication _cairoApplication;
        private readonly ICommandService _commandService;
        private readonly ITilingManager _tilingManager;

        private HotKey _launcherHotKey;

        public ShellHotKeyService(ICairoApplication cairoApplication, ICommandService commandService, ITilingManager tilingManager)
        {
            _cairoApplication = cairoApplication;
            _tilingManager = tilingManager;

            ServiceStartTask = new Task(RegisterSystemHotkeys);
            _commandService = commandService;
        }

        private void RegisterSystemHotkeys()
        {
            if (EnvironmentHelper.IsAppRunningAsShell)
            {
                _cairoApplication.Dispatch(() =>
                {
                    new HotKey(Key.E, HotKeyModifier.Win | HotKeyModifier.NoRepeat, OnWinECommand);
                    new HotKey(Key.I, HotKeyModifier.Win | HotKeyModifier.NoRepeat, OnWinICommand);
                    new HotKey(Key.Pause, HotKeyModifier.Win | HotKeyModifier.NoRepeat, OnWinPauseCommand);

                    UpdateLauncherHotKey();
                    _tilingManager.EnabledChanged += TilingManager_EnabledChanged;
                });
            }
        }

        private void TilingManager_EnabledChanged(object sender, EventArgs e)
        {
            // run after the tiling hotkey service has released or claimed Win+R
            _cairoApplication.Dispatch(UpdateLauncherHotKey);
        }

        /// <summary>
        /// The tiling keymap owns Win+R while tiling is on; otherwise it is registered here.
        /// </summary>
        private void UpdateLauncherHotKey()
        {
            if (_tilingManager.IsEnabled)
            {
                _launcherHotKey?.Dispose();
                _launcherHotKey = null;
            }
            else if (_launcherHotKey == null)
            {
                _launcherHotKey = new HotKey(Key.R, HotKeyModifier.Win | HotKeyModifier.NoRepeat, OnWinRCommand);
            }
        }

        private void OnWinRCommand(HotKey cmd)
        {
            _commandService.InvokeCommand("ToggleProgramLauncher");
        }

        private void OnWinECommand(HotKey cmd)
        {
            _commandService.InvokeCommand("OpenLocationInWindow", ("Path", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        }

        private void OnWinICommand(HotKey cmd)
        {
            _commandService.InvokeCommand("OpenControlPanel");
        }

        private void OnWinPauseCommand(HotKey cmd)
        {
            ShellHelper.StartProcess("system.cpl");
        }
    }
}

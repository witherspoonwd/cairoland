using CairoDesktop.Application.Interfaces;
using CairoDesktop.Infrastructure.ExtensionMethods;
using ManagedShell;
using System;
using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Infrastructure.Services;
using CairoDesktop.Common;

namespace CairoDesktop.MenuBarExtensions
{
    public sealed class MenuBarExtensionsShellExtension : IShellExtension
    {
        private readonly ICairoApplication _cairoApplication;
        private readonly ICommandService _commandService;
        private readonly Settings _settings;
        private readonly ShellManager _shellManager;

        public List<IMenuBarExtension> MenuExtras { get; private set; }

        public MenuBarExtensionsShellExtension(ICairoApplication cairoApplication, ShellManagerService shellManagerService, ICommandService commandService, Settings settings)
        {
            _cairoApplication = cairoApplication;
            _commandService = commandService;
            _settings = settings;
            _shellManager = shellManagerService.ShellManager;
            MenuExtras = new List<IMenuBarExtension>();
        }

        public void Start()
        {
            // left to right, matching the runch bar: volume and tray share one block, then ram, cpu and the clock
            MenuExtras.AddRange(new ActionCenterMenuBarExtension(_settings),
                                new SearchMenuBarExtension(_cairoApplication, _settings),
                                new VolumeMenuBarExtension(_settings),
                                new SystemTrayMenuBarExtension(_shellManager.NotificationArea, _settings),
                                new SystemMonitorMenuBarExtension(),
                                new ClockMenuBarExtension(_commandService, _settings));

            MenuExtras.AddTo(_cairoApplication.MenuBarExtensions);
        }

        public void Stop()
        {
            MenuExtras.RemoveFrom(_cairoApplication.MenuBarExtensions);
            foreach (var disposable in MenuExtras.OfType<IDisposable>())
            {
                disposable.Dispose();
            }
            MenuExtras.Clear();
            MenuExtras = null;
        }
    }
}
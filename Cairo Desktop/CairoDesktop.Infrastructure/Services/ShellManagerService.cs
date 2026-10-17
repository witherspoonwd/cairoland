using CairoDesktop.Common;
using ManagedShell;
using ManagedShell.Common.Enums;
using System;

namespace CairoDesktop.Infrastructure.Services
{
    public class ShellManagerService : IDisposable
    {
        private readonly Settings _settings;

        public ShellManager ShellManager { get; }

        public ShellManagerService(Settings settings)
        {
            _settings = settings;

            ShellManager = ConfigureShellManager();
        }

        private ShellManager ConfigureShellManager()
        {
            ShellConfig config = new ShellConfig()
            {
                // The tasks service is never started (the tiling engine tracks windows itself),
                // but ManagedShell's FullScreenHelper requires an instance.
                EnableTasksService = true,
                AutoStartTasksService = false,
                TaskIconSize = IconSize.Small,

                EnableTrayService = _settings.EnableSysTray,
                AutoStartTrayService = false,
                PinnedNotifyIcons = _settings.PinnedNotifyIcons
            };

            return new ShellManager(config);
        }

        public void Dispose()
        {
            ShellManager.Dispose();
        }
    }
}

using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.Services;
using CairoDesktop.Tiling;

namespace CairoDesktop.Services
{
    public class SettingsUIService : ISettingsUIService
    {
        private readonly IAppGrabber _appGrabber;
        private readonly ICairoApplication _cairoApplication;
        private readonly ICommandService _commandService;
        private readonly ShellManagerService _shellManager;
        private readonly IThemeService _themeService;
        private readonly IApplicationUpdateService _updateService;
        private readonly WorkstationLockManager _lockManager;
        private readonly Settings _settings;

        internal SettingsUI SettingsUi;

        public SettingsUIService(ICairoApplication cairoApplication, IAppGrabber appGrabber, IApplicationUpdateService updateService,
            ShellManagerService shellManager, IThemeService themeService, ICommandService commandService, WorkstationLockManager lockManager, Settings settings)
        {
            _appGrabber = appGrabber;
            _cairoApplication = cairoApplication;
            _commandService = commandService;
            _shellManager = shellManager;
            _themeService = themeService;
            _updateService = updateService;
            _lockManager = lockManager;
            _settings = settings;
        }

        public void Show()
        {
            if (SettingsUi == null)
            {
                SettingsUi = new SettingsUI(_cairoApplication, this, _shellManager, _updateService, _appGrabber, _themeService, _commandService, _lockManager, _settings);
            }

            SettingsUi.Show();
            SettingsUi.Activate();
        }

        public void Show(string tabIdentifier)
        {
            // Eventually extensions should be able to add their own tab to the settings window. To show their tab, they pass their extension GUID here.

            Show();

            switch (tabIdentifier)
            {
                case "tiling":
                    SettingsUi.TabTiling.IsSelected = true;
                    break;
            }
        }
    }
}

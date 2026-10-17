using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.ObjectModel;
using CairoDesktop.Infrastructure.Services;
using CairoDesktop.Tiling;
using ManagedShell.AppBar;

namespace CairoDesktop.MenuBar.Services
{
    public class MenuBarWindowService : AppBarWindowService
    {
        private readonly ISettingsUIService _settingsUiService;
        private readonly IApplicationUpdateService _updateService;
        private readonly ICommandService _commandService;
        private readonly ITilingManager _tilingManager;

        public MenuBarWindowService(ICairoApplication cairoApplication, AppBarEventService appBarEventService, ShellManagerService shellManagerService, IWindowManager windowManager, IApplicationUpdateService updateService, ISettingsUIService settingsUiService, ICommandService commandService, ITilingManager tilingManager, Settings settings) : base(cairoApplication, appBarEventService, shellManagerService, windowManager, settings)
        {
            _settingsUiService = settingsUiService;
            _updateService = updateService;
            _commandService = commandService;
            _tilingManager = tilingManager;

            // tags are per screen, so every screen always gets its own bar
            EnableMultiMon = true;
            EnableService = _settings.EnableMenuBar;
        }

        protected override void HandleSettingChange(string setting)
        {
            switch (setting)
            {
                case "EnableMenuBar":
                    HandleEnableServiceChanged(_settings.EnableMenuBar);
                    break;
            }
        }

        protected override void OpenWindow(AppBarScreen screen)
        {
            MenuBar newMenuBar = new MenuBar(_cairoApplication, _appBarEventService, _shellManager, _windowManager, _updateService, _settingsUiService, _commandService, _tilingManager, _settings, screen, _settings.MenuBarEdge, _settings.EnableMenuBarAutoHide ? AppBarMode.AutoHide : AppBarMode.Normal);
            Windows.Add(newMenuBar);
            newMenuBar.Show();
        }
    }
}

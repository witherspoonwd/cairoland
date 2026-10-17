using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.ObjectModel;
using CairoDesktop.Infrastructure.Services;
using ManagedShell.AppBar;

namespace CairoDesktop.MenuBar.Services
{
    public class MenuBarWindowService : AppBarWindowService
    {
        private readonly ISettingsUIService _settingsUiService;
        private readonly IApplicationUpdateService _updateService;
        private readonly ICommandService _commandService;

        public MenuBarWindowService(ICairoApplication cairoApplication, AppBarEventService appBarEventService, ShellManagerService shellManagerService, IWindowManager windowManager, IApplicationUpdateService updateService, ISettingsUIService settingsUiService, ICommandService commandService, Settings settings) : base(cairoApplication, appBarEventService, shellManagerService, windowManager, settings)
        {
            _settingsUiService = settingsUiService;
            _updateService = updateService;
            _commandService = commandService;

            EnableMultiMon = _settings.EnableMenuBarMultiMon;
            EnableService = _settings.EnableMenuBar;
        }

        protected override void HandleSettingChange(string setting)
        {
            switch (setting)
            {
                case "EnableMenuBar":
                    HandleEnableServiceChanged(_settings.EnableMenuBar);
                    break;
                case "EnableMenuBarMultiMon":
                    HandleEnableMultiMonChanged(_settings.EnableMenuBarMultiMon);
                    break;
            }
        }

        protected override void OpenWindow(AppBarScreen screen)
        {
            MenuBar newMenuBar = new MenuBar(_cairoApplication, _appBarEventService, _shellManager, _windowManager, _updateService, _settingsUiService, _commandService, _settings, screen, _settings.MenuBarEdge, _settings.EnableMenuBarAutoHide ? AppBarMode.AutoHide : AppBarMode.Normal);
            Windows.Add(newMenuBar);
            newMenuBar.Show();
        }
    }
}

using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.DependencyInjection;
using System.Threading.Tasks;

namespace CairoDesktop.MenuBar.Services
{
    public sealed class MenuBarHotKeyService : CairoBackgroundService
    {
        private HotKey cairoMenuHotKey;

        private readonly ICairoApplication _cairoApplication;
        private readonly ICommandService _commandService;
        private readonly Settings _settings;

        public MenuBarHotKeyService(ICairoApplication cairoApplication, ICommandService commandService, Settings settings)
        {
            _cairoApplication = cairoApplication;
            _settings = settings;
            _settings.PropertyChanged += Settings_PropertyChanged;

            ServiceStartTask = new Task(RegisterHotkeys);
            _commandService = commandService;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.PropertyName)) return;

            switch (e.PropertyName)
            {
                case nameof(_settings.CairoMenuHotKey):
                    if (_settings.EnableCairoMenuHotKey)
                    {
                        unregisterCairoMenuHotKey();
                        registerCairoMenuHotKey();
                    }

                    break;
                case nameof(_settings.EnableCairoMenuHotKey):
                    if (_settings.EnableCairoMenuHotKey)
                    {
                        registerCairoMenuHotKey();
                    }
                    else
                    {
                        unregisterCairoMenuHotKey();
                    }

                    break;
            }
        }

        private void RegisterHotkeys()
        {
            registerCairoMenuHotKey();
        }

        private void registerCairoMenuHotKey()
        {
            if (!_settings.EnableCairoMenuHotKey)
            {
                return;
            }

            _cairoApplication.Dispatch(() =>
            {
                cairoMenuHotKey = HotKeyManager.RegisterHotKey(_settings.CairoMenuHotKey, OnToggleCairoMenu);
            });
        }

        private void unregisterCairoMenuHotKey()
        {
            cairoMenuHotKey?.Dispose();
            cairoMenuHotKey = null;
        }

        private void OnToggleCairoMenu(HotKey obj)
        {
            _commandService.InvokeCommand("ToggleCairoMenu");
        }
    }
}

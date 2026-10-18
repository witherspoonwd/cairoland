using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Infrastructure.DependencyInjection;
using CairoDesktop.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace CairoDesktop.Services
{
    class FirstRunService : CairoBackgroundService
    {
        private readonly ICairoApplication _cairoApplication;
        private readonly ILogger<FirstRunService> _logger;
        private readonly Settings _settings;
        private readonly SetupCheckService _setupCheck;

        private readonly bool _forceTour;

        // lets tiling start (and find out whether Win+L can be remapped) before the Setup check runs
        private static readonly TimeSpan SetupCheckDelay = TimeSpan.FromSeconds(5);

        public FirstRunService(ICairoApplication cairoApplication, ILogger<FirstRunService> logger, IOptionsMonitor<CommandLineOptions> options, Settings settings, SetupCheckService setupCheck)
        {
            _cairoApplication = cairoApplication;
            _logger = logger;
            _settings = settings;
            _setupCheck = setupCheck;

            try
            {
                _forceTour = options.CurrentValue.Tour;
            }
            catch (Exception e)
            {
                _logger.LogError($"Unable to read tour command line option: {e.Message}");
            }

            ServiceStartTask = new Task(FirstRun);
        }

        private void FirstRun()
        {
            if (_settings.IsFirstRun || _forceTour)
            {
                // the tour ends with the Setup check
                _cairoApplication.Dispatch(() =>
                {
                    ShowWelcome();
                });
                return;
            }

            _cairoApplication.Dispatch(RunSetupCheck);
        }

        private void ShowWelcome()
        {
            Welcome welcome = new Welcome(_cairoApplication, _settings, _setupCheck);
            welcome.Show();
        }

        /// <summary>
        /// Opens the Setup check at startup when it finds something to act on that the user hasn't dismissed.
        /// </summary>
        private async void RunSetupCheck()
        {
            try
            {
                await Task.Delay(SetupCheckDelay);
                var items = await _setupCheck.RunAsync();
                if (_setupCheck.ShouldShow(items))
                {
                    Welcome.ShowSetupCheck(_cairoApplication, _settings, _setupCheck, items);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Unable to run the setup check: {e.Message}");
            }
        }
    }
}

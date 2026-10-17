using CairoDesktop.Application.Interfaces;
using CairoDesktop.Infrastructure.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Starts the tiling manager on the UI thread once the shell is up, and stops it on exit.
    /// </summary>
    public sealed class TilingService : CairoBackgroundService
    {
        private readonly ICairoApplication _cairoApplication;
        private readonly TilingManager _tilingManager;

        public TilingService(ICairoApplication cairoApplication, ITilingManager tilingManager)
        {
            _cairoApplication = cairoApplication;
            _tilingManager = (TilingManager)tilingManager;

            // Dispatch runs after startup finishes, when screens and menu bars exist
            ServiceStartTask = new Task(() => _cairoApplication.Dispatch(_tilingManager.Initialize));
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            {
                _tilingManager.Dispose();
            }
            else
            {
                _tilingManager.EmergencyRestore();
            }

            await base.StopAsync(cancellationToken);
        }
    }
}

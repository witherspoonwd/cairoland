using CairoDesktop.Application.Interfaces;
using CairoDesktop.Infrastructure.ObjectModel;
using System;
using System.Windows.Controls;

namespace CairoDesktop.MenuBarExtensions
{
    class SystemMonitorMenuBarExtension : UserControlMenuBarExtension, IDisposable
    {
        // shared by every menu bar so each screen doesn't sample separately
        private readonly SystemStats _stats = new SystemStats();

        public override UserControl StartControl(IMenuBar host)
        {
            return new SystemMonitor(_stats);
        }

        public void Dispose()
        {
            _stats.Dispose();
        }
    }
}

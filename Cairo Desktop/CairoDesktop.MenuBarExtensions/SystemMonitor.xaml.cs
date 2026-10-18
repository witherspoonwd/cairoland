using System.Windows;
using System.Windows.Controls;

namespace CairoDesktop.MenuBarExtensions
{
    /// <summary>
    /// The RAM block and the CPU usage/temperature block from the runch quickshell bar.
    /// </summary>
    public partial class SystemMonitor : UserControl
    {
        private readonly SystemStats _stats;
        private bool _isLoaded;

        public SystemMonitor(SystemStats stats)
        {
            _stats = stats;
            DataContext = stats;

            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            _stats.AddUser();
            _isLoaded = true;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            _stats.RemoveUser();
            _isLoaded = false;
        }
    }
}

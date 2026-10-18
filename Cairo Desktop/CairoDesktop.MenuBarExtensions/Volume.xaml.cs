using CairoDesktop.Interop;
using ManagedShell.Common.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CairoDesktop.MenuBarExtensions
{
    public partial class Volume : UserControl
    {
        // the scroll wheel changes the volume in steps of 5%, like the runch quickshell bar
        private const float VolumeStep = 0.05f;

        private bool _isLoaded;
        private DispatcherTimer _volumeTimer;

        public Volume()
        {
            InitializeComponent();
        }

        private void Volume_Tick()
        {
            txtVolume.Text = (int)Math.Round(VolumeHelper.GetMasterVolume() * 100) + "%";
        }

        private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0)
            {
                return;
            }

            if (CoreAudio.AdjustMasterVolume(e.Delta > 0 ? VolumeStep : -VolumeStep))
            {
                Volume_Tick();
            }

            e.Handled = true;
        }

        private void miOpenVolume_Click(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartProcess("sndvol.exe", "-f " + (int)(((ushort)(System.Windows.Forms.Cursor.Position.X / DpiHelper.DpiScaleAdjustment)) | (uint)((int)ActualHeight << 16)));
        }

        private void miOpenSoundSettings_Click(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartProcess("mmsys.cpl");
        }

        private void miOpenVolumeMixer_Click(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartProcess("sndvol.exe", "-T " + (int)(((ushort)(System.Windows.Forms.Cursor.Position.X / DpiHelper.DpiScaleAdjustment)) | (uint)((int)ActualHeight << 16)));
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            Volume_Tick();

            _volumeTimer = new DispatcherTimer(new TimeSpan(0, 0, 0, 1), DispatcherPriority.Background, delegate
            {
                Volume_Tick();
            }, Dispatcher);

            _isLoaded = true;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            _volumeTimer?.Stop();
            _volumeTimer = null;

            _isLoaded = false;
        }
    }
}

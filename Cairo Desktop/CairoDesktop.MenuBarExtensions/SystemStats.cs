using CairoDesktop.Interop;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace CairoDesktop.MenuBarExtensions
{
    /// <summary>
    /// RAM, CPU usage and CPU temperature as display strings, sampled on timers while any monitor is shown.
    /// Formats and intervals match the runch quickshell scripts (ramUsage.sh, cpuUsage.sh, cpuTemp.sh).
    /// </summary>
    public sealed class SystemStats : INotifyPropertyChanged, IDisposable
    {
        private const double BytesPerGiB = 1024d * 1024d * 1024d;

        private const string NoTemperature = "-- C°";
        private const string NoTemperatureToolTip = "CPU temperature needs LibreHardwareMonitor running as administrator.";

        // Windows only exposes the CPU's own sensor (Tctl on AMD) to kernel drivers, so the temperature comes from
        // LibreHardwareMonitor (or OpenHardwareMonitor) when it is running and publishing its sensors to WMI.
        // The ACPI thermal zones Windows does expose are often a fixed placeholder on desktops, so they aren't used.
        private static readonly string[] HardwareMonitorNamespaces = { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" };

        // in order of preference: AMD's control temperature, then Intel's package temperature
        private static readonly string[] CpuTemperatureSensors = { "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "CPU Package" };

        public event PropertyChangedEventHandler PropertyChanged;

        private readonly DispatcherTimer _ramTimer;
        private readonly DispatcherTimer _cpuTimer;
        private readonly DispatcherTimer _tempTimer;

        private int _users;

        private ulong _prevIdle;
        private ulong _prevTotal;

        private bool _isReadingTemperature;

        private string _ramUsage = "";
        public string RamUsage
        {
            get => _ramUsage;
            private set => Set(ref _ramUsage, value);
        }

        private string _cpuUsage = "";
        public string CpuUsage
        {
            get => _cpuUsage;
            private set => Set(ref _cpuUsage, value);
        }

        private string _cpuTemperature = "";
        public string CpuTemperature
        {
            get => _cpuTemperature;
            private set => Set(ref _cpuTemperature, value);
        }

        private string _cpuTemperatureToolTip;
        public string CpuTemperatureToolTip
        {
            get => _cpuTemperatureToolTip;
            private set => Set(ref _cpuTemperatureToolTip, value);
        }

        public SystemStats()
        {
            _ramTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _ramTimer.Tick += (sender, e) => UpdateRam();

            _cpuTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _cpuTimer.Tick += (sender, e) => UpdateCpu();

            _tempTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _tempTimer.Tick += (sender, e) => UpdateTemperature();
        }

        /// <summary>Starts sampling when the first monitor is shown.</summary>
        public void AddUser()
        {
            if (_users++ > 0)
            {
                return;
            }

            UpdateRam();
            UpdateCpu();
            UpdateTemperature();

            _ramTimer.Start();
            _cpuTimer.Start();
            _tempTimer.Start();
        }

        /// <summary>Stops sampling when the last monitor goes away.</summary>
        public void RemoveUser()
        {
            if (_users == 0 || --_users > 0)
            {
                return;
            }

            _ramTimer.Stop();
            _cpuTimer.Stop();
            _tempTimer.Stop();
        }

        private void UpdateRam()
        {
            Kernel32.MEMORYSTATUSEX status = Kernel32.MEMORYSTATUSEX.Create();
            if (!Kernel32.GlobalMemoryStatusEx(ref status))
            {
                return;
            }

            double total = status.ullTotalPhys / BytesPerGiB;
            double used = (status.ullTotalPhys - status.ullAvailPhys) / BytesPerGiB;
            RamUsage = string.Format(CultureInfo.InvariantCulture, "{0:0.0} / {1:0.0} GB", used, total);
        }

        private void UpdateCpu()
        {
            if (!Kernel32.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                return;
            }

            // kernel time already includes idle time
            ulong total = kernel + user;
            ulong totalDiff = total - _prevTotal;
            ulong idleDiff = idle - _prevIdle;

            // like cpuUsage.sh, nothing is shown until there are two samples to compare
            if (_prevTotal != 0 && totalDiff > 0)
            {
                double usage = (totalDiff - idleDiff) * 100d / totalDiff;
                CpuUsage = string.Format(CultureInfo.InvariantCulture, "{0:0.00}%", usage);
            }

            _prevTotal = total;
            _prevIdle = idle;
        }

        private void UpdateTemperature()
        {
            if (_isReadingTemperature)
            {
                return;
            }

            // WMI queries can take a while, so keep them off the UI thread
            _isReadingTemperature = true;
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            Task.Run(ReadCpuTemperature).ContinueWith(task =>
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    _isReadingTemperature = false;

                    double? celsius = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                    CpuTemperature = celsius.HasValue
                        ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} C°", celsius.Value)
                        : NoTemperature;
                    CpuTemperatureToolTip = celsius.HasValue ? null : NoTemperatureToolTip;
                }));
            });
        }

        private static double? ReadCpuTemperature()
        {
            foreach (string scope in HardwareMonitorNamespaces)
            {
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, "SELECT Name, Value, Parent FROM Sensor WHERE SensorType = 'Temperature'"))
                    using (ManagementObjectCollection sensors = searcher.Get())
                    {
                        double? best = null;
                        int bestRank = int.MaxValue;

                        foreach (ManagementBaseObject sensor in sensors)
                        {
                            using (sensor)
                            {
                                string parent = sensor["Parent"] as string ?? "";
                                if (parent.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    continue;
                                }

                                // any other CPU temperature (a CCD, say) beats nothing
                                int rank = Array.IndexOf(CpuTemperatureSensors, sensor["Name"] as string);
                                if (rank < 0)
                                {
                                    rank = CpuTemperatureSensors.Length;
                                }

                                if (rank < bestRank && sensor["Value"] != null)
                                {
                                    best = Convert.ToDouble(sensor["Value"], CultureInfo.InvariantCulture);
                                    bestRank = rank;
                                }
                            }
                        }

                        if (best.HasValue)
                        {
                            return best;
                        }
                    }
                }
                catch (ManagementException)
                {
                    // the monitor isn't running, so its namespace doesn't exist
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return null;
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _ramTimer.Stop();
            _cpuTimer.Stop();
            _tempTimer.Stop();
        }
    }
}

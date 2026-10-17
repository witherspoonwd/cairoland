using CairoDesktop.Common.Logging;
using ManagedShell.AppBar;
using ManagedShell.Common.Enums;
using ManagedShell.WindowsTray;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace CairoDesktop.Common
{
    public class Settings : INotifyPropertyChanged
    {
        private static Settings instance;

        public static Settings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = _settingsFile.Settings;
                    _isInitializing = false;
                }

                return instance;
            }
        }

        private static string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cairo Desktop", "settings.json");
        private static bool _isInitializing = true;
        private static SettingsFile<Settings> _settingsFile = new SettingsFile<Settings>(_settingsPath, new Settings());
        
        public event PropertyChangedEventHandler PropertyChanged;

        // This should not be used directly! Unfortunately it must be public for JsonSerializer.
        public Settings()
        {
            PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _settingsFile.Settings = this;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (field?.Equals(value) != true)
            {
                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        protected void SetEnum<T>(ref T field, T value, [CallerMemberName] string propertyName = "") where T : Enum
        {
            if (!field.Equals(value))
            {
                if (!Enum.IsDefined(typeof(T), value))
                {
                    return;
                }

                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        #region Public properties

        #region General
        private bool _isFirstRun = true;
        public bool IsFirstRun
        {
            get => _isFirstRun;
            set => Set(ref _isFirstRun, value);
        }

        private string _theme = "Default";
        public string Theme
        {
            get => _theme;
            set => Set(ref _theme, value);
        }

        private string _language = "";
        public string Language
        {
            get
            {
                // if we are Null or Empty, use the process's culture
                if (string.IsNullOrEmpty(_language))
                {
                    _language = CultureInfo.CurrentUICulture.Name.Replace('-', '_');
                }

                return _language;
            }
            set
            {
                // if we are Null or Empty, use the process's culture
                if (string.IsNullOrEmpty(value))
                {
                    Set(ref _language, CultureInfo.CurrentUICulture.Name.Replace('-', '_'));
                }
                else if (_language != value)
                {
                    Set(ref _language, value);
                }
            }
        }
        #endregion

        #region Menu Bar
        private bool _enableMenuBar = true;
        public bool EnableMenuBar
        {
            get => _enableMenuBar;
            set => Set(ref _enableMenuBar, value);
        }

        private bool _enableMenuBarShadow = true;
        public bool EnableMenuBarShadow
        {
            get => _enableMenuBarShadow;
            set => Set(ref _enableMenuBarShadow, value);
        }

        private bool _enableSysTray = true;
        public bool EnableSysTray
        {
            get => _enableSysTray;
            set => Set(ref _enableSysTray, value);
        }

        private bool _sysTrayAlwaysExpanded;
        public bool SysTrayAlwaysExpanded
        {
            get => _sysTrayAlwaysExpanded;
            set => Set(ref _sysTrayAlwaysExpanded, value);
        }

        private bool _enableCairoMenuHotKey = true;
        public bool EnableCairoMenuHotKey
        {
            get => _enableCairoMenuHotKey;
            set => Set(ref _enableCairoMenuHotKey, value);
        }

        private string[] _cairoMenuHotKey = { "Win", "Shift", "Z" };
        public string[] CairoMenuHotKey
        {
            get => _cairoMenuHotKey;
            set => Set(ref _cairoMenuHotKey, value);
        }

        private bool _enableMenuBarBlur = true;
        public bool EnableMenuBarBlur
        {
            get => _enableMenuBarBlur;
            set => Set(ref _enableMenuBarBlur, value);
        }

        private bool _enableMenuBarMultiMon;
        public bool EnableMenuBarMultiMon
        {
            get => _enableMenuBarMultiMon;
            set => Set(ref _enableMenuBarMultiMon, value);
        }

        private bool _showHibernate;
        public bool ShowHibernate
        {
            get => _showHibernate;
            set => Set(ref _showHibernate, value);
        }

        private string[] _pinnedNotifyIcons = { NotificationArea.HEALTH_GUID, NotificationArea.POWER_GUID, NotificationArea.NETWORK_GUID, NotificationArea.VOLUME_GUID };
        public string[] PinnedNotifyIcons
        {
            get => _pinnedNotifyIcons;
            set => Set(ref _pinnedNotifyIcons, value);
        }

        private bool _enableMenuExtraVolume = true;
        public bool EnableMenuExtraVolume
        {
            get => _enableMenuExtraVolume;
            set => Set(ref _enableMenuExtraVolume, value);
        }

        private bool _enableMenuExtraActionCenter = true;
        public bool EnableMenuExtraActionCenter
        {
            get => _enableMenuExtraActionCenter;
            set => Set(ref _enableMenuExtraActionCenter, value);
        }

        private bool _enableMenuExtraClock = true;
        public bool EnableMenuExtraClock
        {
            get => _enableMenuExtraClock;
            set => Set(ref _enableMenuExtraClock, value);
        }

        private bool _enableMenuExtraSearch = true;
        public bool EnableMenuExtraSearch
        {
            get => _enableMenuExtraSearch;
            set => Set(ref _enableMenuExtraSearch, value);
        }

        private bool _enableMenuBarAutoHide;
        public bool EnableMenuBarAutoHide
        {
            get => _enableMenuBarAutoHide;
            set => Set(ref _enableMenuBarAutoHide, value);
        }

        private AppBarEdge _menuBarEdge = AppBarEdge.Top;
        public AppBarEdge MenuBarEdge
        {
            get => _menuBarEdge;
            set
            {
                if (_menuBarEdge != value)
                {
                    if (value == AppBarEdge.Left || value == AppBarEdge.Right)
                    {
                        // Vertical orientation is not supported
                        return;
                    }

                    SetEnum(ref _menuBarEdge, value);
                }
            }
        }
        #endregion

        #region Advanced
        private string _timeFormat;
        public string TimeFormat
        {
            get
            {
                // if Null or Empty, get CurrentCulture
                if (string.IsNullOrEmpty(_timeFormat))
                {
                    _timeFormat = $"ddd {CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern}";
                }

                return _timeFormat;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Set(ref _timeFormat, $"ddd {CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern}");
                }
                else if (_timeFormat != value)
                {
                    Set(ref _timeFormat, value);
                }
            }
        }

        private string _dateFormat = "D";
        public string DateFormat
        {
            get => _dateFormat;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Set(ref _dateFormat, "D");
                }
                else if (_dateFormat != value)
                {
                    Set(ref _dateFormat, value);
                }
            }
        }

        private string _fileManager = "%WINDIR%\\explorer.exe";
        public string FileManager
        {
            get => _fileManager;
            set => Set(ref _fileManager, value);
        }

        private bool _forceSoftwareRendering;
        public bool ForceSoftwareRendering
        {
            get => _forceSoftwareRendering;
            set => Set(ref _forceSoftwareRendering, value);
        }

        private int _autoHideShowDelayMs = 250;
        public int AutoHideShowDelayMs
        {
            get => _autoHideShowDelayMs;
            set => Set(ref _autoHideShowDelayMs, value);
        }
        
        private LogSeverity _logSeverity = LogSeverity.Info;
        public LogSeverity LogSeverity
        {
            get => _logSeverity;
            set => SetEnum(ref _logSeverity, value);
        }
        #endregion

        #endregion
    }
}

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

        // the Setup check problems the user dismissed; it opens again at startup only when they change
        private string _setupCheckDismissed = "";
        public string SetupCheckDismissed
        {
            get => _setupCheckDismissed;
            set => Set(ref _setupCheckDismissed, value ?? "");
        }

        private string _theme = "Default";
        public string Theme
        {
            get => _theme;
            set => Set(ref _theme, value);
        }

        // image Cairo paints behind the desktop when it's the shell; empty uses the Windows wallpaper
        private string _desktopBackgroundPath = "";
        public string DesktopBackgroundPath
        {
            get => _desktopBackgroundPath;
            set => Set(ref _desktopBackgroundPath, value ?? "");
        }

        // Fill, Fit, Stretch, Center or Tile; applies to DesktopBackgroundPath only
        private string _desktopBackgroundStyle = "Fill";
        public string DesktopBackgroundStyle
        {
            get => _desktopBackgroundStyle;
            set => Set(ref _desktopBackgroundStyle, string.IsNullOrEmpty(value) ? "Fill" : value);
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

        #region Tiling
        private bool _enableTiling = true;
        public bool EnableTiling
        {
            get => _enableTiling;
            set => Set(ref _enableTiling, value);
        }

        /// <summary>
        /// Forces tiling on when Explorer is the shell, for development.
        /// </summary>
        private bool _enableTilingOutsideShell;
        public bool EnableTilingOutsideShell
        {
            get => _enableTilingOutsideShell;
            set => Set(ref _enableTilingOutsideShell, value);
        }

        // UWP frames on hidden tags are moved off screen instead of hidden (SW_HIDE), for apps that come back blank
        private bool _tilingParkUwpWindows = false;
        public bool TilingParkUwpWindows
        {
            get => _tilingParkUwpWindows;
            set => Set(ref _tilingParkUwpWindows, value);
        }

        private int _tilingGap = 6;
        public int TilingGap
        {
            get => _tilingGap;
            set => Set(ref _tilingGap, Math.Max(0, value));
        }

        private bool _tilingFollowActivation = true;
        public bool TilingFollowActivation
        {
            get => _tilingFollowActivation;
            set => Set(ref _tilingFollowActivation, value);
        }

        private string[] _tilingFloatClasses = { };
        public string[] TilingFloatClasses
        {
            get => _tilingFloatClasses;
            set => Set(ref _tilingFloatClasses, value ?? new string[0]);
        }

        private string[] _tilingFloatExes = { };
        public string[] TilingFloatExes
        {
            get => _tilingFloatExes;
            set => Set(ref _tilingFloatExes, value ?? new string[0]);
        }

        /// <summary>
        /// Base modifier for tiling hotkeys: "Win" or "Alt+Win".
        /// </summary>
        private string _tilingModifier = "Win";
        public string TilingModifier
        {
            get => _tilingModifier;
            set => Set(ref _tilingModifier, value);
        }

        private bool _enableFocusFollowsMouse = true;
        public bool EnableFocusFollowsMouse
        {
            get => _enableFocusFollowsMouse;
            set => Set(ref _enableFocusFollowsMouse, value);
        }

        private bool _focusFollowsMouseRaise;
        public bool FocusFollowsMouseRaise
        {
            get => _focusFollowsMouseRaise;
            set => Set(ref _focusFollowsMouseRaise, value);
        }

        private int _focusFollowsMouseDelayMs = 100;
        public int FocusFollowsMouseDelayMs
        {
            get => _focusFollowsMouseDelayMs;
            set => Set(ref _focusFollowsMouseDelayMs, Math.Max(0, value));
        }

        /// <summary>
        /// "Native" uses Windows' active window tracking; "Polling" uses a timer.
        /// </summary>
        private string _focusFollowsMouseMode = "Native";
        public string FocusFollowsMouseMode
        {
            get => _focusFollowsMouseMode;
            set => Set(ref _focusFollowsMouseMode, value);
        }

        private string _terminalCommand = "wt.exe";
        public string TerminalCommand
        {
            get => _terminalCommand;
            set => Set(ref _terminalCommand, value);
        }

        /// <summary>
        /// Takes Win+L away from Windows so it can be bound to tiling; locking moves to Win+P.
        /// </summary>
        private bool _remapWinL = true;
        public bool RemapWinL
        {
            get => _remapWinL;
            set => Set(ref _remapWinL, value);
        }

        /// <summary>
        /// User-defined Super key shortcuts. They take priority over the built-in tiling keys.
        /// </summary>
        private SuperKeyShortcut[] _superKeyShortcuts = SuperKeyShortcut.Defaults;
        public SuperKeyShortcut[] SuperKeyShortcuts
        {
            get => _superKeyShortcuts;
            set => Set(ref _superKeyShortcuts, value ?? new SuperKeyShortcut[0]);
        }

        // missing from settings saved before it existed, so those count as 0 and get every newer default
        private int _superKeyShortcutsVersion;
        public int SuperKeyShortcutsVersion
        {
            get => _superKeyShortcutsVersion;
            set => Set(ref _superKeyShortcutsVersion, value);
        }
        #endregion

        #region Advanced
        // the clock format of the runch quickshell bar
        private const string DefaultTimeFormat = "MM.dd.yy HH:mm:ss";

        private string _timeFormat;
        public string TimeFormat
        {
            get
            {
                // if Null or Empty, use the default
                if (string.IsNullOrEmpty(_timeFormat))
                {
                    _timeFormat = DefaultTimeFormat;
                }

                return _timeFormat;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Set(ref _timeFormat, DefaultTimeFormat);
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

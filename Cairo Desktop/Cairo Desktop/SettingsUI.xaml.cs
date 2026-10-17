using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using ManagedShell.Interop;
using CairoDesktop.SupportingClasses;
using ManagedShell;
using ManagedShell.WindowsTray;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CairoDesktop.Infrastructure.Services;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using ManagedShell.Common.Enums;
using CairoDesktop.Services;
using ManagedShell.AppBar;

namespace CairoDesktop
{
    /// <summary>
    /// Interaction logic for SettingsUI.xaml
    /// </summary>
    public partial class SettingsUI : Window
    {
        private readonly IAppGrabber _appGrabber;
        private readonly IApplicationUpdateService _applicationUpdateService;
        private readonly ICairoApplication _cairoApplication;
        private readonly ICommandService _commandService;
        private readonly ShellManager _shellManager;
        private readonly IThemeService _themeService;
        private readonly SettingsUIService _uiService;
        private readonly Settings _settings;

        internal SettingsUI(ICairoApplication cairoApplication,
            SettingsUIService uiService,
            ShellManagerService shellManagerService,
            IApplicationUpdateService applicationUpdateService,
            IAppGrabber appGrabber,
            IThemeService themeService,
            ICommandService commandService,
            Settings settings)
        {
            InitializeComponent();

            _appGrabber = appGrabber;
            _applicationUpdateService = applicationUpdateService;
            _cairoApplication = cairoApplication;
            _commandService = commandService;
            _shellManager = shellManagerService.ShellManager;
            _themeService = themeService;
            _uiService = uiService;
            _settings = settings;

            DataContext = _settings;

            loadThemes();
            loadLanguages();
            loadRadioGroups();
            loadHotKeys();
            loadLoggingLevels();
            loadNotificationSettings();
            loadVersionDependentSettings();

            checkUpdateConfig();
            checkRunAtLogOn();
            checkIfCanHibernate();

            _settings.PropertyChanged += Settings_PropertyChanged;
            _commandService = commandService;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MenuBarEdge":
                case "SysTrayAlwaysExpanded":
                    loadRadioGroups();
                    break;
            }
        }

        private void loadRadioGroups()
        {
            switch (_settings.MenuBarEdge)
            {
                case AppBarEdge.Top:
                    radMenuBarEdgeTop.IsChecked = true;
                    radMenuBarEdgeBottom.IsChecked = false;
                    break;
                case AppBarEdge.Bottom:
                    radMenuBarEdgeTop.IsChecked = false;
                    radMenuBarEdgeBottom.IsChecked = true;
                    break;
                default:
                    break;
            }

            switch (_settings.SysTrayAlwaysExpanded)
            {
                case false:
                    radTrayMode0.IsChecked = true;
                    radTrayMode1.IsChecked = false;
                    break;
                case true:
                    radTrayMode0.IsChecked = false;
                    radTrayMode1.IsChecked = true;
                    break;
            }
        }

        private void loadThemes()
        {
            foreach (var theme in _themeService.GetThemes())
            {
                cboThemeSelect.Items.Add(theme);
            }
        }

        private void loadLanguages()
        {
            cboLangSelect.DisplayMemberPath = "Key";
            cboLangSelect.SelectedValuePath = "Value";
            foreach (KeyValuePair<string, string> lang in Common.Localization.DisplayString.Languages)
            {
                cboLangSelect.Items.Add(lang);
            }
        }

        private void loadHotKeys()
        {
            string[] modifiers = { "Win", "Shift", "Alt", "Ctrl" };
            string[] keys = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

            cboCairoMenuHotKeyMod2.Items.Add("None");

            foreach (string modifier in modifiers)
            {
                cboCairoMenuHotKeyMod1.Items.Add(modifier);
                cboCairoMenuHotKeyMod2.Items.Add(modifier);
            }

            foreach (string key in keys)
            {
                cboCairoMenuHotKeyKey.Items.Add(key);
            }
        }

        private void loadLoggingLevels()
        {
            foreach (string sev in Enum.GetNames(typeof(LogSeverity)))
            {
                cboLogSeverity.Items.Add(sev);
            }
        }

        private void loadNotificationSettings()
        {
            if (_settings.EnableSysTray)
            {
                PinnedIcons.ItemsSource = _shellManager.NotificationArea.PinnedIcons;
                UnpinnedIcons.ItemsSource = _shellManager.NotificationArea.UnpinnedIcons;
            }
            else
            {
                pnlTraySettings.Visibility = Visibility.Collapsed;
            }
        }

        private void loadVersionDependentSettings()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                chkEnableMenuBarBlur.Visibility = Visibility.Collapsed;
                chkEnableMenuExtraActionCenter.Visibility = Visibility.Collapsed;
                chkEnableMenuExtraVolume.Visibility = Visibility.Collapsed;
            }
            else if (EnvironmentHelper.IsWindows10OrBetter && !EnvironmentHelper.IsAppRunningAsShell)
            {
                chkEnableMenuExtraVolume.Visibility = Visibility.Collapsed;
            }
            else if (EnvironmentHelper.IsWindows10OrBetter && EnvironmentHelper.IsAppRunningAsShell)
            {
                chkEnableMenuExtraActionCenter.Visibility = Visibility.Collapsed;
            }
        }


        #region Notification area icon drag and drop

        private Point? startPoint = null;
        private bool inDrag = false;

        // receive drop functions
        private void UnpinnedIcons_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(NotifyIcon)))
            {
                NotifyIcon dropData = e.Data.GetData(typeof(NotifyIcon)) as NotifyIcon;

                if (dropData.IsPinned) e.Effects = DragDropEffects.Move;
                else e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void PinnedIcons_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(NotifyIcon)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void UnpinnedIcons_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(NotifyIcon)))
            {
                NotifyIcon dropData = e.Data.GetData(typeof(NotifyIcon)) as NotifyIcon;

                if (dropData.IsPinned)
                {
                    dropData.Unpin();
                    _settings.PinnedNotifyIcons = _shellManager.NotificationArea.PinnedNotifyIcons;
                }
            }

            e.Handled = true;
        }

        private void PinnedIcons_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(NotifyIcon)))
            {
                NotifyIcon dropData = e.Data.GetData(typeof(NotifyIcon)) as NotifyIcon;

                dropData.Pin();
                _settings.PinnedNotifyIcons = _shellManager.NotificationArea.PinnedNotifyIcons;
            }

            e.Handled = true;
        }

        private void icon_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(NotifyIcon)))
            {
                Border border = sender as Border;
                NotifyIcon existingNotifyIcon = border.DataContext as NotifyIcon;
                NotifyIcon dropData = e.Data.GetData(typeof(NotifyIcon)) as NotifyIcon;

                // allow move if we are dragging into pinned items, out of pinned items, or we are rearranging inside pinned items
                if (dropData.IsPinned || existingNotifyIcon.IsPinned) e.Effects = DragDropEffects.Move;
                else e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void icon_Drop(object sender, DragEventArgs e)
        {
            Border border = sender as Border;
            NotifyIcon existingNotifyIcon = border.DataContext as NotifyIcon;

            if (e.Data.GetDataPresent(typeof(NotifyIcon)))
            {
                NotifyIcon dropData = e.Data.GetData(typeof(NotifyIcon)) as NotifyIcon;

                if (existingNotifyIcon.IsPinned)
                {
                    // pinned area; allow sorting
                    dropData.Pin(existingNotifyIcon.PinOrder);
                }
                else
                {
                    // non-pinned icons area
                    dropData.Unpin();
                }

                _settings.PinnedNotifyIcons = _shellManager.NotificationArea.PinnedNotifyIcons;
            }

            e.Handled = true;
        }

        // send drag functions
        private void icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Store the mouse position
            startPoint = e.GetPosition(this);
        }

        private void icon_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!inDrag && startPoint != null)
            {
                inDrag = true;

                Point mousePos = e.GetPosition(this);
                Vector diff = (Point)startPoint - mousePos;

                if (mousePos.Y <= ActualHeight && ((Point)startPoint).Y <= ActualHeight && e.LeftButton == MouseButtonState.Pressed && (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
                {
                    Border border = sender as Border;
                    NotifyIcon notifyIcon = border.DataContext as NotifyIcon;

                    try
                    {
                        DragDrop.DoDragDrop(border, notifyIcon, DragDropEffects.Move);
                    }
                    catch { }

                    // reset the stored mouse position
                    startPoint = null;
                }
                else if (e.LeftButton != MouseButtonState.Pressed)
                {
                    // reset the stored mouse position
                    startPoint = null;
                }

                inDrag = false;
            }

            e.Handled = true;
        }

        #endregion

        #region Startup checks
        private void checkUpdateConfig()
        {
            if (_applicationUpdateService.IsAvailable)
            {
                chkEnableAutoUpdates.IsChecked = _applicationUpdateService.AutomaticUpdatesEnabled;
            }
            else
            {
                chkEnableAutoUpdates.Visibility = Visibility.Collapsed;
            }
        }

        private void checkRunAtLogOn()
        {
            if (EnvironmentHelper.IsAppConfiguredAsShell.Equals(true))
            {
                chkRunAtLogOn.Visibility = Visibility.Collapsed;
            }

            try
            {
                RegistryKey rKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                List<string> rKeyValueNames = rKey?.GetValueNames().ToList();

                if (rKeyValueNames != null)
                {
                    if (rKeyValueNames.Contains("CairoShell"))
                    {
                        chkRunAtLogOn.IsChecked = true;
                    }
                    else
                    {
                        chkRunAtLogOn.IsChecked = false;
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Error($"SettingsWindow: Unable to load autorun setting from registry: {e.Message}");
            }
        }

        private void checkIfCanHibernate()
        {
            if (!PowerHelper.CanHibernate())
            {
                chkShowHibernate.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        private void EnableAutoUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (chkEnableAutoUpdates.IsChecked != null)
            {
                _applicationUpdateService.AutomaticUpdatesEnabled = (bool)chkEnableAutoUpdates.IsChecked;
            }
            else
            {
                _applicationUpdateService.AutomaticUpdatesEnabled = false;
            }
        }

        private void ShowRestartButton()
        {
            btnRestart.Visibility = Visibility.Visible;
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            ShowRestartButton();
        }

        private void DropDown_Changed(object sender, EventArgs e)
        {
            ShowRestartButton();
        }

        private void RestartCairo(object sender, RoutedEventArgs e)
        {
            saveChanges();

            _cairoApplication.RestartCairo();
        }

        /// <summary>
        /// Handles the Closing event of the window to save the settings.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Arguments for the event.</param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            saveChanges();
            _uiService.SettingsUi = null;
        }

        private void saveChanges()
        {
            // placeholder in case we need to do extra work in the future
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            NativeMethods.SetForegroundWindow(helper.Handle);
        }

        private void radMenuBarEdgeTop_Click(object sender, RoutedEventArgs e)
        {
            _settings.MenuBarEdge = AppBarEdge.Top;
        }

        private void radMenuBarEdgeBottom_Click(object sender, RoutedEventArgs e)
        {
            _settings.MenuBarEdge = AppBarEdge.Bottom;
        }

        private void radTrayMode0_Click(object sender, RoutedEventArgs e)
        {
            _settings.SysTrayAlwaysExpanded = false;
        }

        private void radTrayMode1_Click(object sender, RoutedEventArgs e)
        {
            _settings.SysTrayAlwaysExpanded = true;
        }

        private void cboCairoMenuHotKey_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboCairoMenuHotKeyMod1.SelectedValue == null || cboCairoMenuHotKeyMod2.SelectedValue == null || cboCairoMenuHotKeyKey.SelectedValue == null)
            {
                return;
            }
            string[] hotkey = { cboCairoMenuHotKeyMod1.SelectedValue.ToString(), cboCairoMenuHotKeyMod2.SelectedValue.ToString(), cboCairoMenuHotKeyKey.SelectedValue.ToString() };

            if (_settings.CairoMenuHotKey[0] == hotkey[0] && _settings.CairoMenuHotKey[1] == hotkey[1] && _settings.CairoMenuHotKey[2] == hotkey[2])
            {
                return;
            }
            _settings.CairoMenuHotKey = hotkey;
        }

        private void btnChangeShell_Click(object sender, RoutedEventArgs e)
        {
            btnChangeShell.IsEnabled = false;

            EnvironmentHelper.IsAppConfiguredAsShell = !EnvironmentHelper.IsAppConfiguredAsShell;

            CairoMessage.ShowOkCancel(Common.Localization.DisplayString.sSettings_Advanced_ShellChangedText,
                Common.Localization.DisplayString.sSettings_Advanced_ShellChanged, CairoMessageImage.LogOff,
                Common.Localization.DisplayString.sSettings_Advanced_LogOffNow, Common.Localization.DisplayString.sSettings_Advanced_LogOffLater,
                result =>
                {
                    if (result == true)
                        ShellHelper.Logoff();
                });
        }

        private void btnOpenLogsFolder_OnClick(object sender, RoutedEventArgs e)
        {
            _commandService.InvokeCommand("OpenLocationInWindow", ("Path", CairoApplication.LogsFolder));
        }

        private void chkRunAtLogOn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RegistryKey rKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                var chkBox = (CheckBox)sender;

                if (chkBox.IsChecked.Equals(false))
                {
                    //Delete SubKey
                    rKey.DeleteValue("CairoShell");
                }
                else
                {
                    string exePath = ExePath.GetExecutablePath();
                    //Write SubKey
                    rKey.SetValue("CairoShell", exePath);
                }
            }
            catch (Exception exception)
            {
                ShellLogger.Error($"SettingsWindow: Unable to update registry autorun setting: {exception.Message}");
            }
        }
    }
}
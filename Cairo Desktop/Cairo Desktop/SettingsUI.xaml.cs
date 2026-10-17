using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using ManagedShell.Interop;
using CairoDesktop.SupportingClasses;
using ManagedShell;
using ManagedShell.WindowsTray;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using CairoDesktop.Tiling;
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
        private readonly WorkstationLockManager _lockManager;
        private readonly TilingHotKeyService _hotKeyService;
        private readonly Settings _settings;

        internal SettingsUI(ICairoApplication cairoApplication,
            SettingsUIService uiService,
            ShellManagerService shellManagerService,
            IApplicationUpdateService applicationUpdateService,
            IAppGrabber appGrabber,
            IThemeService themeService,
            ICommandService commandService,
            WorkstationLockManager lockManager,
            TilingHotKeyService hotKeyService,
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
            _lockManager = lockManager;
            _hotKeyService = hotKeyService;
            _settings = settings;

            DataContext = _settings;

            loadThemes();
            loadLanguages();
            loadRadioGroups();
            loadHotKeys();
            loadLoggingLevels();
            loadTilingSettings();
            loadShortcutSettings();
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

        private void loadTilingSettings()
        {
            cboTilingModifier.Items.Add("Win");
            cboTilingModifier.Items.Add("Alt+Win");
            cboFocusFollowsMouseMode.Items.Add("Native");
            cboFocusFollowsMouseMode.Items.Add("Polling");

            updateRemapWinLUnavailable();
            _lockManager.StateChanged += LockManager_StateChanged;

            if (UIAccess.IsActive)
            {
                txtElevatedHelp.Text = DisplayString.sSettings_Tiling_ElevatedHelpUIAccess;
            }
        }

        private void LockManager_StateChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(updateRemapWinLUnavailable));
        }

        private void updateRemapWinLUnavailable()
        {
            pnlRemapWinLUnavailable.Visibility = _lockManager.IsUnavailable ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnRemapWinLFix_OnClick(object sender, RoutedEventArgs e)
        {
            LockPolicyPrompt.Show(_lockManager);
        }

        #region Shortcuts

        private static readonly string[] shortcutKeyNames = buildShortcutKeyNames();

        private static string[] buildShortcutKeyNames()
        {
            List<string> keys = new List<string>();
            for (char c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
            for (int i = 0; i <= 9; i++) keys.Add(i.ToString());
            for (int i = 1; i <= 12; i++) keys.Add("F" + i);
            keys.AddRange(new[] { "Enter", "Space", "Tab", "Back", "Escape", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down", "Pause", "OemComma", "OemPeriod" });
            return keys.ToArray();
        }

        private readonly ObservableCollection<ShortcutRow> shortcutRows = new ObservableCollection<ShortcutRow>();

        public string[] ShortcutKeys => shortcutKeyNames;

        public List<ShortcutAction> ShortcutActions { get; private set; }

        private void loadShortcutSettings()
        {
            // commands that take at most one value can be bound; that value comes from the row's Value box
            ShortcutActions = _commandService.Commands
                .Where(command => (command.Parameters?.Count(p => p.IsRequired) ?? 0) <= 1)
                .Select(command =>
                {
                    // same choice as the hotkey service: the first required parameter, else the first one
                    string valueHint = null;
                    bool valueRequired = false;
                    List<CairoCommandParameter> parameters = command.Parameters?.ToList() ?? new List<CairoCommandParameter>();
                    if (parameters.Count > 0)
                    {
                        CairoCommandParameter parameter = parameters.FirstOrDefault(p => p.IsRequired);
                        if (parameter.Name == null)
                        {
                            parameter = parameters[0];
                        }

                        valueHint = parameter.Description ?? parameter.Name;
                        valueRequired = parameter.IsRequired;
                    }

                    return new ShortcutAction
                    {
                        Id = command.Identifier,
                        Label = string.IsNullOrEmpty(command.Label) ? command.Identifier : command.Label,
                        ValueHint = valueHint,
                        ValueRequired = valueRequired
                    };
                })
                .OrderBy(action => action.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            loadShortcutRows();
            lstShortcuts.ItemsSource = shortcutRows;

            _hotKeyService.EntriesChanged += HotKeyService_EntriesChanged;
            updateShortcutStatus();
        }

        private void HotKeyService_EntriesChanged(object sender, EventArgs e)
        {
            updateShortcutStatus();
        }

        /// <summary>
        /// Shows what each shortcut and built-in key does right now, from the hotkey service's last registration.
        /// </summary>
        private void updateShortcutStatus()
        {
            string prefix = TilingKeymap.FormatModifiers(TilingKeymap.ParseBaseModifier(_settings.TilingModifier)).TrimEnd('+') + " +";
            List<BuiltInKeyRow> builtIns = new List<BuiltInKeyRow>();

            foreach (HotKeyEntry entry in _hotKeyService.Entries)
            {
                if (entry.IsShortcut)
                {
                    if (entry.ShortcutIndex < shortcutRows.Count)
                    {
                        ShortcutRow row = shortcutRows[entry.ShortcutIndex];
                        row.ModifierPrefix = prefix;
                        row.Status = getShortcutStatus(entry, row, out bool isLive);
                        row.IsLive = isLive;
                    }

                    continue;
                }

                builtIns.Add(new BuiltInKeyRow
                {
                    Combo = entry.Combo,
                    Action = describeCommand(entry.CommandId, entry.Parameters),
                    Status = getBuiltInStatus(entry),
                    IsLive = entry.IsLive
                });
            }

            lstBuiltInKeys.ItemsSource = builtIns;
        }

        private string getShortcutStatus(HotKeyEntry entry, ShortcutRow row, out bool isLive)
        {
            isLive = false;
            switch (entry.State)
            {
                case HotKeyState.Incomplete:
                    return DisplayString.sShortcutState_Incomplete;
                case HotKeyState.Duplicate:
                    return DisplayString.sShortcutState_Duplicate;
                case HotKeyState.LockKey:
                    return DisplayString.sShortcutState_LockKey;
                case HotKeyState.Inactive:
                    return DisplayString.sShortcutState_Inactive;
            }

            if (row.IsMissingValue)
            {
                return string.Format(DisplayString.sShortcutState_NeedsValue, row.ValueHint);
            }

            isLive = true;
            string status = entry.State == HotKeyState.Hooked ? DisplayString.sShortcutState_Hooked : DisplayString.sShortcutState_Active;
            if (entry.Replaces.Count > 0)
            {
                status += " · " + string.Format(DisplayString.sShortcutState_Replaces,
                    string.Join(", ", entry.Replaces.Select(replaced => describeCommand(replaced.CommandId, replaced.Parameters))));
            }

            return status;
        }

        private string getBuiltInStatus(HotKeyEntry entry)
        {
            switch (entry.State)
            {
                case HotKeyState.Active:
                    return DisplayString.sShortcutState_Active;
                case HotKeyState.Hooked:
                    return DisplayString.sShortcutState_Hooked;
                case HotKeyState.Overridden:
                    return DisplayString.sShortcutState_Overridden;
                case HotKeyState.LockKey:
                    return DisplayString.sShortcutState_LockKey;
                default:
                    return entry.Binding?.Scope == KeyBindingScope.Shell ? DisplayString.sShortcutState_InactiveShell
                        : entry.Binding?.Scope == KeyBindingScope.Tiling ? DisplayString.sShortcutState_InactiveTiling
                        : DisplayString.sShortcutState_Inactive;
            }
        }

        /// <summary>The command's label, plus its parameter values, e.g. "View tag (3)".</summary>
        private string describeCommand(string commandId, (string name, object value)[] parameters)
        {
            string label = ShortcutActions.FirstOrDefault(action => action.Id == commandId)?.Label
                ?? _commandService.Commands.FirstOrDefault(command => command.Identifier == commandId)?.Label
                ?? commandId;

            List<string> values = new List<string>();
            foreach (var parameter in parameters ?? new (string, object)[0])
            {
                switch (parameter.name)
                {
                    case "Index" when parameter.value is int index:
                        // tags are numbered from 1 on screen
                        values.Add((index + 1).ToString());
                        break;
                    case "Next" when parameter.value is bool next:
                        if (!next) values.Add("previous");
                        break;
                    default:
                        if (parameter.value != null) values.Add(parameter.value.ToString());
                        break;
                }
            }

            return values.Count > 0 ? $"{label} ({string.Join(", ", values)})" : label;
        }

        private void loadShortcutRows()
        {
            foreach (ShortcutRow row in shortcutRows)
            {
                row.Changed -= ShortcutRow_Changed;
            }

            shortcutRows.Clear();
            foreach (SuperKeyShortcut shortcut in _settings.SuperKeyShortcuts)
            {
                addShortcutRow(shortcut);
            }
        }

        private void addShortcutRow(SuperKeyShortcut shortcut)
        {
            ShortcutRow row = new ShortcutRow(shortcut, id => ShortcutActions.FirstOrDefault(action => action.Id == id));
            row.Changed += ShortcutRow_Changed;
            shortcutRows.Add(row);
        }

        private void ShortcutRow_Changed(object sender, EventArgs e)
        {
            saveShortcuts();
        }

        private void saveShortcuts()
        {
            _settings.SuperKeyShortcuts = shortcutRows.Select(row => row.ToShortcut()).ToArray();
        }

        private void btnAddShortcut_OnClick(object sender, RoutedEventArgs e)
        {
            // incomplete until a key and action are chosen; incomplete shortcuts are skipped when registering
            addShortcutRow(new SuperKeyShortcut());
            saveShortcuts();
        }

        private void btnRemoveShortcut_OnClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ShortcutRow row)
            {
                row.Changed -= ShortcutRow_Changed;
                shortcutRows.Remove(row);
                saveShortcuts();
            }
        }

        private void btnResetShortcuts_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.SuperKeyShortcuts = SuperKeyShortcut.Defaults;
            loadShortcutRows();
            // the status update from the settings change ran against the old rows
            updateShortcutStatus();
        }

        #endregion

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
            _lockManager.StateChanged -= LockManager_StateChanged;
            _hotKeyService.EntriesChanged -= HotKeyService_EntriesChanged;
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
using CairoDesktop.AppGrabber;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common;
using ManagedShell;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CairoDesktop.Infrastructure.ObjectModel;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using ManagedShell.Common.Enums;
using NativeMethods = ManagedShell.Interop.NativeMethods;
using System.Windows.Threading;
using ManagedShell.ShellFolders;
using Microsoft.Extensions.Hosting;
using CairoDesktop.Infrastructure.Services;
using System.Collections.Generic;
using System.Windows.Data;
using CairoDesktop.Common.Localization;
using CairoDesktop.Tiling;
using CairoDesktop.Tiling.ViewModels;

namespace CairoDesktop.MenuBar
{
    public partial class MenuBar : CairoAppBarWindow, IMenuBar
    {
        private readonly IApplicationUpdateService _applicationUpdateService;
        private readonly ISettingsUIService _settingsUiService;
        internal readonly ICommandService _commandService;
        private readonly Settings _settings;
        private readonly AppBarEventService _appBarEventService;
        private readonly ITilingManager _tilingManager;
        private readonly Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler> _commandPropertyChangedHandlers = new Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler>();
        private readonly Dictionary<UserControlMenuBarExtension, UserControl> _loadedMenuExtras = new Dictionary<UserControlMenuBarExtension, UserControl>();

        private bool isCairoMenuInitialized;
        private MenuBarShadow shadow;

        private static readonly int[] GapChoices = { 0, 4, 6, 10, 16 };

        public MenuBar(ICairoApplication cairoApplication, AppBarEventService appBarEventService, ShellManager shellManager, IWindowManager windowManager, IApplicationUpdateService applicationUpdateService, ISettingsUIService settingsUiService, ICommandService commandService, ITilingManager tilingManager, Settings settings, AppBarScreen screen, AppBarEdge edge, AppBarMode mode) : base(cairoApplication, shellManager, windowManager, screen, edge, mode, 26)
        {
            _appBarEventService = appBarEventService;
            _applicationUpdateService = applicationUpdateService;
            _settingsUiService = settingsUiService;
            _commandService = commandService;
            _tilingManager = tilingManager;
            _settings = settings;

            object menuBarWindowAllowsTransparencyResource = TryFindResource("MenuBarWindowAllowsTransparency");
            if (menuBarWindowAllowsTransparencyResource is bool resourceValue)
                AllowsTransparency = resourceValue;

            InitializeComponent();

            AutoHideShowDelayMs = _settings.AutoHideShowDelayMs;
            RequiresScreenEdge = true;

            setupChildren();

            _settings.PropertyChanged += Settings_PropertyChanged;
            _tilingManager.EnabledChanged += TilingManager_EnabledChanged;

            // Set element that should be transformed for auto-hide
            AutoHideElement = MenuBarWindow;
        }

        private void setupChildren()
        {
            // screens are matched by device name, since AppBarScreen instances are recreated on display changes
            ScreenTagsViewModel model = _tilingManager.GetScreenModel(Screen);
            tagBar.DataContext = model;
            windowList.DataContext = model;

            // hidden while tiling is off
            BooleanToVisibilityConverter converter = new BooleanToVisibilityConverter();
            tagBar.SetBinding(VisibilityProperty, new Binding(nameof(ScreenTagsViewModel.IsActive)) { Converter = converter, FallbackValue = Visibility.Collapsed });
            windowList.SetBinding(VisibilityProperty, new Binding(nameof(ScreenTagsViewModel.IsActive)) { Converter = converter, FallbackValue = Visibility.Collapsed });
        }

        private void TilingManager_EnabledChanged(object sender, EventArgs e)
        {
            setupChildren();
            ClearCommandHandlers();
        }

        private void SetupMenuBarExtensions()
        {
            foreach (var userControlMenuBarExtension in _cairoApplication.MenuBarExtensions.OfType<UserControlMenuBarExtension>())
            {
                try
                {
                    var menuExtra = userControlMenuBarExtension.StartControl(this);
                    if (menuExtra != null)
                    {
                        MenuExtrasHost.Children.Add(menuExtra);
                        _loadedMenuExtras.Add(userControlMenuBarExtension, menuExtra);
                    }
                }
                catch (Exception ex)
                {
                    ShellLogger.Error($"Unable to start UserControl menu bar extension due to exception in {ex.TargetSite?.Module}: {ex.Message}");
                }
            }
        }

        private void StopMenuBarExtensions()
        {
            MenuExtrasHost.Children.Clear();

            foreach (var extra in _loadedMenuExtras)
            {
                try
                {
                    extra.Key.StopControl(this, extra.Value);
                }
                catch (Exception ex)
                {
                    ShellLogger.Error($"Unable to stop UserControl menu bar extension due to exception in {ex.TargetSite?.Module}: {ex.Message}");
                }
            }

            _loadedMenuExtras.Clear();
        }

        private void ClearCommandHandlers()
        {
            foreach (var handler in _commandPropertyChangedHandlers)
            {
                handler.Key.PropertyChanged -= handler.Value;
            }

            _commandPropertyChangedHandlers.Clear();
            isCairoMenuInitialized = false;
        }

        private void CairoMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (isCairoMenuInitialized)
            {
                return;
            }

            CairoMenu.Items.Clear();

            // TODO: Make configurable
            var cairoMenuItems = new string[]
            {
                "AboutCairo",
                "CheckForUpdates",
                "",
                "OpenCairoSettings",
                WindowManagerMenuIdentifier,
                "OpenControlPanel",
                "OpenWindowsSettings",
                "",
                "ShowRunDialog",
                "StartTaskManager",
                "",
                "ExitCairo",
                "",
                "Sleep",
                "Hibernate",
                "Restart",
                "ShutDown",
                "",
                "Lock",
                "LogOff"
            };

            foreach (var identifier in cairoMenuItems)
            {
                if (string.IsNullOrEmpty(identifier))
                {
                    CairoMenu.Items.Add(new Separator());
                    continue;
                }

                if (identifier == WindowManagerMenuIdentifier)
                {
                    CairoMenu.Items.Add(BuildWindowManagerMenu());
                    continue;
                }

                var command = _commandService.Commands.FirstOrDefault(cmd => cmd.Identifier == identifier);
                if (command == null)
                {
                    continue;
                }
                var menuItem = new MenuItem
                {
                    Header = command.Label,
                    Tag = command.Identifier,
                    Visibility = command.IsAvailable ? Visibility.Visible : Visibility.Collapsed
                };
                menuItem.Click += (_sender, _e) => _commandService.InvokeCommand(command.Identifier);
                if (command is INotifyPropertyChanged notifyPropertyChangedCommand)
                {
                    PropertyChangedEventHandler changeHandler = (_sender, _e) =>
                    {
                        if (_e.PropertyName == "Label")
                        {
                            menuItem.Header = command.Label;
                        }
                        if (_e.PropertyName == "IsAvailable")
                        {
                            menuItem.Visibility = command.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
                            DeduplicateSeparators();
                        }
                    };
                    notifyPropertyChangedCommand.PropertyChanged += changeHandler;
                    _commandPropertyChangedHandlers.Add(notifyPropertyChangedCommand, changeHandler);
                }
                CairoMenu.Items.Add(menuItem);
            }

            // Add CairoMenu MenuItems
            // TODO: Extensions should instead define Commands, and the user can add them to the Cairo menu
            if (_cairoApplication.CairoMenu.Count > 0)
            {
                var anchor = CairoMenu.Items.OfType<MenuItem>().FirstOrDefault(item => (item.Tag as string) == "OpenWindowsSettings");
                var insertIndex = anchor != null ? CairoMenu.Items.IndexOf(anchor) + 1 : CairoMenu.Items.Count;
                CairoMenu.Items.Insert(insertIndex, new Separator());
                foreach (var cairoMenuItem in _cairoApplication.CairoMenu)
                {
                    insertIndex += 1;
                    var menuItem = new MenuItem { Header = cairoMenuItem.Header };
                    menuItem.Click += cairoMenuItem.MenuItem_Click;
                    CairoMenu.Items.Insert(insertIndex, menuItem);
                }
            }

            DeduplicateSeparators();

            isCairoMenuInitialized = true;
        }

        private const string WindowManagerMenuIdentifier = "|WindowManagerMenu|";

        /// <summary>
        /// Cairo menu ▸ Window Manager: tiling toggles bound to settings, gaps, and shortcuts to related commands.
        /// </summary>
        private MenuItem BuildWindowManagerMenu()
        {
            MenuItem root = new MenuItem { Header = DisplayString.sWindowManager };

            root.Items.Add(CreateSettingToggle(DisplayString.sWindowManager_EnableTiling, nameof(Settings.EnableTiling)));
            root.Items.Add(CreateSettingToggle(DisplayString.sWindowManager_FocusFollowsMouse, nameof(Settings.EnableFocusFollowsMouse)));
            root.Items.Add(CreateSettingToggle(DisplayString.sWindowManager_RaiseOnFocus, nameof(Settings.FocusFollowsMouseRaise)));
            root.Items.Add(CreateSettingToggle(DisplayString.sWindowManager_FollowActivation, nameof(Settings.TilingFollowActivation)));

            MenuItem gaps = new MenuItem { Header = DisplayString.sWindowManager_Gaps };
            foreach (int gap in GapChoices)
            {
                MenuItem gapItem = new MenuItem { Header = gap.ToString(), IsCheckable = true };
                gapItem.Click += (_sender, _e) => _settings.TilingGap = gap;
                gaps.Items.Add(gapItem);
            }
            gaps.SubmenuOpened += (_sender, _e) =>
            {
                foreach (MenuItem gapItem in gaps.Items.OfType<MenuItem>())
                {
                    gapItem.IsChecked = gapItem.Header as string == _settings.TilingGap.ToString();
                }
            };
            root.Items.Add(gaps);

            root.Items.Add(new Separator());
            root.Items.Add(CreateCommandItem("TilingEqualize"));
            root.Items.Add(CreateCommandItem("DisplayProjection"));
            root.Items.Add(new Separator());

            MenuItem settingsItem = new MenuItem { Header = DisplayString.sWindowManager_Settings };
            settingsItem.Click += (_sender, _e) => _settingsUiService.Show("tiling");
            root.Items.Add(settingsItem);

            return root;
        }

        private MenuItem CreateSettingToggle(string header, string settingName)
        {
            MenuItem item = new MenuItem { Header = header, IsCheckable = true };
            item.SetBinding(MenuItem.IsCheckedProperty, new Binding(settingName) { Source = _settings, Mode = BindingMode.TwoWay });
            return item;
        }

        private MenuItem CreateCommandItem(string identifier)
        {
            var command = _commandService.Commands.FirstOrDefault(cmd => cmd.Identifier == identifier);
            MenuItem item = new MenuItem { Header = command?.Label ?? identifier, IsEnabled = command?.IsAvailable == true };
            item.Click += (_sender, _e) => _commandService.InvokeCommand(identifier);

            if (command is INotifyPropertyChanged notifyPropertyChangedCommand)
            {
                PropertyChangedEventHandler changeHandler = (_sender, _e) =>
                {
                    if (_e.PropertyName == "IsAvailable")
                    {
                        item.IsEnabled = command.IsAvailable;
                    }
                };
                notifyPropertyChangedCommand.PropertyChanged += changeHandler;
                _commandPropertyChangedHandlers[notifyPropertyChangedCommand] = changeHandler;
            }

            return item;
        }

        private void DeduplicateSeparators()
        {
            Type previousType = null;
            foreach (UIElement item in CairoMenu.Items)
            {
                Type currentType = item.GetType();
                if (previousType == typeof(Separator) && currentType == typeof(Separator))
                {
                    ((Separator)item).Visibility = Visibility.Collapsed;
                }
                else if (currentType == typeof(Separator))
                {
                    ((Separator)item).Visibility = Visibility.Visible;
                }

                if (item.Visibility == Visibility.Visible)
                {
                    previousType = currentType;
                }
            }
        }

        private void setupShadow()
        {
            if (_settings.EnableMenuBarShadow && shadow == null && AppBarMode != AppBarMode.AutoHide)
            {
                shadow = new MenuBarShadow(_cairoApplication, _windowManager, this);
                shadow.Show();
            }
        }

        protected override void OnSourceInitialized(object sender, EventArgs e)
        {
            base.OnSourceInitialized(sender, e);
            
            SetupMenuBarExtensions();

            SetBlur(_settings.EnableMenuBarBlur);

            setupShadow();
        }

        #region Events

        public override bool UpdatePosition()
        {
            var changed = base.UpdatePosition();

            if (changed)
            {
                setShadowPosition();
            }

            return changed;
        }

        private void setShadowPosition()
        {
            shadow?.SetPosition();
        }

        private void closeShadow()
        {
            if (shadow != null && !shadow.IsClosing)
            {
                shadow.AllowClose = true;
                shadow.Close();
                shadow = null;
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            setShadowPosition();
        }

        protected override void CustomClosing()
        {
            if (!_windowManager.IsSettingDisplays && !AllowClose)
            {
                return;
            }

            closeShadow();
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _tilingManager.EnabledChanged -= TilingManager_EnabledChanged;
            StopMenuBarExtensions();
            ClearCommandHandlers();
        }

        private void UnAutoHideBeforeAction(Action action)
        {
            if (AppBarMode == AppBarMode.AutoHide && AllowAutoHide)
            {
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(AutoHideShowAnimationMs);
                timer.Tick += (object sender, EventArgs e) =>
                {
                    action();
                    timer.Stop();
                };

                PeekDuringAutoHide();
                timer.Start();
            }
            else
            {
                action();
            }
        }

        internal void ToggleCairoMenu()
        {
            if (!CairoMenu.IsSubmenuOpen)
            {
                NativeMethods.SetForegroundWindow(Handle);
                UnAutoHideBeforeAction(() => CairoMenu.IsSubmenuOpen = true);
            }
            else
            {
                CairoMenu.IsSubmenuOpen = false;
            }
        }

        private void MenuBar_OnMouseEnter(object sender, MouseEventArgs e)
        {
            _appBarEventService.NotifyAppBarEvent(this, AppBarEventReason.MouseEnter);
        }

        private void MenuBar_OnMouseLeave(object sender, MouseEventArgs e)
        {
            _appBarEventService.NotifyAppBarEvent(this, AppBarEventReason.MouseLeave);
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e != null && !string.IsNullOrWhiteSpace(e.PropertyName))
            {
                switch (e.PropertyName)
                {
                    case "EnableMenuBarBlur":
                        SetBlur(_settings.EnableMenuBarBlur);
                        break;
                    case "EnableMenuBarShadow":
                        if (_settings.EnableMenuBarShadow)
                        {
                            setupShadow();
                        }
                        else
                        {
                            closeShadow();
                        }
                        break;
                    case "EnableMenuBarAutoHide":
                        AppBarMode = _settings.EnableMenuBarAutoHide ? AppBarMode.AutoHide : AppBarMode.Normal;
                        if (AppBarMode == AppBarMode.AutoHide)
                        {
                            closeShadow();
                        }
                        else
                        {
                            setupShadow();
                        }
                        break;
                    case "AutoHideShowDelayMs":
                        AutoHideShowDelayMs = _settings.AutoHideShowDelayMs;
                        break;
                    case "Theme":
                        PeekDuringAutoHide();
                        break;
                    case "MenuBarEdge":
                        PeekDuringAutoHide();
                        AppBarEdge = _settings.MenuBarEdge;
                        UpdatePosition();
                        if (EnvironmentHelper.IsAppRunningAsShell) _appBarManager.SetWorkArea(Screen);
                        break;
                    case nameof(_settings.EnableMenuExtraActionCenter):
                    case nameof(_settings.EnableMenuExtraClock):
                    case nameof(_settings.EnableMenuExtraSearch):
                    case nameof(_settings.EnableMenuExtraVolume):
                        StopMenuBarExtensions();
                        SetupMenuBarExtensions();
                        break;
                }
            }
        }

        #endregion
        
        #region IMenuExtraHost
        public IntPtr GetHandle()
        {
            return Handle;
        }

        public bool GetIsPrimaryDisplay()
        {
            return Screen.Primary;
        }

        public MenuBarDimensions GetDimensions()
        {
            return new MenuBarDimensions
            {
                ScreenEdge = (int)AppBarEdge,
                DpiScale = DpiScale,
                Height = Height,
                Width = Width,
                Left = Left,
                Top = Top
            };
        }
        #endregion

        #region IMenuBar
        void IMenuBar.PeekDuringAutoHide(int msToPeek)
        {
            PeekDuringAutoHide(msToPeek);
        }
        #endregion
    }
}
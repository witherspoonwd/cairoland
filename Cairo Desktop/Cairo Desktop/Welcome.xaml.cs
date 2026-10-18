using System;
using System.Collections.Generic;
using System.Windows;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using CairoDesktop.Services;
using ManagedShell.Common.Helpers;

namespace CairoDesktop
{
    /// <summary>
    /// Interaction logic for Welcome.xaml
    /// </summary>
    public partial class Welcome : Window
    {
        private readonly ICairoApplication _cairoApplication;
        private readonly Settings _settings;
        private readonly SetupCheckService _setupCheck;
        private List<SetupCheckItem> _setupItems;

        const int maxSize = 780;
        string _initialLanguage = "en_US";

        /// <summary>
        /// The first-run tour, ending with the Setup check. With <paramref name="setupCheckOnly"/>, just the
        /// Setup check (at startup when it found a problem, or from Settings).
        /// </summary>
        public Welcome(ICairoApplication cairoApplication, Settings settings, SetupCheckService setupCheck, bool setupCheckOnly = false,
            List<SetupCheckItem> setupItems = null)
        {
            _cairoApplication = cairoApplication;
            _settings = settings;
            _setupCheck = setupCheck;

            InitializeComponent();

            cboLangSelect.DataContext = _settings;

            SetSizeAndLocation();

            LoadLanguages();

            if (setupCheckOnly)
            {
                bdrWelcome.Visibility = Visibility.Collapsed;
                ShowSetupCheck(setupItems);
            }
        }

        /// <summary>Opens the Setup check by itself, or brings an open one forward.</summary>
        public static void ShowSetupCheck(ICairoApplication cairoApplication, Settings settings, SetupCheckService setupCheck,
            List<SetupCheckItem> setupItems = null)
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is Welcome open)
                {
                    open.ShowSetupCheck(setupItems);
                    open.Activate();
                    return;
                }
            }

            new Welcome(cairoApplication, settings, setupCheck, true, setupItems).Show();
        }

        private void SetSizeAndLocation()
        {
            SetSize();
            SetLocation();
        }

        private void SetSize()
        {
            double screenHeight = ScreenHelper.PrimaryMonitorDeviceSize.Height / DpiHelper.DpiScale;
            double size = screenHeight - 100;
            if (size >= maxSize)
            {
                Height = maxSize;
            }
            else
            {
                Height = size;
            }

            MaxHeight = screenHeight;
        }

        private void SetLocation()
        {
            var screenSize = ScreenHelper.PrimaryMonitorDeviceSize;
            Left = ((screenSize.Width / DpiHelper.DpiScale) - Width) / 2;
            Top = ((screenSize.Height / DpiHelper.DpiScale) - Height) / 2;

            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        private void LoadLanguages()
        {
            _initialLanguage = _settings.Language;

            cboLangSelect.DisplayMemberPath = "Key";
            cboLangSelect.SelectedValuePath = "Value";

            foreach (KeyValuePair<string, string> lang in DisplayString.Languages)
            {
                cboLangSelect.Items.Add(lang);
            }

            cboLangSelect.SelectedValue = _settings.Language;
        }

        private void btnGoPage2_Click(object sender, RoutedEventArgs e)
        {
            bdrWelcome.Visibility = Visibility.Collapsed;
            bdrMenuBar.Visibility = Visibility.Visible;
        }

        private void btnFinish_Click(object sender, RoutedEventArgs e)
        {
            _settings.IsFirstRun = false;
            ShowSetupCheck(null);
        }

        #region Setup check

        private void ShowSetupCheck(List<SetupCheckItem> items)
        {
            bdrWelcome.Visibility = Visibility.Collapsed;
            bdrMenuBar.Visibility = Visibility.Collapsed;
            bdrSetupCheck.Visibility = Visibility.Visible;

            if (items != null)
            {
                SetSetupItems(items);
            }
            else
            {
                RunSetupCheck();
            }
        }

        private async void RunSetupCheck()
        {
            txtSetupCheckRunning.Visibility = Visibility.Visible;
            btnCheckAgain.IsEnabled = false;
            lstSetupCheck.ItemsSource = null;

            SetSetupItems(await _setupCheck.RunAsync());
        }

        private void SetSetupItems(List<SetupCheckItem> items)
        {
            _setupItems = items;
            lstSetupCheck.ItemsSource = items;
            txtSetupCheckRunning.Visibility = Visibility.Collapsed;
            btnCheckAgain.IsEnabled = true;
        }

        private async void btnSetupFix_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is SetupCheckItem item) || item.Fix == null)
            {
                return;
            }

            element.IsEnabled = false;
            bool fixedIt = await item.Fix();
            element.IsEnabled = true;

            if (fixedIt)
            {
                RunSetupCheck();
            }
        }

        private void btnCheckAgain_Click(object sender, RoutedEventArgs e)
        {
            RunSetupCheck();
        }

        private void btnSetupDone_Click(object sender, RoutedEventArgs e)
        {
            // don't open again at startup until something else needs attention
            if (_setupItems != null)
            {
                _setupCheck.Dismiss(_setupItems);
            }

            Close();
        }

        #endregion

        private void cboLangSelect_DropDownClosed(object sender, EventArgs e)
        {
            if (_settings.Language != _initialLanguage)
            {
                CairoMessage.Show(DisplayString.sWelcome_ChangingLanguageText, DisplayString.sWelcome_ChangingLanguage, MessageBoxButton.OK, CairoMessageImage.Information,
                    result =>
                    {
                        if (result == true)
                        {
                            _cairoApplication?.RestartCairo();
                        }
                    });
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
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

        const int maxSize = 780;
        string _initialLanguage = "en_US";

        public Welcome(ICairoApplication cairoApplication, Settings settings)
        {
            _cairoApplication = cairoApplication;
            _settings = settings;

            InitializeComponent();

            cboLangSelect.DataContext = _settings;

            SetSizeAndLocation();

            LoadLanguages();
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
            Close();
        }

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

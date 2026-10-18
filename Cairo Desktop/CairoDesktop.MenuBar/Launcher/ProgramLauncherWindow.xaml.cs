using CairoDesktop.AppGrabber;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using CairoDesktop.Interop;
using CairoDesktop.Tiling;
using CairoDesktop.Tiling.Commands;
using CairoDesktop.Tiling.Core;
using ManagedShell.AppBar;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CairoDesktop.MenuBar.Launcher
{
    public class LauncherResult
    {
        public LauncherEntry Entry { get; }

        public List<TextSegment> NameSegments { get; }

        public LauncherResult(LauncherEntry entry, string query)
        {
            Entry = entry;
            NameSegments = TextSegment.Split(entry.Name, FuzzyMatcher.MatchPositions(query ?? string.Empty, entry.Name));
        }
    }

    /// <summary>
    /// Keyboard-driven fuzzy launcher for installed programs.
    /// </summary>
    public partial class ProgramLauncherWindow : Window
    {
        private const int MaxResults = 10;
        private const int TopMarginDip = 8;
        private const string RunPrefix = "/run";

        private readonly ProgramIndex _index;
        private readonly LaunchHistory _history;
        private readonly IAppGrabber _appGrabber;
        private readonly Settings _settings;
        private readonly ITilingManager _tilingManager;
        private User32.RECT _workArea;
        private bool _isClosing;
        private IDisposable _focusFollowsMouseSuspension;

        public ProgramLauncherWindow(ProgramIndex index, LaunchHistory history, IAppGrabber appGrabber, Settings settings, ITilingManager tilingManager)
        {
            _index = index;
            _history = history;
            _appGrabber = appGrabber;
            _settings = settings;
            _tilingManager = tilingManager;

            InitializeComponent();

            _index.Updated += Index_Updated;
            UpdateResults();
        }

        /// <summary>
        /// Shows the launcher on the monitor with <paramref name="workArea"/> and takes keyboard focus.
        /// </summary>
        public void ShowOn(User32.RECT workArea)
        {
            _workArea = workArea;
            _index.RefreshIfStale();

            // the launcher closes when deactivated, and it resizes as results change, which can leave the cursor
            // over another window that focus follows mouse would then activate
            _focusFollowsMouseSuspension = _tilingManager.SuspendFocusFollowsMouse();

            Show();
            Reposition();

            IntPtr handle = new WindowInteropHelper(this).Handle;
            using (Process process = Process.GetCurrentProcess())
            {
                User32.AllowSetForegroundWindow(process.Id);
            }

            // opened from a hotkey, so use the same foreground-lock workaround as the tiling manager
            WindowArranger.Focus(handle);
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }

        private void Reposition()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || !User32.GetWindowRect(handle, out User32.RECT rect))
            {
                return;
            }

            double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            int margin = (int)Math.Round(TopMarginDip * scale);
            int x = _workArea.Left + (_workArea.Width - rect.Width) / 2;

            // just below the menu bar, which sits at the top of the screen unless moved to the bottom
            int y = _settings.MenuBarEdge == AppBarEdge.Bottom
                ? _workArea.Bottom - rect.Height - margin
                : _workArea.Top + margin;

            User32.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }

        private void Index_Updated(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateResults));
        }

        /// <summary>
        /// Returns whether the search box holds a "/run COMMAND" line, giving the command (possibly empty) to run.
        /// </summary>
        private bool TryGetRunCommand(out string command)
        {
            string text = SearchBox.Text.TrimStart();
            bool isRun = text.StartsWith(RunPrefix, StringComparison.OrdinalIgnoreCase)
                && (text.Length == RunPrefix.Length || char.IsWhiteSpace(text[RunPrefix.Length]));

            command = isRun ? text.Substring(RunPrefix.Length).Trim() : null;
            return isRun;
        }

        private void UpdateResults()
        {
            if (TryGetRunCommand(out string command))
            {
                ResultsList.ItemsSource = null;
                ResultsList.Visibility = Visibility.Collapsed;
                NoResultsText.Visibility = Visibility.Collapsed;
                ShowRunCommandText(command.Length == 0 ? DisplayString.sLauncher_RunPrompt : string.Format(DisplayString.sLauncher_RunCommand, command), dimmed: command.Length == 0);
                return;
            }

            RunCommandText.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = Visibility.Visible;

            string query = SearchBox.Text;
            List<LauncherEntry> ranked = FuzzyMatcher.Rank(query, _index.Entries, entry => entry.Name, entry => entry.ExeName, entry => _history.Frecency(entry.Key));

            ResultsList.ItemsSource = ranked.Take(MaxResults).Select(entry => new LauncherResult(entry, query)).ToList();
            ResultsList.SelectedIndex = ResultsList.Items.Count > 0 ? 0 : -1;
            NoResultsText.Visibility = ResultsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateResults();
        }

        private void MoveSelection(int delta)
        {
            int count = ResultsList.Items.Count;
            if (count == 0)
            {
                return;
            }

            ResultsList.SelectedIndex = (ResultsList.SelectedIndex + delta + count) % count;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }

        private void ShowRunCommandText(string text, bool dimmed)
        {
            RunCommandText.Text = text;
            RunCommandText.Opacity = dimmed ? 0.55 : 1.0;
            RunCommandText.Visibility = Visibility.Visible;
        }

        private void RunCommand(string command, bool asAdmin)
        {
            if (command.Length == 0)
            {
                return;
            }

            // run before closing so a failure can be reported here; elevation prompts deactivate and close the launcher anyway
            if (RunCommandCommand.RunCommandLine(command, asAdmin))
            {
                CloseLauncher();
            }
            else if (!_isClosing)
            {
                ShowRunCommandText(string.Format(DisplayString.sLauncher_RunFailed, command), dimmed: true);
            }
        }

        private void Launch(bool asAdmin)
        {
            if (TryGetRunCommand(out string command))
            {
                RunCommand(command, asAdmin);
                return;
            }

            if (!(ResultsList.SelectedItem is LauncherResult result))
            {
                return;
            }

            CloseLauncher();
            _history.RecordLaunch(result.Entry.Key);

            if (asAdmin)
            {
                _appGrabber.LaunchProgramAdmin(result.Entry.App);
            }
            else
            {
                _appGrabber.LaunchProgram(result.Entry.App);
            }
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            switch (e.Key)
            {
                case Key.Escape:
                    CloseLauncher();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Launch(asAdmin: ctrl);
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    MoveSelection(shift ? -1 : 1);
                    e.Handled = true;
                    break;
                case Key.J when ctrl:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.K when ctrl:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Launch(asAdmin: false);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            CloseLauncher();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Reposition();
        }

        public void CloseLauncher()
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            _index.Updated -= Index_Updated;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // also reached by Alt+F4 and other closes that skip CloseLauncher
            _index.Updated -= Index_Updated;
            _focusFollowsMouseSuspension?.Dispose();
            _focusFollowsMouseSuspension = null;
            base.OnClosed(e);
        }
    }
}

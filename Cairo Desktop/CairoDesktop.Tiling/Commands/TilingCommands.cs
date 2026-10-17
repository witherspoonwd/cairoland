using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common;
using CairoDesktop.Common.Localization;
using CairoDesktop.Interop;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using System;
using System.Text;
using System.Windows.Interop;

namespace CairoDesktop.Tiling.Commands
{
    public class TilingViewTagCommand : TilingCommand
    {
        public TilingViewTagCommand(ITilingManager manager)
            : base(manager, "TilingViewTag", "Views a tag on the focused screen.", () => DisplayString.sCommand_TilingViewTag, IndexParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            int? index = GetInt(parameters, "Index");
            if (!index.HasValue) return false;
            Manager.ViewTag(null, index.Value);
            return true;
        }
    }

    public class TilingMoveToTagCommand : TilingCommand
    {
        public TilingMoveToTagCommand(ITilingManager manager)
            : base(manager, "TilingMoveToTag", "Moves the focused window to a tag.", () => DisplayString.sCommand_TilingMoveToTag, IndexParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            int? index = GetInt(parameters, "Index");
            if (!index.HasValue) return false;
            Manager.MoveFocusedToTag(index.Value);
            return true;
        }
    }

    public class TilingViewPreviousTagCommand : TilingCommand
    {
        public TilingViewPreviousTagCommand(ITilingManager manager)
            : base(manager, "TilingViewPreviousTag", "Views the previously viewed tag.", () => DisplayString.sCommand_TilingViewPreviousTag) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.ViewPreviousTag(null);
            return true;
        }
    }

    public class TilingFocusDirectionCommand : TilingCommand
    {
        public TilingFocusDirectionCommand(ITilingManager manager)
            : base(manager, "TilingFocusDirection", "Focuses the window in a direction.", () => DisplayString.sCommand_TilingFocusDirection, DirectionParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            var direction = GetDirection(parameters);
            if (!direction.HasValue) return false;
            Manager.FocusDirection(direction.Value);
            return true;
        }
    }

    public class TilingSwapDirectionCommand : TilingCommand
    {
        public TilingSwapDirectionCommand(ITilingManager manager)
            : base(manager, "TilingSwapDirection", "Swaps the focused window with its neighbor in a direction.", () => DisplayString.sCommand_TilingSwapDirection, DirectionParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            var direction = GetDirection(parameters);
            if (!direction.HasValue) return false;
            Manager.SwapDirection(direction.Value);
            return true;
        }
    }

    public class TilingMoveDirectionCommand : TilingCommand
    {
        public TilingMoveDirectionCommand(ITilingManager manager)
            : base(manager, "TilingMoveDirection", "Moves the focused window into its neighbor's split.", () => DisplayString.sCommand_TilingMoveDirection, DirectionParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            var direction = GetDirection(parameters);
            if (!direction.HasValue) return false;
            Manager.MoveDirection(direction.Value);
            return true;
        }
    }

    public class TilingResizeCommand : TilingCommand
    {
        public TilingResizeCommand(ITilingManager manager)
            : base(manager, "TilingResize", "Moves the focused window's split edge in a direction.", () => DisplayString.sCommand_TilingResize, DirectionParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            var direction = GetDirection(parameters);
            if (!direction.HasValue) return false;
            Manager.Resize(direction.Value);
            return true;
        }
    }

    public class TilingFocusNextCommand : TilingCommand
    {
        public TilingFocusNextCommand(ITilingManager manager)
            : base(manager, "TilingFocusNext", "Focuses the next window on the tag.", () => DisplayString.sCommand_TilingFocusNext) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.FocusNext();
            return true;
        }
    }

    public class TilingFocusPrevCommand : TilingCommand
    {
        public TilingFocusPrevCommand(ITilingManager manager)
            : base(manager, "TilingFocusPrev", "Focuses the previous window on the tag.", () => DisplayString.sCommand_TilingFocusPrev) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.FocusPrev();
            return true;
        }
    }

    public class TilingToggleOrientationCommand : TilingCommand
    {
        public TilingToggleOrientationCommand(ITilingManager manager)
            : base(manager, "TilingToggleOrientation", "Flips the orientation of the focused window's split.", () => DisplayString.sCommand_TilingToggleOrientation) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.ToggleOrientation();
            return true;
        }
    }

    public class TilingEqualizeCommand : TilingCommand
    {
        public TilingEqualizeCommand(ITilingManager manager)
            : base(manager, "TilingEqualize", "Makes every split on the current tag even.", () => DisplayString.sCommand_TilingEqualize) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.Equalize();
            return true;
        }
    }

    public class TilingToggleFloatingCommand : TilingCommand
    {
        public TilingToggleFloatingCommand(ITilingManager manager)
            : base(manager, "TilingToggleFloating", "Toggles whether the focused window floats.", () => DisplayString.sCommand_TilingToggleFloating) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.ToggleFloating();
            return true;
        }
    }

    public class TilingToggleFullscreenCommand : TilingCommand
    {
        public TilingToggleFullscreenCommand(ITilingManager manager)
            : base(manager, "TilingToggleFullscreen", "Toggles fullscreen for the focused window.", () => DisplayString.sCommand_TilingToggleFullscreen) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.ToggleFullscreen();
            return true;
        }
    }

    public class TilingCloseWindowCommand : TilingCommand
    {
        public TilingCloseWindowCommand(ITilingManager manager)
            : base(manager, "TilingCloseWindow", "Closes the focused window.", () => DisplayString.sCommand_TilingCloseWindow) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.CloseFocused();
            return true;
        }
    }

    public class TilingFocusScreenCommand : TilingCommand
    {
        public TilingFocusScreenCommand(ITilingManager manager)
            : base(manager, "TilingFocusScreen", "Focuses the next or previous screen.", () => DisplayString.sCommand_TilingFocusScreen, NextParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.FocusScreen(GetBool(parameters, "Next") ?? true);
            return true;
        }
    }

    public class TilingMoveToScreenCommand : TilingCommand
    {
        public TilingMoveToScreenCommand(ITilingManager manager)
            : base(manager, "TilingMoveToScreen", "Moves the focused window to the next or previous screen.", () => DisplayString.sCommand_TilingMoveToScreen, NextParameter) { }

        protected override bool Run((string name, object value)[] parameters)
        {
            Manager.MoveFocusedToScreen(GetBool(parameters, "Next") ?? true);
            return true;
        }
    }

    /// <summary>
    /// Closes the foreground window the way its title bar X button does (WM_SYSCOMMAND SC_CLOSE), whether or not
    /// tiling manages it.
    /// </summary>
    public class CloseActiveWindowCommand : TilingCommand
    {
        private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        public CloseActiveWindowCommand(ITilingManager manager)
            : base(manager, "CloseActiveWindow", "Closes the active window.", () => DisplayString.sCommand_CloseActiveWindow) { }

        protected override bool RequiresTiling => false;

        protected override bool Run((string name, object value)[] parameters)
        {
            IntPtr hwnd = User32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == User32.GetShellWindow())
            {
                return false;
            }

            StringBuilder className = new StringBuilder(256);
            User32.GetClassName(hwnd, className, className.Capacity);
            if (Array.IndexOf(ShellClasses, className.ToString()) >= 0)
            {
                return false;
            }

            // never close Cairo's own bars
            if (HwndSource.FromHwnd(hwnd)?.RootVisual is AppBarWindow)
            {
                return false;
            }

            return User32.PostMessage(hwnd, User32.WM_SYSCOMMAND, new IntPtr(User32.SC_CLOSE), IntPtr.Zero);
        }
    }

    public class RunCommandCommand : TilingCommand
    {
        public RunCommandCommand(ITilingManager manager)
            : base(manager, "RunCommand", "Runs a command line.", () => DisplayString.sCommand_RunCommand, CommandParameter) { }

        protected override bool RequiresTiling => false;

        private static readonly CairoCommandParameter CommandParameter = new CairoCommandParameter
        {
            Name = "Command",
            Description = "The command line to run, e.g. notepad.exe or \"C:\\Program Files\\App\\app.exe\" --flag",
            IsRequired = true
        };

        protected override bool Run((string name, object value)[] parameters)
        {
            string command = null;
            foreach (var parameter in parameters)
            {
                if (parameter.name == "Command")
                {
                    command = parameter.value?.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            TilingLaunchTerminalCommand.SplitCommand(Environment.ExpandEnvironmentVariables(command.Trim()), out string fileName, out string arguments);
            return ShellHelper.StartProcess(fileName, arguments);
        }
    }

    public class TilingLaunchTerminalCommand : TilingCommand
    {
        private readonly Settings _settings;

        public TilingLaunchTerminalCommand(ITilingManager manager, Settings settings)
            : base(manager, "TilingLaunchTerminal", "Launches the configured terminal.", () => DisplayString.sCommand_TilingLaunchTerminal)
        {
            _settings = settings;
        }

        protected override bool RequiresTiling => false;

        protected override bool Run((string name, object value)[] parameters)
        {
            string command = string.IsNullOrWhiteSpace(_settings.TerminalCommand) ? "wt.exe" : _settings.TerminalCommand.Trim();
            SplitCommand(command, out string fileName, out string arguments);

            if (ShellHelper.StartProcess(fileName, arguments))
            {
                return true;
            }

            return ShellHelper.StartProcess("cmd.exe");
        }

        internal static void SplitCommand(string command, out string fileName, out string arguments)
        {
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                if (end > 0)
                {
                    fileName = command.Substring(1, end - 1);
                    arguments = command.Substring(end + 1).Trim();
                    return;
                }
            }

            int space = command.IndexOf(' ');
            fileName = space < 0 ? command : command.Substring(0, space);
            arguments = space < 0 ? string.Empty : command.Substring(space + 1).Trim();
        }
    }

    public class RestartCairoCommand : TilingCommand
    {
        private readonly ICairoApplication _cairoApplication;

        public RestartCairoCommand(ITilingManager manager, ICairoApplication cairoApplication)
            : base(manager, "RestartCairo", "Restarts Cairo.", () => DisplayString.sCommand_RestartCairo)
        {
            _cairoApplication = cairoApplication;
        }

        protected override bool RequiresTiling => false;

        protected override bool Run((string name, object value)[] parameters)
        {
            _cairoApplication.RestartCairo();
            return true;
        }
    }

    public class DisplayProjectionCommand : TilingCommand
    {
        public DisplayProjectionCommand(ITilingManager manager)
            : base(manager, "DisplayProjection", "Opens the display projection switcher.", () => DisplayString.sCommand_DisplayProjection) { }

        protected override bool RequiresTiling => false;

        protected override bool Run((string name, object value)[] parameters)
        {
            return ShellHelper.StartProcess(Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\DisplaySwitch.exe"));
        }
    }
}

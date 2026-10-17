using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common.Localization;
using ManagedShell.Common.Helpers;
using System.Collections.Generic;

namespace CairoDesktop.Commands
{
    public class OpenPersonalizeControlPanelCommand : ICairoCommand
    {
        public ICairoCommandInfo Info => _info;

        private readonly OpenPersonalizeControlPanelCommandInfo _info = new OpenPersonalizeControlPanelCommandInfo();

        public bool Execute(params (string name, object value)[] parameters)
        {
            return ShellHelper.StartProcess("Rundll32.exe", "shell32.dll,Control_RunDLL desk.cpl,,2");
        }
    }

    public class OpenPersonalizeControlPanelCommandInfo : ICairoCommandInfo
    {
        public string Identifier => "OpenPersonalizeControlPanel";

        public string Description => "Opens the Personalize control panel.";

        public string Label => DisplayString.sDesktop_Personalize;

        public bool IsAvailable => true;

        public IReadOnlyCollection<CairoCommandParameter> Parameters => null;
    }
}

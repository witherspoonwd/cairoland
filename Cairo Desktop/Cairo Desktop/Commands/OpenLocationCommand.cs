using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common.Localization;
using CairoDesktop.Infrastructure.ObjectModel;
using ManagedShell.ShellFolders;
using System.Collections.Generic;

namespace CairoDesktop.Commands
{
    public class OpenLocationCommand : ICairoCommand
    {
        public ICairoCommandInfo Info => _info;

        private readonly ICommandService _commandService;
        private readonly OpenLocationCommandInfo _info = new OpenLocationCommandInfo();

        public OpenLocationCommand(ICommandService commandService)
        {
            _commandService = commandService;
        }

        public bool Execute(params (string name, object value)[] parameters)
        {
            string path = null;

            foreach (var parameter in parameters)
            {
                switch (parameter.name)
                {
                    case "Path":
                        if (parameter.value is string _path)
                        {
                            path = _path;
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return _commandService.InvokeCommand("OpenLocationInWindow", ("Path", path));
        }
    }

    public class OpenLocationCommandInfo : ICairoShellItemCommandInfo
    {
        public string Identifier => "OpenLocation";

        public string Description => "Opens the specified location in a new window.";

        public string Label => DisplayString.sCommand_OpenLocation;

        public bool IsAvailable => true;

        private List<CairoCommandParameter> _parameters = new List<CairoCommandParameter>()
        {
            new CairoCommandParameter {
                Name = "Path",
                Description = "Path to open",
                IsRequired = true
            }
        };

        public IReadOnlyCollection<CairoCommandParameter> Parameters => _parameters;

        public string LabelForShellItem(ShellItem item)
        {
            return DisplayString.sStacks_OpenInNewWindow;
        }

        public bool IsAvailableForShellItem(ShellItem item)
        {
            // OpenLocationInWindow is offered in shell item context menus instead
            return false;
        }
    }
}

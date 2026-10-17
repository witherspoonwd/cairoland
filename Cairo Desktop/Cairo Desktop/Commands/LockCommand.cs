using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Common.Localization;
using CairoDesktop.Tiling;
using ManagedShell.Common.Helpers;
using System.Collections.Generic;

namespace CairoDesktop.Commands
{
    public class LockCommand : ICairoCommand
    {
        public ICairoCommandInfo Info => _info;

        private readonly LockCommandInfo _info = new LockCommandInfo();
        private readonly WorkstationLockManager _lockManager;

        public LockCommand(WorkstationLockManager lockManager)
        {
            _lockManager = lockManager;
        }

        public bool Execute(params (string name, object value)[] parameters)
        {
            if (_lockManager.IsRemapActive)
            {
                // Win+L is remapped, so locking has to briefly lift the policy
                _lockManager.Lock();
            }
            else
            {
                ShellHelper.Lock();
            }

            return true;
        }
    }

    public class LockCommandInfo : ICairoCommandInfo
    {
        public string Identifier => "Lock";

        public string Description => "Locks the workstation.";

        public string Label => DisplayString.sCairoMenu_Lock;

        public bool IsAvailable => true;

        public IReadOnlyCollection<CairoCommandParameter> Parameters => null;
    }
}

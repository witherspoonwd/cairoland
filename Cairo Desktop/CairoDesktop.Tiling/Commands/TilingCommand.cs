using CairoDesktop.Application.Interfaces;
using CairoDesktop.Application.Structs;
using CairoDesktop.Tiling.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CairoDesktop.Tiling.Commands
{
    /// <summary>
    /// Base for commands that act on the tiling manager. They are available only while tiling is enabled.
    /// </summary>
    public abstract class TilingCommand : ICairoCommand, IDisposable
    {
        protected readonly ITilingManager Manager;
        private readonly TilingCommandInfo _info;

        public ICairoCommandInfo Info => _info;

        protected TilingCommand(ITilingManager manager, string identifier, string description, Func<string> label, params CairoCommandParameter[] parameters)
        {
            Manager = manager;
            _info = new TilingCommandInfo(manager, identifier, description, label, parameters, RequiresTiling);
        }

        /// <summary>When false the command is available even with tiling off.</summary>
        protected virtual bool RequiresTiling => true;

        public bool Execute(params (string name, object value)[] parameters)
        {
            if (RequiresTiling && !Manager.IsEnabled)
            {
                return false;
            }

            return Run(parameters ?? new (string, object)[0]);
        }

        protected abstract bool Run((string name, object value)[] parameters);

        protected static int? GetInt((string name, object value)[] parameters, string name)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.name == name)
                {
                    if (parameter.value is int i) return i;
                    if (parameter.value != null && int.TryParse(parameter.value.ToString(), out int parsed)) return parsed;
                }
            }

            return null;
        }

        protected static bool? GetBool((string name, object value)[] parameters, string name)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.name == name)
                {
                    if (parameter.value is bool b) return b;
                    if (parameter.value != null && bool.TryParse(parameter.value.ToString(), out bool parsed)) return parsed;
                }
            }

            return null;
        }

        protected static Direction? GetDirection((string name, object value)[] parameters)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.name == "Direction")
                {
                    if (parameter.value is Direction d) return d;
                    if (parameter.value != null && Enum.TryParse(parameter.value.ToString(), true, out Direction parsed)) return parsed;
                }
            }

            return null;
        }

        protected static readonly CairoCommandParameter DirectionParameter = new CairoCommandParameter
        {
            Name = "Direction",
            Description = "Left, Right, Up or Down",
            IsRequired = true
        };

        protected static readonly CairoCommandParameter IndexParameter = new CairoCommandParameter
        {
            Name = "Index",
            Description = "Zero-based tag index (0-9)",
            IsRequired = true
        };

        protected static readonly CairoCommandParameter NextParameter = new CairoCommandParameter
        {
            Name = "Next",
            Description = "True for the next screen, false for the previous one",
            IsRequired = false
        };

        public virtual void Dispose()
        {
            _info.Dispose();
        }
    }

    public class TilingCommandInfo : ICairoCommandInfo, INotifyPropertyChanged, IDisposable
    {
        private readonly ITilingManager _manager;
        private readonly Func<string> _label;
        private readonly bool _requiresTiling;

        public string Identifier { get; }

        public string Description { get; }

        public string Label => _label();

        public bool IsAvailable => !_requiresTiling || _manager.IsEnabled;

        public IReadOnlyCollection<CairoCommandParameter> Parameters { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public TilingCommandInfo(ITilingManager manager, string identifier, string description, Func<string> label, CairoCommandParameter[] parameters, bool requiresTiling)
        {
            _manager = manager;
            _label = label;
            _requiresTiling = requiresTiling;
            Identifier = identifier;
            Description = description;
            Parameters = parameters.Length > 0 ? parameters : null;

            _manager.EnabledChanged += Manager_EnabledChanged;
        }

        private void Manager_EnabledChanged(object sender, EventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
        }

        public void Dispose()
        {
            _manager.EnabledChanged -= Manager_EnabledChanged;
        }
    }
}

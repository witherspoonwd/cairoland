using CairoDesktop.Common;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CairoDesktop.SupportingClasses
{
    /// <summary>
    /// An action the shortcut editor offers: a Cairo command, and the hint for its value if it takes one.
    /// </summary>
    public class ShortcutAction
    {
        public string Id { get; set; }

        public string Label { get; set; }

        /// <summary>Null when the command takes no value.</summary>
        public string ValueHint { get; set; }

        /// <summary>True when the command does nothing without a value.</summary>
        public bool ValueRequired { get; set; }
    }

    /// <summary>
    /// One read-only row in the Shortcuts tab's list of built-in keys.
    /// </summary>
    public class BuiltInKeyRow
    {
        public string Combo { get; set; }

        public string Action { get; set; }

        public string Status { get; set; }

        public bool IsLive { get; set; }
    }

    /// <summary>
    /// One editable row in the Shortcuts settings tab. Raises <see cref="Changed"/> on every edit so the list can
    /// be written back to <see cref="Settings.SuperKeyShortcuts"/>.
    /// </summary>
    public class ShortcutRow : INotifyPropertyChanged
    {
        private readonly Func<string, ShortcutAction> _findAction;

        private bool _shift;
        private bool _ctrl;
        private bool _alt;
        private string _key;
        private string _command;
        private string _value;

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler Changed;

        public ShortcutRow(SuperKeyShortcut shortcut, Func<string, ShortcutAction> findAction)
        {
            _findAction = findAction;
            _shift = shortcut.Shift;
            _ctrl = shortcut.Ctrl;
            _alt = shortcut.Alt;
            _key = shortcut.Key;
            _command = shortcut.Command;
            _value = shortcut.Value;
        }

        public bool Shift
        {
            get => _shift;
            set => Set(ref _shift, value);
        }

        public bool Ctrl
        {
            get => _ctrl;
            set => Set(ref _ctrl, value);
        }

        public bool Alt
        {
            get => _alt;
            set => Set(ref _alt, value);
        }

        public string Key
        {
            get => _key;
            set => Set(ref _key, value);
        }

        public string Command
        {
            get => _command;
            set
            {
                if (Set(ref _command, value))
                {
                    OnPropertyChanged(nameof(NeedsValue));
                    OnPropertyChanged(nameof(ValueHint));
                }
            }
        }

        public string Value
        {
            get => _value;
            set => Set(ref _value, value);
        }

        public bool NeedsValue => ValueHint != null;

        public string ValueHint => _findAction(_command)?.ValueHint;

        public bool IsMissingValue => (_findAction(_command)?.ValueRequired ?? false) && string.IsNullOrWhiteSpace(_value);

        private string _modifierPrefix;
        /// <summary>The base modifier, e.g. "Win +", which every shortcut includes.</summary>
        public string ModifierPrefix
        {
            get => _modifierPrefix;
            set => SetDisplay(ref _modifierPrefix, value);
        }

        private string _status;
        public string Status
        {
            get => _status;
            set => SetDisplay(ref _status, value);
        }

        private bool _isLive;
        /// <summary>False when the status is a problem to fix.</summary>
        public bool IsLive
        {
            get => _isLive;
            set => SetDisplay(ref _isLive, value);
        }

        public SuperKeyShortcut ToShortcut()
        {
            return new SuperKeyShortcut
            {
                Shift = _shift,
                Ctrl = _ctrl,
                Alt = _alt,
                Key = _key,
                Command = _command,
                Value = NeedsValue ? _value : null
            };
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // for display-only properties, which aren't saved
        private void SetDisplay<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (!Equals(field, value))
            {
                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

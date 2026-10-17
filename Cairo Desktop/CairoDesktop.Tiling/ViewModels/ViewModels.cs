using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace CairoDesktop.Tiling.ViewModels
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;

        public RelayCommand(Action<object> execute)
        {
            _execute = execute;
        }

        public RelayCommand(Action execute) : this(_ => execute())
        {
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute(parameter);
    }

    public class TagViewModel : ObservableObject
    {
        public int Index { get; }

        private string _label;
        public string Label
        {
            get => _label;
            set => Set(ref _label, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        private bool _isOccupied;
        public bool IsOccupied
        {
            get => _isOccupied;
            set => Set(ref _isOccupied, value);
        }

        private bool _isUrgent;
        public bool IsUrgent
        {
            get => _isUrgent;
            set => Set(ref _isUrgent, value);
        }

        /// <summary>Views this tag.</summary>
        public ICommand ViewCommand { get; }

        /// <summary>Moves the focused window to this tag.</summary>
        public ICommand MoveHereCommand { get; }

        public TagViewModel(int index, string label, Action<int> view, Action<int> moveHere)
        {
            Index = index;
            _label = label;
            ViewCommand = new RelayCommand(() => view(index));
            MoveHereCommand = new RelayCommand(() => moveHere(index));
        }
    }

    public class ManagedWindowViewModel : ObservableObject
    {
        public ManagedWindow Window { get; }

        public IntPtr Hwnd => Window.Hwnd;

        public string Title => Window.Title;

        private ImageSource _icon;
        private bool _iconLoaded;
        public ImageSource Icon
        {
            get
            {
                if (!_iconLoaded)
                {
                    _iconLoaded = true;
                    _icon = WindowIconHelper.GetIcon(Window.Hwnd, Window.ClassName);
                }

                return _icon;
            }
        }

        private bool _isFocused;
        public bool IsFocused
        {
            get => _isFocused;
            set => Set(ref _isFocused, value);
        }

        private bool _isFloating;
        public bool IsFloating
        {
            get => _isFloating;
            set => Set(ref _isFloating, value);
        }

        public ICommand FocusCommand { get; }

        public ICommand CloseCommand { get; }

        public ICommand ToggleFloatingCommand { get; }

        /// <summary>Takes the tag index as its parameter.</summary>
        public ICommand MoveToTagCommand { get; }

        public ManagedWindowViewModel(ManagedWindow window, ITilingManager manager)
        {
            Window = window;
            _isFloating = window.IsFloating;
            Window.PropertyChanged += Window_PropertyChanged;

            FocusCommand = new RelayCommand(() => manager.FocusWindow(window.Hwnd));
            CloseCommand = new RelayCommand(() => manager.CloseWindow(window.Hwnd));
            ToggleFloatingCommand = new RelayCommand(() => manager.ToggleFloating(window.Hwnd));
            MoveToTagCommand = new RelayCommand(parameter =>
            {
                if (parameter != null && int.TryParse(parameter.ToString(), out int index))
                {
                    manager.MoveWindowToTag(window.Hwnd, index);
                }
            });
        }

        private void Window_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ManagedWindow.Title))
            {
                OnPropertyChanged(nameof(Title));
            }
        }

        internal void Detach()
        {
            Window.PropertyChanged -= Window_PropertyChanged;
        }
    }

    /// <summary>
    /// Tag bar and window list state for one screen.
    /// </summary>
    public class ScreenTagsViewModel : ObservableObject
    {
        public string DeviceName { get; }

        public ObservableCollection<TagViewModel> Tags { get; } = new ObservableCollection<TagViewModel>();

        /// <summary>Windows on the selected tag: tiled in tree order, then floating.</summary>
        public ObservableCollection<ManagedWindowViewModel> Windows { get; } = new ObservableCollection<ManagedWindowViewModel>();

        private ManagedWindowViewModel _focusedWindow;
        public ManagedWindowViewModel FocusedWindow
        {
            get => _focusedWindow;
            set
            {
                ManagedWindowViewModel old = _focusedWindow;
                if (Set(ref _focusedWindow, value))
                {
                    if (old != null) old.IsFocused = false;
                    if (value != null) value.IsFocused = true;
                }
            }
        }

        private bool _isActive;
        /// <summary>False when tiling is off or the screen is not managed.</summary>
        public bool IsActive
        {
            get => _isActive;
            set => Set(ref _isActive, value);
        }

        /// <summary>Views the next (parameter true) or previous tag; used by the scroll wheel.</summary>
        public ICommand CycleTagCommand { get; }

        public ScreenTagsViewModel(string deviceName, Action<bool> cycle)
        {
            DeviceName = deviceName;
            CycleTagCommand = new RelayCommand(parameter => cycle(parameter is bool next && next));
        }
    }
}

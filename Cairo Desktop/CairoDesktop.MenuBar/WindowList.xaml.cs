using CairoDesktop.Tiling.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CairoDesktop.MenuBar
{
    /// <summary>
    /// The windows on a screen's selected tag. Left-click focuses, middle-click closes, right-click opens a menu.
    /// </summary>
    public partial class WindowList : UserControl
    {
        public WindowList()
        {
            InitializeComponent();
        }

        private void Item_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is ManagedWindowViewModel window))
            {
                return;
            }

            switch (e.ChangedButton)
            {
                case MouseButton.Left:
                    window.FocusCommand.Execute(null);
                    e.Handled = true;
                    break;
                case MouseButton.Middle:
                    window.CloseCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}

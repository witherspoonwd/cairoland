using CairoDesktop.Tiling.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace CairoDesktop.MenuBar
{
    /// <summary>
    /// The tags of one screen. Left-click views a tag, right-click moves the focused window there, and the
    /// scroll wheel cycles through tags.
    /// </summary>
    public partial class TagBar : UserControl
    {
        public TagBar()
        {
            InitializeComponent();
        }

        private void TagBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DataContext is ScreenTagsViewModel model)
            {
                // scrolling up moves to the next tag
                model.CycleTagCommand.Execute(e.Delta > 0);
                e.Handled = true;
            }
        }
    }
}

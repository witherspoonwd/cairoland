using System;
using System.Windows;
using System.Windows.Interop;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Cairo's own windows are ignored by the tiling manager unless they opt in with
    /// <c>tiling:TilingWindow.IsManaged="True"</c>, which makes them live on a tag like any other app window.
    /// </summary>
    public static class TilingWindow
    {
        public static readonly DependencyProperty IsManagedProperty =
            DependencyProperty.RegisterAttached("IsManaged", typeof(bool), typeof(TilingWindow), new PropertyMetadata(false));

        public static bool GetIsManaged(DependencyObject element) => (bool)element.GetValue(IsManagedProperty);

        public static void SetIsManaged(DependencyObject element, bool value) => element.SetValue(IsManagedProperty, value);

        /// <summary>Whether a window of this process opted in. Must be called on the UI thread.</summary>
        internal static bool IsOwnWindowManaged(IntPtr hwnd)
        {
            return HwndSource.FromHwnd(hwnd)?.RootVisual is Window window && GetIsManaged(window);
        }
    }
}

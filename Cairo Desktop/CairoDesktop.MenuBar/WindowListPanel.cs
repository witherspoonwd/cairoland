using System;
using System.Windows;
using System.Windows.Controls;

namespace CairoDesktop.MenuBar
{
    /// <summary>
    /// Lays children out in one row, sharing the available width equally up to <see cref="MaxItemWidth"/>.
    /// </summary>
    public class WindowListPanel : Panel
    {
        public static readonly DependencyProperty MaxItemWidthProperty = DependencyProperty.Register(
            nameof(MaxItemWidth), typeof(double), typeof(WindowListPanel),
            new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MaxItemWidth
        {
            get => (double)GetValue(MaxItemWidthProperty);
            set => SetValue(MaxItemWidthProperty, value);
        }

        private double ItemWidth(double available)
        {
            int count = InternalChildren.Count;
            if (count == 0)
            {
                return 0;
            }

            if (double.IsInfinity(available))
            {
                return MaxItemWidth;
            }

            return Math.Max(0, Math.Min(MaxItemWidth, Math.Floor(available / count)));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = ItemWidth(availableSize.Width);
            double height = 0;

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(width, availableSize.Height));
                height = Math.Max(height, child.DesiredSize.Height);
            }

            return new Size(width * InternalChildren.Count, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double width = ItemWidth(finalSize.Width);
            double x = 0;

            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width;
            }

            return finalSize;
        }
    }
}

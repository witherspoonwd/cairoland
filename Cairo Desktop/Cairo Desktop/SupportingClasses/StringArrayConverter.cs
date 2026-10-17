using System;
using System.Linq;
using System.Windows.Data;

namespace CairoDesktop.SupportingClasses
{
    /// <summary>
    /// Converts a string array to and from comma-separated text.
    /// </summary>
    [ValueConversion(typeof(string[]), typeof(string))]
    public class StringArrayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is string[] items ? string.Join(", ", items) : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (!(value is string text))
            {
                return new string[0];
            }

            return text.Split(',')
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToArray();
        }
    }
}

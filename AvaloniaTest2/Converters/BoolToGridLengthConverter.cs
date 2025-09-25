using System;
using Avalonia.Data.Converters;
using Avalonia;
using Avalonia.Controls;

namespace AvaloniaTest2.Converters
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        /// <summary>
        /// Convierte un bool a GridLength.
        /// true → valor VisibleLength (por ejemplo, 200)
        /// false → valor HiddenLength (por ejemplo, 0)
        /// </summary>
        public GridLength VisibleLength { get; set; } = new GridLength(200, GridUnitType.Pixel);
        public GridLength HiddenLength { get; set; } = new GridLength(0, GridUnitType.Pixel);

        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? VisibleLength : HiddenLength;
            }

            return HiddenLength;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is GridLength gl)
            {
                return gl.Value > 0;
            }

            return false;
        }
    }
}
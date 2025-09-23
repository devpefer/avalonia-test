using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AvaloniaTest2.Converters
{
    public class BoolToStatusConverter : IValueConverter
    {
        // Convierte bool a string
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? "Online" : "Offline";
            }
            return "Desconocido";
        }

        // No necesario para este caso, pero debe implementarse
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                return s.Equals("Online", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}
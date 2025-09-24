using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaTest2.Converters;

public class BytesToMbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // if (value is long bytes)
        //     return $"{bytes / 1024 / 1024.0:F2} MB";
        // return value;

        if (value is long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    public class InstallStatusToOpacityConverter : IValueConverter
    {
        public static readonly InstallStatusToOpacityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isInstalled = !string.IsNullOrEmpty(value as string);
            return isInstalled ? 1.0 : 0.3; // Pełna przezroczystość lub wyszarzenie
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

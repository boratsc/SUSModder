using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using SUSModder.Services;

namespace SUSModder.Converters
{
    public class StringToBitmapConverter : IValueConverter
    {
        public static readonly StringToBitmapConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string fileName && !string.IsNullOrWhiteSpace(fileName))
            {
                // Użyj serwisu preloadingu z cache
                return ModIconPreloader.GetIcon(fileName);
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
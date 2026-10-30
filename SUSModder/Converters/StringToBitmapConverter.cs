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
            // Memory-only for remote references; background loading signals IconAvailable.
            const string prefix = "susmodder-icon:";
            return value is string reference && reference.StartsWith(prefix, StringComparison.Ordinal)
                ? ModIconPreloader.GetVersionedIcon(reference[prefix.Length..])
                : ModIconPreloader.GetIcon(value as string);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

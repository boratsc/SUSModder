using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    public class StringTruncateConverter : IValueConverter
    {
        public static readonly StringTruncateConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text || string.IsNullOrEmpty(text))
                return value;

            int maxLength = 150; // Domyślna długość
            
            if (parameter is string paramStr && int.TryParse(paramStr, out int customLength))
            {
                maxLength = customLength;
            }

            if (text.Length <= maxLength)
                return text;

            // Skróć do maxLength i dodaj wielokropek
            return text.Substring(0, maxLength).TrimEnd() + "...";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

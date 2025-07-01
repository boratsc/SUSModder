using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SUSModder.Converters
{
    public class StringNotNullOrEmptyToBoolConverter : IValueConverter
    {
        public static readonly StringNotNullOrEmptyToBoolConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isNotNullOrEmpty = value is string str && !string.IsNullOrEmpty(str);

            // Jeśli parameter to "Invert" lub "True", odwróć wynik
            if (parameter is string param && (param == "Invert" || param == "True"))
            {
                return !isNotNullOrEmpty;
            }

            return isNotNullOrEmpty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    /// <summary>
    /// Konwertuje liczbę dostępnych aktualizacji na kolor tekstu
    /// Czerwony (#F44336) jeśli są aktualizacje (count > 0), biały (#FFFFFF) jeśli nie ma
    /// </summary>
    public class UpdatesColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                // Czerwony jeśli są aktualizacje
                if (count > 0)
                    return new SolidColorBrush(Color.Parse("#F44336"));

                // Biały jeśli nie ma aktualizacji
                return new SolidColorBrush(Color.Parse("#FFFFFF"));
            }

            return new SolidColorBrush(Color.Parse("#FFFFFF")); // Domyślnie biały
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

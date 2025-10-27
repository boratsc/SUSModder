using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    /// <summary>
    /// Konwertuje liczbę dostępnych aktualizacji na kolor tekstu
    /// Czerwony (#F44336) jeśli są aktualizacje (count > 0), domyślny kolor z motywu jeśli nie ma
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

                // Pobierz kolor z zasobów motywu gdy nie ma aktualizacji
                if (Application.Current?.Resources.TryGetResource("TextPrimaryBrush", null, out var resource) == true)
                {
                    return resource as IBrush ?? new SolidColorBrush(Color.Parse("#FFFFFF"));
                }
            }

            // Fallback - pobierz kolor z zasobów lub użyj białego
            if (Application.Current?.Resources.TryGetResource("TextPrimaryBrush", null, out var fallbackResource) == true)
            {
                return fallbackResource as IBrush ?? new SolidColorBrush(Color.Parse("#FFFFFF"));
            }

            return new SolidColorBrush(Color.Parse("#FFFFFF")); // Ostateczny fallback
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    /// <summary>
    /// Konwertuje liczbę dostępnych aktualizacji na kolor tekstu.
    /// Gdy są aktualizacje — token motywu UpdateAlertBrush (Dark/Szklany: bursztyn z palety,
    /// Light/Pink: czerwieni jak dawniej); poza themingiem fallback #F44336.
    /// </summary>
    public class UpdatesColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                // Kolor alertu aktualizacji z motywu, fallback dawniej wtłoczony czerwony
                if (count > 0)
                {
                    if (Application.Current?.Resources.TryGetResource("UpdateAlertBrush", null, out var alertResource) == true
                        && alertResource is IBrush alertBrush)
                    {
                        return alertBrush;
                    }

                    return new SolidColorBrush(Color.Parse("#F44336"));
                }

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

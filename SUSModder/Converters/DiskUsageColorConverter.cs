using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    /// <summary>
    /// Konwertuje procent zajętości dysku na kolor (zielony/żółty/czerwony)
    /// </summary>
    public class DiskUsageColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                // Zielony < 50%
                if (percentage < 50)
                    return new SolidColorBrush(Color.Parse("#4CAF50"));
                
                // Żółty 50-80%
                if (percentage < 80)
                    return new SolidColorBrush(Color.Parse("#FFC107"));
                
                // Czerwony > 80%
                return new SolidColorBrush(Color.Parse("#F44336"));
            }

            return new SolidColorBrush(Color.Parse("#4CAF50")); // Domyślnie zielony
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

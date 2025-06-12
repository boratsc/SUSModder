using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    public class ThemeColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isDark && parameter is string colorKey)
            {
                return colorKey switch
                {
                    "WindowBackground" => new SolidColorBrush(isDark ? Color.Parse("#2D2D30") : Color.Parse("#F5F5F5")),
                    "PaneBackground" => new SolidColorBrush(isDark ? Color.Parse("#252526") : Color.Parse("#E8E8E8")),
                    "ModCardBackground" => new SolidColorBrush(isDark ? Color.Parse("#3C3C3C") : Color.Parse("#FFFFFF")),
                    "ModCardBorder" => new SolidColorBrush(isDark ? Color.Parse("#5A5A5C") : Color.Parse("#CCCCCC")),
                    "GridSplitter" => new SolidColorBrush(isDark ? Color.Parse("#464647") : Color.Parse("#DDDDDD")),
                    "TextPrimary" => new SolidColorBrush(isDark ? Color.Parse("#FFFFFF") : Color.Parse("#212529")),
                    "TextSecondary" => new SolidColorBrush(isDark ? Color.Parse("#CCCCCC") : Color.Parse("#6C757D")),
                    "Accent" => new SolidColorBrush(isDark ? Color.Parse("#007ACC") : Color.Parse("#0D6EFD")),
                    "Danger" => new SolidColorBrush(isDark ? Color.Parse("#F14C4C") : Color.Parse("#DC3545")),
                    "Success" => new SolidColorBrush(isDark ? Color.Parse("#73C991") : Color.Parse("#198754")),
                    _ => new SolidColorBrush(Colors.Transparent)
                };
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

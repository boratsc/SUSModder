using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SUSModder.Converters;

/// <summary>
/// Konwerter odwracający wartość logiczną (true ↔ false).
/// Przydatny dla IsVisible bindings gdzie potrzebna jest negacja.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

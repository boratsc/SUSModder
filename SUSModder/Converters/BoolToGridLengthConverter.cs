using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace SUSModder.Converters;

/// <summary>
/// Mapuje bool na GridLength (np. kolumna panelu moda: 400px / 0).
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public double TrueWidth { get; set; } = 400;
    public double FalseWidth { get; set; } = 0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = value is true ? TrueWidth : FalseWidth;
        return new GridLength(width);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

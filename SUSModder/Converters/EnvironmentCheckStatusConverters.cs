using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentIcons.Common;
using SUSModder.Core.Diagnostics.Environment;
using System;
using System.Globalization;

namespace SUSModder.Converters;

/// <summary>
/// Status checku środowiska → ikona (kształt rozpoznawalny bez koloru).
/// Etykieta tekstowa statusu jest obok, więc czytniki i daltoniści nie tracą informacji.
/// </summary>
public sealed class EnvironmentCheckStatusToSymbolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SelfCheckStatus.Ok => Symbol.CheckmarkCircle,
        SelfCheckStatus.Info => Symbol.Info,
        SelfCheckStatus.Warning => Symbol.Warning,
        SelfCheckStatus.Error => Symbol.ErrorCircle,
        SelfCheckStatus.NotApplicable => Symbol.Prohibited,
        _ => Symbol.QuestionCircle
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>Status checku środowiska → pędzel motywu (fallback jak w UpdatesColorConverter).</summary>
public sealed class EnvironmentCheckStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            SelfCheckStatus.Ok => "SuccessBrush",
            SelfCheckStatus.Warning => "WarningBrush",
            SelfCheckStatus.Error => "DangerBrush",
            SelfCheckStatus.Info => "IconAccentBrush",
            _ => "TextMutedBrush"
        };

        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse("#FFFFFF"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

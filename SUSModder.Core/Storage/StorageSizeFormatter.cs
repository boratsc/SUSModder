using System.Globalization;

namespace SUSModder.Core.Storage;

/// <summary>Jednostka rozmiaru — binarne (IEC), czyli dzielnik 1024.</summary>
public enum StorageSizeUnit
{
    /// <summary>Bajty.</summary>
    Bytes = 0,

    /// <summary>Kibibajty (1024 B).</summary>
    KibiBytes = 1,

    /// <summary>Mebibajty (1024 KiB).</summary>
    MebiBytes = 2,

    /// <summary>Gibibajty (1024 MiB).</summary>
    GibiBytes = 3,

    /// <summary>Tebibajty (1024 GiB).</summary>
    TebiBytes = 4
}

/// <summary>Rozłożona wartość rozmiaru — do lokalnegoizacji etykiety przez UI.</summary>
public sealed record StorageSizeDisplay(long Bytes, StorageSizeUnit Unit, double Value)
{
    /// <summary>Techniczny sufiks jednostki (IEC — wspólny dla PL i EN).</summary>
    public string UnitSuffix => StorageSizeFormatter.GetUnitSuffix(Unit);
}

/// <summary>
/// Jedyny formatter rozmiaru w Core — wspólny dla kafelków moda (3.1-09)
/// i podsumowania miejsca (3.1-08).
///
/// Używa jednostek binarnych IEC (KiB/MiB/GiB) z dzielnikiem 1024.
/// Nie wolno oznaczać takiej wartości jako „GB” (dziesiętne) — sufiks KiB/GiB jest jednoznaczny.
/// </summary>
public static class StorageSizeFormatter
{
    /// <summary>Dzielnik jednostek binarnych.</summary>
    public const long Scale = 1024L;

    /// <summary>Rozdziela bajty na jednostkę i wartość.</summary>
    public static StorageSizeDisplay Split(long bytes)
    {
        var negative = bytes < 0;
        var value = Math.Abs(bytes);
        double scaled = value;

        var unit = StorageSizeUnit.Bytes;
        if (value >= Scale * Scale * Scale * Scale)
        {
            scaled = value / (double)(Scale * Scale * Scale * Scale);
            unit = StorageSizeUnit.TebiBytes;
        }
        else if (value >= Scale * Scale * Scale)
        {
            scaled = value / (double)(Scale * Scale * Scale);
            unit = StorageSizeUnit.GibiBytes;
        }
        else if (value >= Scale * Scale)
        {
            scaled = value / (double)(Scale * Scale);
            unit = StorageSizeUnit.MebiBytes;
        }
        else if (value >= Scale)
        {
            scaled = value / (double)Scale;
            unit = StorageSizeUnit.KibiBytes;
        }

        if (negative)
            scaled = -scaled;

        return new StorageSizeDisplay(bytes, unit, scaled);
    }

    /// <summary>Formatuje bajty jako tekst (np. „1,5 KiB”). Separator dziesiętny zależy od kultury.</summary>
    public static string Format(long bytes, IFormatProvider? formatProvider = null, int fractionDigits = 2)
    {
        return Format(Split(bytes), formatProvider, fractionDigits);
    }

    /// <summary>Formatuje rozbłożony rozmiar jako tekst (np. „1,5 KiB”).</summary>
    public static string Format(StorageSizeDisplay display, IFormatProvider? formatProvider = null, int fractionDigits = 2)
    {
        var provider = formatProvider ?? CultureInfo.CurrentCulture;
        var digits = Math.Clamp(fractionDigits, 0, 8);

        // Bajty zawsze jako liczba całkowita — „0 B”, „512 B”.
        if (display.Unit == StorageSizeUnit.Bytes)
        {
            return string.Concat(display.Bytes.ToString(provider), " ", GetUnitSuffix(StorageSizeUnit.Bytes));
        }

        // Wartości całkowite bez miejsc dziesiętnych („1 KiB”), ułamki z maksymalnie dwoma
        // miejscami i bez zbędnych zer na końcu („1,5 KiB”).
        var isWhole = Math.Abs(display.Value - Math.Truncate(display.Value)) < 0.0000001;
        var effectiveDigits = isWhole ? 0 : digits;

        var text = display.Value.ToString("N" + effectiveDigits.ToString(CultureInfo.InvariantCulture), provider);

        if (effectiveDigits > 0)
        {
            var info = provider as NumberFormatInfo ?? NumberFormatInfo.CurrentInfo;
            var separator = info.NumberDecimalSeparator;

            if (text.EndsWith("0", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
                if (text.EndsWith(separator, StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - separator.Length);
            }
        }

        return string.Concat(text, " ", GetUnitSuffix(display.Unit));
    }

    /// <summary>Techniczny sufiks jednostki (IEC — wspólny dla PL i EN).</summary>
    public static string GetUnitSuffix(StorageSizeUnit unit) => unit switch
    {
        StorageSizeUnit.Bytes => "B",
        StorageSizeUnit.KibiBytes => "KiB",
        StorageSizeUnit.MebiBytes => "MiB",
        StorageSizeUnit.GibiBytes => "GiB",
        StorageSizeUnit.TebiBytes => "TiB",
        _ => "B"
    };
}

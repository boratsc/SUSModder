namespace SUSModder.Core.GameIntegration;

public static class AmongUsVersionHelper
{
    public static string ToStorageVersion(string amongVersion) =>
        amongVersion.Replace("-", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal);

    public static string NormalizeAmongVersion(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        var parts = trimmed.Replace('.', '-').Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && int.TryParse(parts[0], out var year)
            && int.TryParse(parts[1], out var month)
            && int.TryParse(parts[2], out var day))
        {
            return $"{year}-{month}-{day}";
        }

        if (trimmed.All(char.IsDigit) && trimmed.Length is >= 6 and <= 8)
        {
            var yearText = trimmed[..4];
            var rest = trimmed[4..];

            if (rest.Length >= 2
                && int.TryParse(yearText, out year)
                && int.TryParse(rest[..^1], out month)
                && int.TryParse(rest[^1..], out day)
                && month is >= 1 and <= 12
                && day is >= 1 and <= 31)
            {
                return $"{year}-{month}-{day}";
            }

            if (rest.Length >= 3
                && int.TryParse(yearText, out year)
                && int.TryParse(rest[..^2], out month)
                && int.TryParse(rest[^2..], out day)
                && month is >= 1 and <= 12
                && day is >= 1 and <= 31)
            {
                return $"{year}-{month}-{day}";
            }
        }

        return trimmed;
    }
}

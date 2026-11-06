namespace SUSModder.Core.Storage;

/// <summary>
/// Wspólny walidator kontraktu „no-follow” — używany przez serwis pomiaru i planer sprzątania.
///
/// Zasada: ścieżka, która przechodzi przez reparse point (junction/symlink), nie może być
/// mierzona ani inwentaryzowana, bo wynik dotyczyłby obcego drzewa.
///
/// Granica zaufania (<c>TrustedRoot</c>) wyznacza zakres sprawdzania przodków:
/// - ścieżka jest sprawdzana zawsze (także gdy sama jest granicą),
/// - przodkowie są sprawdzeni tylko do granicy (włącznie),
/// - powyżej granicy decyzję o zaufanym katalogu podejmuje wywołujący.
/// </summary>
internal static class StorageLinkBoundary
{
    /// <summary>
    /// Sprawdza, czy ścieżka (albo jeden z jej przodków w zakresie zaufania) jest reparse pointem.
    /// </summary>
    /// <param name="path">Kanoniczna ścieżka do sprawdzenia.</param>
    /// <param name="trustedRoot">Granica zaufania (kanoniczna) albo null (sprawdzamy wszystkich przodków).</param>
    /// <param name="linkPath">Wychodzi: ścieżka reparse pointu, który zablokował pomiar.</param>
    internal static bool IsLinkPath(string path, string? trustedRoot, out string? linkPath)
    {
        linkPath = null;

        var rootAttributes = TryGetAttributes(path);
        if (rootAttributes.HasValue && rootAttributes.Value.HasFlag(FileAttributes.ReparsePoint))
        {
            linkPath = path;
            return true;
        }

        // Granica zaufania jest sprawdzana sama w sobie; przodków ponad nią nie oceniamy.
        if (!string.IsNullOrEmpty(trustedRoot)
            && string.Equals(path, trustedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var current = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(current))
        {
            var normalized = StoragePathNormalizer.TryNormalize(current);
            if (string.IsNullOrEmpty(normalized))
                break;

            var attributes = TryGetAttributes(normalized);
            if (attributes.HasValue && attributes.Value.HasFlag(FileAttributes.ReparsePoint))
            {
                linkPath = normalized;
                return true;
            }

            if (!string.IsNullOrEmpty(trustedRoot)
                && string.Equals(normalized, trustedRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Doszliśmy do granicy zaufania — dalej nie sprawdzamy.
                break;
            }

            var parent = Path.GetDirectoryName(normalized);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, normalized, StringComparison.OrdinalIgnoreCase))
                break;

            current = parent;
        }

        return false;
    }

    /// <summary>Odczytuje atrybuty ścieżki; null oznacza, że odczyt się nie udał.</summary>
    internal static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch
        {
            return null;
        }
    }
}

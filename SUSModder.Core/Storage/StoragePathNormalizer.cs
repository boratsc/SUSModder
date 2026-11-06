namespace SUSModder.Core.Storage;

/// <summary>
/// Normalizacja ścieżek używana przez pomiar i planowanie sprzątania.
/// Kanoniczna ścieżka jest kluczem cache — dzięki temu ten sam katalog
/// (różny zapis, wielka litera, „..”) jest mierzony i liczony tylko raz.
/// </summary>
public static class StoragePathNormalizer
{
    /// <summary>
    /// Zwraca kanoniczną ścieżkę (pełna, bez końcowego separatora) albo pusty string
    /// dla pustej wejściowej ścieżki. Rzuca wyjątek dla ścieżki niepoprawnej.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        var full = Path.GetFullPath(expanded);
        return TrimTrailingSeparators(full);
    }

    /// <summary>Jak <see cref="Normalize"/>, ale zwraca null zamiast rzucać wyjątek.</summary>
    public static string? TryNormalize(string? path)
    {
        try
        {
            return Normalize(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Czy <paramref name="child"/> leży bezpośrednio wewnątrz <paramref name="parent"/> (ściśle pod nim).</summary>
    public static bool IsSubPath(string? parent, string? child)
    {
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
            return false;

        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
            return false;

        var prefix = parent!.EndsWith(Path.DirectorySeparatorChar) || parent.EndsWith(Path.AltDirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;

        return child!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Czy <paramref name="child"/> to <paramref name="parent"/> albo leży wewnątrz niego.</summary>
    public static bool IsSameOrSubPath(string? parent, string? child)
    {
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
            return false;

        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
            return true;

        return IsSubPath(parent, child);
    }

    /// <summary>
    /// Usuwa duplikaty i zagnieżdżone korzenie. Zostawia tylko korzenie, które nie leżą
    /// wewnątrz innego korzenia — dzięki temu jedna biblioteka nie jest liczona drugi raz.
    /// </summary>
    /// <returns>Korzenie wzięte do sumy oraz ścieżki pominięte (duplikaty / zagnieżdżone).</returns>
    public static (List<string> Kept, List<string> Dropped) DeduplicateRoots(IEnumerable<string>? paths)
    {
        var kept = new List<string>();
        var dropped = new List<string>();

        if (paths is null)
            return (kept, dropped);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in paths)
        {
            var key = TryNormalize(raw);
            if (string.IsNullOrEmpty(key))
            {
                if (!string.IsNullOrWhiteSpace(raw))
                    dropped.Add(raw);
                continue;
            }

            if (!seen.Add(key))
            {
                dropped.Add(key);
                continue;
            }

            // Korzen leży wewnątrz już zachowanego — pomijamy (doliczony zostałby drugi raz).
            if (kept.Any(k => IsSubPath(k, key)))
            {
                dropped.Add(key);
                continue;
            }

            // Nowy korzen zawiera wcześniejszy — wypieramy zagnieżdżony.
            var nested = kept.Where(k => IsSubPath(key, k)).ToList();
            foreach (var nestedPath in nested)
            {
                kept.Remove(nestedPath);
                dropped.Add(nestedPath);
            }

            kept.Add(key);
        }

        return (kept, dropped);
    }

    private static string TrimTrailingSeparators(string full)
    {
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && full.Length == root!.Length)
            return full;

        if (full.Length <= 2)
            return full;

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

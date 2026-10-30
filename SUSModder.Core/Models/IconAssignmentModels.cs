namespace SUSModder.Core.Models;

/// <summary>
/// Globalne role ikon (3.1-04). Każda rola ma dokładnie jeden zasób zarządzany
/// niezależnie od modów.
/// </summary>
public enum IconGlobalRole
{
    /// <summary>Ikona vanilla (Among Us) — działa również dla lokalnie wykrytej gry.</summary>
    Vanilla,

    /// <summary>Wspólna ikona wszystkich dodatków DLL.</summary>
    Dll
}

/// <summary>
/// Stabilne klucze przypisań ikon (3.1-04). Klucz jest jedyną tożsamością
/// przypisania — nie jest wyprowadzany z nazwy moda, nazwy pliku ani ścieżki.
/// </summary>
public static class IconAssignmentKeys
{
    public const string GlobalVanilla = "global:vanilla";
    public const string GlobalDll = "global:dll";
    public const string ModPrefix = "mod:";

    /// <summary>Klucz przypisania ikony pełnego moda.</summary>
    public static string ForMod(int modId) => ModPrefix + modId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Klucz przypisania ikony globalnej.</summary>
    public static string ForGlobal(IconGlobalRole role) => role switch
    {
        IconGlobalRole.Vanilla => GlobalVanilla,
        IconGlobalRole.Dll => GlobalDll,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    /// <summary>Odczytuje Id moda z klucza <c>mod:&lt;id&gt;</c>.</summary>
    public static bool TryGetModId(string? assignmentKey, out int modId)
    {
        modId = 0;
        if (string.IsNullOrEmpty(assignmentKey) || !assignmentKey.StartsWith(ModPrefix, StringComparison.Ordinal))
            return false;

        var raw = assignmentKey[ModPrefix.Length..];
        return int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out modId)
               && modId > 0;
    }
}

/// <summary>
/// Oczekiwana referencja zasobu ikony zapisana w SQLite (3.1-04).
/// Przechowuje wyłącznie metadane zasobu — bajty obrazu pozostają w cache plikowym.
/// </summary>
public sealed class IconAssignment
{
    /// <summary>Stabilny klucz przypisania (patrz <see cref="IconAssignmentKeys"/>).</summary>
    public string AssignmentKey { get; init; } = string.Empty;

    /// <summary>
    /// True dla trwałego tombstone: backend potwierdził w schemacie 1, że przypisanie
    /// zostało jawnie usunięte (jawny null). Tombstone odróżnia „usunięte” od
    /// „nigdy nie było przypisania” — po restarcie offline UI pokazuje placeholder,
    /// a nie starą ikonę legacy. Usunięcie nie kasuje wiersza.
    /// </summary>
    public bool IsRemoved { get; init; }

    /// <summary>Identyfikator zasobu po stronie backendu.</summary>
    public string AssetId { get; init; } = string.Empty;

    /// <summary>Niezmienny adres zasobu (wymaga walidacji hosta w warstwie pobierania).</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>SHA-256 bajtów PNG (64 znaki szesnastkowe).</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Typ zawartości zasobu.</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Rozmiar zasobu w bajtach.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Wersja schematu kontraktu, w którym referencja została potwierdzona.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Czas ostatniego zapisu (UTC).</summary>
    public DateTime UpdatedAtUtc { get; init; }
}

using System.Collections.Generic;
using System.Text.Json;

namespace SUSModder.Core.Services.Localization;

/// <summary>
/// Algorytm rozwiązywania kluczy lokalizacyjnych w strukturze JSON,
/// która może mieszać zagnieżdżone obiekty (np. UI.Buttons.Install)
/// z płaskimi kluczami zawierającymi kropki w nazwie (np.
/// LaunchDiagnostics.Severity.Info). Wyekstrahowany z warstwy UI,
/// aby mógł być testowany niezależnie od implementacji
/// <see cref="ILocalizationService"/>.
/// </summary>
public static class LocalizationKeyResolver
{
    /// <summary>
    /// Próbuje rozwiązać klucz w danym drzewie tłumaczeń. Zwraca wartość
    /// tekstową lub null, jeśli klucz nie istnieje. Nie rzuca wyjątków.
    /// </summary>
    /// <param name="translations">Drzewo tłumaczeń (Dictionary&lt;string, object&gt; lub JsonElement).</param>
    /// <param name="key">Klucz w formacie "a.b.c" - może odpowiadać zarówno zagnieżdżonej ścieżce, jak i płaskiemu kluczowi w jednej z sekcji.</param>
    public static string? Resolve(object? translations, string key)
    {
        if (translations is null || string.IsNullOrWhiteSpace(key))
            return null;

        var parts = key.Split('.');

        // Algorytm "najdłuższego prefiksu zagnieżdżenia":
        //   a.b.c.d → a.b.c → a.b → a
        // Dla każdego prefiksu szukamy kontenera w drzewie, a w nim
        // reszty ścieżki jako płaskiego klucza lub dalszej nawigacji.
        for (int prefixLen = parts.Length; prefixLen >= 1; prefixLen--)
        {
            var prefix = parts.Take(prefixLen).ToArray();
            if (!TryNavigate(translations, prefix, out var container) || container is null)
                continue;

            var remaining = parts.Skip(prefixLen).ToArray();

            if (remaining.Length == 0)
            {
                if (TryGetScalar(container, out var directVal))
                    return directVal;
                continue;
            }

            // 1) cały remaining jako płaski klucz w kontenerze
            var flatKey = string.Join(".", remaining);
            if (TryNavigate(container, new[] { flatKey }, out var flatVal)
                && TryGetScalar(flatVal, out var flatScalar))
            {
                return flatScalar;
            }

            // 2) nawiguj po remaining jako zagnieżdżona ścieżka
            if (TryNavigate(container, remaining, out var nestedVal)
                && TryGetScalar(nestedVal, out var nestedScalar))
            {
                return nestedScalar;
            }
        }

        return null;
    }

    private static bool TryNavigate(object root, string[] path, out object? value)
    {
        value = null;
        object? current = root;
        for (int i = 0; i < path.Length; i++)
        {
            if (current is null)
                return false;

            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(path[i], out var next))
                    return false;
                current = next;
            }
            else if (current is JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object)
                    return false;
                if (!element.TryGetProperty(path[i], out var next))
                    return false;
                current = next;
            }
            else
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private static bool TryGetScalar(object? value, out string? result)
    {
        if (value is string s)
        {
            result = s;
            return true;
        }
        if (value is JsonElement je && je.ValueKind == JsonValueKind.String)
        {
            result = je.GetString();
            return true;
        }
        result = null;
        return false;
    }
}

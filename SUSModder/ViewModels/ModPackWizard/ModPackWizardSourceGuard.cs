using System;
using System.IO;

namespace SUSModder.ViewModels.ModPackWizard;

/// <summary>
/// Reguły bezpieczeństwa źródeł spoza katalogu — przeniesione 1:1 z obecnego kodu kreatora
/// (allowlista URL GitHub release asset + safe path pod BepInEx/plugins).
/// Zaawansowana ścieżka NIE omija blokad ani kontroli — walidacja prezentacyjna przed wysłaniem
/// do istniejącego walidatora backendu.
/// </summary>
public static class ModPackWizardSourceGuard
{
    /// <summary>
    /// Dopuszczamy wyłącznie publiczne_https://github.com/{owner}/{repo}/releases/download/{tag}/{asset}_
    /// — dokładnie ten sam zestaw reguł co w poprzedniej wersji formularza.
    /// </summary>
    public static bool IsAllowedGithubReleaseAssetUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 6 &&
               string.Equals(segments[2], "releases", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[3], "download", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(segments[4]) &&
               !string.IsNullOrWhiteSpace(segments[5]);
    }

    /// <summary>Ścieżka instalacji DLL musi pozostawać pod BepInEx/plugins (bez wyjścia z katalogu instancji).</summary>
    public static bool IsSafeDllInstallPath(string? dllInstallPath)
    {
        if (string.IsNullOrWhiteSpace(dllInstallPath))
            return true;

        var normalized = dllInstallPath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal))
            return false;

        var root = Path.GetFullPath(Path.Combine("C:\\SUSModderPathValidation", "BepInEx", "plugins"));
        var candidate = Path.GetFullPath(Path.Combine("C:\\SUSModderPathValidation", normalized));
        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trimmedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(trimmedCandidate, trimmedRoot, StringComparison.OrdinalIgnoreCase) ||
               trimmedCandidate.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Propozycja nazwy artefaktu na podstawie URL-a release assetu (repo/asset).
    /// Uzupełniamy TYLKO wiarygodnie ustalone dane — użytkownik zawsze może zmienić nazwę.
    /// </summary>
    public static string? SuggestArtifactName(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 6)
            return null;

        var repo = segments[1];
        var asset = Uri.UnescapeDataString(segments[^1]);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(asset);
        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
            return null;

        return string.Equals(nameWithoutExtension, repo, StringComparison.OrdinalIgnoreCase)
            ? nameWithoutExtension
            : $"{nameWithoutExtension} ({repo})";
    }
}

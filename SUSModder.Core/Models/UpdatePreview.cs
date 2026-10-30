using System;
using System.Collections.Generic;
using System.Linq;

namespace SUSModder.Core.Models;

/// <summary>
/// Chwilowa prognoza (podsumowanie) aktualizacji moda FULL.
///
/// Model jest WYŁĄCZNIE odczytowy: powstaje z istniejących danych
/// (mapa instalacji, katalog modów, macierz kompatybilności) i nie jest zapisywany
/// w bazie ani w plikach. Nie jest gwarancją wyniku — po operacji pokazywany jest
/// rzeczywisty wynik (przywrócone/pominięte/błędne dodatki DLL).
///
/// Zasada: brak danych oznacza niepewność, nigdy potwierdzoną zgodność.
/// Stan konfiguracji nie jest deklarowany jako zachowany — aplikacja nie ma
/// zweryfikowanej reguły przenoszenia plików konfiguracji moda/gry.
/// </summary>
public sealed class UpdatePreview
{
    /// <summary>Id moda FULL (z katalogu).</summary>
    public int ModId { get; init; }

    /// <summary>Nazwa moda FULL.</summary>
    public string ModName { get; init; } = string.Empty;

    /// <summary>Platforma źródłowa ("steam" / "epic") — ma znaczenie dla układu katalogu i wersji gry.</summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>Wersja faktycznie zainstalowana (z mapy instalacji, gdy dostępna).</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>Wersja docelowa, do której odnosi się podsumowanie.</summary>
    public string TargetVersion { get; init; } = string.Empty;

    /// <summary>Wersja Among Us wymagana przez wersję docelową.</summary>
    public string TargetAmongVersion { get; init; } = string.Empty;

    /// <summary>Czy mod jest zainstalowany (katalog instalacji istnieje).</summary>
    public bool IsInstalled { get; init; }

    /// <summary>Czy udało się odczytać mapę instalacji (.susmodder-install.json).</summary>
    public bool HasInstallationMap { get; init; }

    /// <summary>Ocena zachowania konfiguracji — nigdy nie deklaruje zachowania plików.</summary>
    public UpdateConfigStatus ConfigStatus { get; init; } = UpdateConfigStatus.Unknown;

    /// <summary>Prognoza losu dodatków DLL (dodatki z mapy instalacji + pliki niestandardowe).</summary>
    public IReadOnlyList<UpdateAddonPreview> Addons { get; init; } = Array.Empty<UpdateAddonPreview>();

    /// <summary>
    /// Stabilny klucz zatwierdzonego celu. Wiąże podsumowanie z konkretną wersją,
    /// platformą i wersją gry — służy do rewalidacji przed wykonaniem operacji.
    /// </summary>
    public string TargetKey { get; init; } = string.Empty;

    /// <summary>Czy któryś dodatek wymaga uwagi użytkownika.</summary>
    public bool HasAddonWarnings => Addons.Any(addon => addon.RequiresAttention);

    /// <summary>Czy podsumowanie zawiera elementy wymagające uwagi.</summary>
    public bool RequiresAttention => HasAddonWarnings;

    /// <summary>
    /// Czy da się potwierdzić zgodność wszystkich dodatków. Brak danych nie jest zgodnością.
    /// </summary>
    public bool IsCompatibilityFullyConfirmed =>
        Addons.Count > 0 && Addons.All(addon => addon.IsCompatibilityConfirmed);
}

/// <summary>
/// Prognoza dla pojedynczego dodatku DLL.
/// </summary>
public sealed class UpdateAddonPreview
{
    /// <summary>Id moda DLL z katalogu (0 dla pliku niestandardowego, brak wpisu w katalogu).</summary>
    public int ModId { get; init; }

    /// <summary>Nazwa dodatku (dla pliku niestandardowego: nazwa pliku).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Zainstalowana wersja dodatku (znana z mapy instalacji).</summary>
    public string? InstalledVersion { get; init; }
    /// <summary>Catalog version that replay will attempt to download, not necessarily the installed version.</summary>
    public string? RestoredVersion { get; init; }

    /// <summary>Status prognozy.</summary>
    public UpdateAddonStatus Status { get; init; } = UpdateAddonStatus.Unknown;

    /// <summary>Kod kompatybilności z API ("F", "W", "NT", "NW") lub null, gdy brak danych.</summary>
    public string? CompatibilityStatusCode { get; init; }

    /// <summary>Czy dane kompatybilności dotyczą dokładnie pary wersji (FULL docelowa × DLL zainstalowana).</summary>
    public bool IsExactCompatibilityMatch { get; init; }

    /// <summary>Czy to plik DLL znaleziony w katalogu, którego nie ma w mapie instalacji.</summary>
    public bool IsCustomFile { get; init; }

    /// <summary>
    /// Stabilny kod statusu do lokalizacji w UI. UI mapuje kod na tekst — bez logiki per język.
    /// </summary>
    public string StatusCode => Status switch
    {
        UpdateAddonStatus.Retained => "retained",
        UpdateAddonStatus.RetainedCompatibilityUnknown => "retained_compatibility_unknown",
        UpdateAddonStatus.RetainedIncompatible => "retained_incompatible",
        UpdateAddonStatus.NotRestoredMissingCatalog => "not_restored_missing_catalog",
        UpdateAddonStatus.NotRestoredCustomFile => "not_restored_custom_file",
        _ => "unknown"
    };

    /// <summary>Czy zgodność z wersją docelową jest potwierdzona danymi (F/W dla dokładnej pary wersji).</summary>
    public bool IsCompatibilityConfirmed => Status == UpdateAddonStatus.Retained;

    /// <summary>Czy dodatek wymaga uwagi użytkownika (niezgodność, brak w katalogu, plik niestandardowy).</summary>
    public bool RequiresAttention => Status is UpdateAddonStatus.RetainedIncompatible
        or UpdateAddonStatus.NotRestoredMissingCatalog
        or UpdateAddonStatus.NotRestoredCustomFile;
}

/// <summary>
/// Stan pojedynczego dodatku DLL po aktualizacji (prognoza, nie wynik).
/// </summary>
public enum UpdateAddonStatus
{
    /// <summary>Brak danych — nie da się ocenić losu dodatku.</summary>
    Unknown = 0,

    /// <summary>Dodatek katalogowy zostanie przywrócony po aktualizacji; zgodność potwierdzona.</summary>
    Retained = 1,

    /// <summary>
    /// Dodatek katalogowy zostanie przywrócony, ale zgodność z wersją docelową nie jest potwierdzona.
    /// Nie jest to zgoda na „zostaje i działa” — brak danych nie jest zgodnością.
    /// </summary>
    RetainedCompatibilityUnknown = 2,

    /// <summary>Dodatek zostanie przywrócony, ale dane kompatybilności wskazują niezgodność z wersją docelową.</summary>
    RetainedIncompatible = 3,

    /// <summary>Dodatek jest zainstalowany, ale nie ma go w katalogu — nie zostanie przywrócony.</summary>
    NotRestoredMissingCatalog = 4,

    /// <summary>
    /// Plik DLL w katalogu moda nie jest zarejestrowany w mapie instalacji —
    /// aktualizacja zastępuje katalog moda, więc plik nie zostanie odtworzony.
    /// </summary>
    NotRestoredCustomFile = 5
}

/// <summary>
/// Ocena zachowania konfiguracji moda/gry podczas aktualizacji.
/// </summary>
public enum UpdateConfigStatus
{
    /// <summary>Brak danych — nie można ocenić stanu konfiguracji.</summary>
    Unknown = 0,

    /// <summary>
    /// Aktualizacja zastępuje katalog moda. Aplikacja przenosi wyłącznie zarejestrowane
    /// dodatki DLL — plików konfiguracji nie przenosi, więc zachowanie ustawień nie jest gwarantowane.
    /// </summary>
    NotGuaranteed = 1
}

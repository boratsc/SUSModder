using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Services;

// Kontrakt selektora wersji narzędzia legendary.exe (NIE wersji gry Among Us).
// Backend: SUSModder.Core ILegendaryVersionProvider + adapter UI CoreLegendaryVersionService (zarejestrowany w DI).
//
// Lista = wydania z OBU aktywnych repozytoriów GitHub, połączone i posortowane od najnowszych:
//   - Upstream legendary-gl/legendary — asset legendary_windows_x64.exe
//     (np. latest 0.21.1 "Lowlife (hotfix)", zweryfikowane na stronie wydań GitHub)
//   - Fork Heroic-Games-Launcher/legendary — asset legendary_windows_x86_64.exe
//     (np. latest 0.20.43 "Riding Shotgun (Hotfix)"; nazwa assetu wg istniejącego URL przypiętego w EpicVersionManager)
// Tożsamość wydania to (Source, Tag, AssetName) — sam tag NIE identyfikuje wersji, bo repozytoria
// mogą wydać ten sam numer, a nazwy assetów różnią się między repo. Tylko wydania z odpowiednim
// assetem Windows x64 trafiają na listę.
// Trwałość trybu (pinned/manual) i wybranego wydania należy do serwisu (user_settings), NIE do UI.
// Pinned (docelowo upstream 0.21.1) definiuje Core — UI tylko wyświetla to, co zwróci serwis.

/// <summary>Repozytorium źródłowe wydania legendary.exe.</summary>
public enum LegendaryReleaseSource
{
    /// <summary>legendary-gl/legendary (upstream, asset legendary_windows_x64.exe).</summary>
    Upstream,

    /// <summary>Heroic-Games-Launcher/legendary (asset legendary_windows_x86_64.exe).</summary>
    Heroic,
}

/// <summary>Wydanie legendary.exe dostępne do pobrania (Windows x64). Identyfikator: Source + Tag + AssetName.</summary>
/// <remarks>
/// Tożsamość obejmuje jawną nazwę assetu — nazwa pliku różni się między repozytoriami
/// (<c>legendary_windows_x64.exe</c> vs <c>legendary_windows_x86_64.exe</c>), więc Source + Tag
/// bez assetu nie opisuje jednoznacznie pobieranego pliku.
/// </remarks>
public sealed record LegendaryReleaseInfo(
    LegendaryReleaseSource Source,
    string Tag,
    string AssetName = "",
    string Sha256 = "",
    DateTimeOffset? PublishedAt = null)
{
    /// <summary>Etykieta do wyświetlenia, np. "legendary-gl 0.21.1" (nazwy repo nie podlegają tłumaczeniu).</summary>
    public string DisplayName => (Source == LegendaryReleaseSource.Upstream ? "legendary-gl" : "Heroic") + " " + Tag;

    /// <summary>Równość tożsamościowa (Source, Tag, AssetName) — bez zależności od PublishedAt.</summary>
    public bool SameId(LegendaryReleaseInfo? other) =>
        other is not null &&
        Source == other.Source &&
        Tag == other.Tag &&
        AssetName == other.AssetName;
}

/// <summary>
/// Stan bez sieci: tryb z ustawień + przypięte wydanie (Pinned) + faktycznie zainstalowany legendary.exe (Installed).
/// Selected = wydanie wybrane w trybie manual (null, gdy brak lub tryb pinned) — intencja użytkownika, NIE stan binarki.
/// Installed = null, gdy pliku brak albo jego wersji nie da się ustalić (np. niezgodny SHA) — UI nie może traktować
/// Selected/Pinned jako "zastosowanego" i musi pozwalać ponownie zastosować to samo wydanie.
/// </summary>
public sealed record LegendaryVersionState(
    bool UsePinned,
    LegendaryReleaseInfo? Selected,
    LegendaryReleaseInfo? Pinned,
    LegendaryReleaseInfo? Installed);

/// <summary>Wynik przełączenia. ErrorCode = stabilny kod z LegendaryErrorCodes (UI lokalizuje);
/// Installed = wydanie aktywne po operacji.</summary>
public sealed record LegendarySwitchResult(
    bool Success,
    string? ErrorCode = null,
    LegendaryReleaseInfo? Installed = null);

public interface ILegendaryVersionService
{
    /// <summary>Stan bez sieci: tryb zapisany w ustawieniach + wydanie przypięte + odczyt zainstalowanego legendary.exe.</summary>
    Task<LegendaryVersionState> GetStateAsync(CancellationToken ct = default);

    /// <summary>Wydania z obu repo (Upstream + Heroic), tylko z assetem Windows x64 zgodnie z nazwą danego repo,
    /// od najnowszych (PublishedAt malejąco, tagi równolegle). Rzuca wyjątek przy braku sieci/błędzie API.</summary>
    Task<IReadOnlyList<LegendaryReleaseInfo>> GetReleasesAsync(CancellationToken ct = default);

    /// <summary>
    /// Pobiera, weryfikuje (SHA256 assetu z GitHub) i podmienia legendary.exe.
    /// release = null oznacza powrót do wydania przypiętego. Nie przerywa przy pracującym legendary (zwraca ToolBusy).
    /// Progress: WYŁĄCZNIE kody faz z LegendaryPhaseCodes ("downloading", "verifying", "installing") —
    /// nigdy tekst przeznaczony do wyświetlenia (nie podlega lokalizacji UI); nieznany kod → UI pokaże etykietę ogólną.
    /// </summary>
    Task<LegendarySwitchResult> SwitchAsync(
        LegendaryReleaseInfo? release,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Stabilne kody faz przełączania przesyłane przez IProgress&lt;string&gt; (techniczne, nie do wyświetlania wprost).</summary>
public static class LegendaryPhaseCodes
{
    public const string Downloading = "downloading";
    public const string Verifying = "verifying";
    public const string Installing = "installing";

    /// <summary>Klucz lokalizacji PL/EN dla kodu fazy; null = nieznany kod (UI użyje etykiety ogólnej).</summary>
    public static string? MapToKey(string? phaseCode) => phaseCode switch
    {
        Downloading => "Settings.LegendaryVersion.Phases.Downloading",
        Verifying => "Settings.LegendaryVersion.Phases.Verifying",
        Installing => "Settings.LegendaryVersion.Phases.Installing",
        _ => null,
    };
}

/// <summary>Stabilne kody błędów przełączenia — UI mapuje na klucze lokalizacji PL/EN.</summary>
public static class LegendaryErrorCodes
{
    public const string Offline = "offline";
    public const string GitHubUnavailable = "github_unavailable";
    public const string NoWindowsAsset = "no_windows_asset";
    public const string ToolBusy = "tool_busy";
    public const string HashMismatch = "hash_mismatch";
    public const string Failed = "failed";

    /// <summary>Klucz lokalizacji PL/EN dla kodu błędu; nieznany kod → Errors.Unknown.</summary>
    public static string MapErrorKey(string? errorCode) => errorCode switch
    {
        Offline => "Settings.LegendaryVersion.Errors.Offline",
        GitHubUnavailable => "Settings.LegendaryVersion.Errors.GitHubUnavailable",
        NoWindowsAsset => "Settings.LegendaryVersion.Errors.NoWindowsAsset",
        ToolBusy => "Settings.LegendaryVersion.Errors.ToolBusy",
        HashMismatch => "Settings.LegendaryVersion.Errors.HashMismatch",
        Failed => "Settings.LegendaryVersion.Errors.Failed",
        _ => "Settings.LegendaryVersion.Errors.Unknown",
    };
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Repozytorium źródłowe wydania legendary.exe.
    /// </summary>
    public enum LegendaryReleaseSource
    {
        /// <summary>Upstream legendary (oryginalne repozytorium projektu) — repo legendary-gl/legendary.</summary>
        Upstream,

        /// <summary>Wydania utrzymywane przez Heroic — repo Heroic-Games-Launcher/legendary.</summary>
        Heroic,
    }

    /// <summary>
    /// Wydanie legendary.exe dostępne do pobrania (Windows x64).
    /// </summary>
    /// <remarks>
    /// Tożsamość wydania to (Source, Tag, AssetName) — sam tag nie identyfikuje wersji,
    /// bo oba repozytoria mogą wydać ten sam numer, a nazwa assetu różni się między repozytoriami.
    /// Sha256 pochodzi z metadanych GitHub (digest assetu); asset bez digestu nie jest oferowany.
    /// </remarks>
    public sealed record LegendaryReleaseInfo(
        LegendaryReleaseSource Source,
        string Tag,
        string AssetName,
        string Sha256,
        DateTimeOffset? PublishedAt = null)
    {
        /// <summary>Repozytorium GitHub, z którego pochodzi wydanie.</summary>
        public string Repository => LegendaryReleaseCatalog.RepositoryFor(Source);

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
    /// Selected = wydanie wybrane w trybie manual (null, gdy brak lub tryb pinned).
    /// </summary>
    public sealed record LegendaryVersionState(
        bool UsePinned,
        LegendaryReleaseInfo? Selected,
        LegendaryReleaseInfo? Pinned,
        LegendaryReleaseInfo? Installed);

    /// <summary>
    /// Wynik przełączenia wersji. ErrorCode = stabilny kod z <see cref="LegendaryErrorCodes"/>
    /// (UI lokalizuje); Installed = wydanie aktywne po operacji.
    /// </summary>
    public sealed record LegendarySwitchResult(
        bool Success,
        string? ErrorCode = null,
        LegendaryReleaseInfo? Installed = null);

    /// <summary>
    /// Stabilne kody błędów przełączenia — UI mapuje je na klucze lokalizacji PL/EN.
    /// </summary>
    /// <remarks>
    /// Wartości są wspólne z kontraktem UI (<c>Settings.LegendaryVersion.Errors.*</c>) —
    /// zmiana wartości wymaga aktualizacji obu stron.
    /// </remarks>
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

    /// <summary>
    /// Fazy zgłaszane przez <see cref="ILegendaryVersionProvider.SwitchAsync"/> — identyfikatory techniczne,
    /// nie treść do pokazania użytkownikowi (tłumaczenie należy do UI).
    /// </summary>
    /// <remarks>
    /// Wartości są wspólne z kontraktem UI (<c>Settings.LegendaryVersion.Phases.*</c>).
    /// </remarks>
    public static class LegendaryProgressPhases
    {
        public const string Download = "downloading";
        public const string Verify = "verifying";
        public const string Install = "installing";
    }

    /// <summary>
    /// Zaufanie do zainstalowanego pliku legendary.exe.
    /// </summary>
    public enum LegendaryToolTrust
    {
        /// <summary>Plik odpowiada wybranemu wydaniu (przypiętemu lub ręcznemu).</summary>
        Trusted,

        /// <summary>
        /// Plik to binarka SUSModder (przypięta wersja albo poprzednia przypięta 0.20.41),
        /// ale nie ta, którą wybrano. Można go uruchomić; tło wymieni go na właściwą wersję.
        /// </summary>
        TrustedButNotSelected,

        /// <summary>Plik o nieznanej sumie — nie wolno go uruchamiać.</summary>
        Untrusted,

        /// <summary>Pliku nie ma.</summary>
        Missing,
    }

    /// <summary>
    /// Wynik inspekcji zainstalowanego narzędzia.
    /// </summary>
    public sealed record LegendaryToolInspection(
        LegendaryToolTrust Trust,
        string? InstalledSha256,
        bool IsSelectedVersion);

    /// <summary>
    /// Błąd komunikacji z API GitHub (brak sieci, limit, niepoprawna odpowiedź).
    /// </summary>
    public sealed class LegendaryReleaseException : Exception
    {
        public LegendaryReleaseException(string errorCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        /// <summary>Stabilny kod z <see cref="LegendaryErrorCodes"/>.</summary>
        public string ErrorCode { get; }
    }
}

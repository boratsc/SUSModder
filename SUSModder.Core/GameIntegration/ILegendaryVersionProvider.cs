using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Zarządza wersją narzędzia legendary.exe używanego w trybie Epic.
    /// </summary>
    /// <remarks>
    /// Kontrakt warstwy UI (namespace <c>SUSModder.Services</c>) mapuje się na ten interfejs;
    /// Core nie zna typów UI.
    /// </remarks>
    public interface ILegendaryVersionProvider
    {
        /// <summary>
        /// Stan bez sieci: tryb zapisany w ustawieniach + wydanie przypięte + odczyt zainstalowanego legendary.exe.
        /// </summary>
        Task<LegendaryVersionState> GetStateAsync(CancellationToken ct = default);

        /// <summary>
        /// Wydania z obu repozytoriów (Upstream + Heroic), tylko z assetem Windows x64
        /// i z digestem sha256 z GitHub, od najnowszych. Rzuca <see cref="LegendaryReleaseException"/> przy błędzie API.
        /// </summary>
        Task<IReadOnlyList<LegendaryReleaseInfo>> GetReleasesAsync(CancellationToken ct = default);

        /// <summary>
        /// Pobiera, weryfikuje (SHA256 z metadanych GitHub) i podmienia legendary.exe.
        /// <paramref name="release"/> = null oznacza powrót do wydania przypiętego.
        /// Nie przerywa przy pracującym legendary — zwraca <see cref="LegendaryErrorCodes.ToolBusy"/>.
        /// </summary>
        Task<LegendarySwitchResult> SwitchAsync(
            LegendaryReleaseInfo? release,
            IProgress<string>? progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// Upewnia się, że zainstalowany legendary.exe odpowiada wybranemu wydaniu
        /// (przypiętemu albo ręcznemu) — pobiera brakujący lub nieaktualny plik.
        /// W trybie ręcznym nigdy nie podmienia na wydanie przypięte.
        /// </summary>
        Task<LegendarySwitchResult> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// Sprawdza sumę zainstalowanego pliku bez ruchu sieciowego.
        /// </summary>
        /// <remarks>
        /// Podstawa decyzji, czy wolno uruchomić narzędzie: plik o nieznanej sumie jest obcy
        /// i nie może zostać uruchomiony.
        /// </remarks>
        Task<LegendaryToolInspection> InspectInstalledAsync(CancellationToken ct = default);
    }
}

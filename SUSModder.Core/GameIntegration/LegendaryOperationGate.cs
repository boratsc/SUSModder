using System;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Wspólna bramka między podmianą pliku narzędzia a uruchamianiem procesów legendary.
    /// </summary>
    /// <remarks>
    /// Podmiana pliku i start procesu muszą się wykluczać — inaczej legendary uruchomiłby
    /// się z pliku, który jest właśnie podmieniany. Brama jest wstrzykiwana w oba kierunki:
    /// podmiana trzyma ją tylko podczas zmiany pliku i zapisu wyboru,
    /// start procesu tylko przez moment startu (weryfikacja → uruchomienie → rejestracja operacji).
    /// Działający proces jest rejestrowany licznikiem operacji — podmiana wtedy odmawia
    /// (ToolBusy) zamiast ubijać proces.
    /// </remarks>
    internal static class LegendaryOperationGate
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static int _activeOperations;

        /// <summary>Liczba uruchomionych operacji legendary (procesów w locie).</summary>
        internal static int ActiveOperations => Volatile.Read(ref _activeOperations);

        /// <summary>
        /// Zajmuje bramkę wyłącznie. Zwalnia w <see cref="Dispose"/>.
        /// </summary>
        internal static async Task<IDisposable> AcquireAsync(CancellationToken ct)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            return new Lease(Gate);
        }

        /// <summary>Rejestruje start operacji (proces legendary) — podmiana zobaczy to jako „w użyciu”.</summary>
        internal static void RegisterOperationStart() => Interlocked.Increment(ref _activeOperations);

        /// <summary>Rejestruje zakończenie operacji.</summary>
        internal static void RegisterOperationEnd() => Interlocked.Decrement(ref _activeOperations);

        private sealed class Lease : IDisposable
        {
            private SemaphoreSlim? _gate;

            public Lease(SemaphoreSlim gate) => _gate = gate;

            public void Dispose()
            {
                SemaphoreSlim? gate = Interlocked.Exchange(ref _gate, null);
                gate?.Release();
            }
        }
    }

    /// <summary>
    /// Zaufane sumy kontrolne narzędzia legendary.exe.
    /// </summary>
    /// <remarks>
    /// Uruchamiamy wyłącznie plik, którego SHA-256 odpowiada jednej ze znanych wartości:
    /// aktualnie przypiętej wersji, poprzedniej przypiętej wersji (0.20.41 — wydane przez nas
    /// i zweryfikowane) albo ręcznie wybranemu wydaniu (suma zweryfikowana w API GitHub
    /// przy wyborze). Plik o innej sumie jest niezaufany — nie uruchamiamy go.
    /// </remarks>
    internal static class LegendaryToolTrustPolicy
    {
        /// <summary>
        /// Poprzednio przypięta wersja 0.20.41 (repo Heroic). Nadal akceptowana — to binarka
        /// wydana przez SUSModder i zweryfikowana, więc nie jest „plikiem obcym".
        /// </summary>
        public const string LegacyPinnedSha256 =
            "b8866cca30d1d66a9d1331d6f38ec8249073bd749b871821ef9c20e70ce7d263";

        /// <summary>
        /// Czy dana suma odpowiada zaufanemu wydaniu (przypiętemu, poprzedniemu przypiętemu
        /// albo ręcznie wybranemu).
        /// </summary>
        public static bool IsTrustedSha256(string? actualSha256, string? manualSha256)
        {
            if (string.IsNullOrWhiteSpace(actualSha256))
                return false;

            if (string.Equals(actualSha256, LegendaryVersionService.PinnedSha256, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(actualSha256, LegacyPinnedSha256, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrWhiteSpace(manualSha256) &&
                   string.Equals(actualSha256, manualSha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}

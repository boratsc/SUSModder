using System;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Uruchamia weryfikację i ewentualną wymianę legendary.exe w tle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wywoływane dla trybu Epic po starcie aplikacji — aktualizacja przypiętej wersji
    /// nie czeka na rozpoczęcie operacji przez użytkownika.
    /// </para>
    /// <para>
    /// Zasady:
    /// nie blokuje startu, nie narusza sesji Epic (katalog konfiguracji legendary nietknięty),
    /// nie zabija żadnego procesu (przy zajętym pliku zwraca ToolBusy),
    /// nie nadpisuje ręcznie wybranego wydania, a przy błędzie zostawia starą binarkę.
    /// </para>
    /// </remarks>
    public static class LegendaryBackgroundUpdater
    {
        /// <summary>
        /// Bieżąca instancja zadania tła (null = brak uruchomionego).
        /// </summary>
        /// <remarks>
        /// Chronimy przed zdublowanym harmonogramem: kolejne wywołania w trakcie trwającego
        /// zadania są pomijane. Po zakończeniu zadania kolejne wywołanie jest możliwe —
        /// samo sprawdzenie sumy kończy się bez pobrania, gdy plik już jest zgodny.
        /// </remarks>
        private static Task? _running;

        private static readonly object SyncRoot = new();

        /// <summary>
        /// Pozwala zresetować stan w testach. Wyłącznie dla testów.
        /// </summary>
        internal static void ResetForTests()
        {
            lock (SyncRoot)
            {
                _running = null;
            }

            _modeOverride = null;
        }

        /// <summary>
        /// Planuje aktualizację w tle. Zadanie nie blokuje i nie zgłasza wyjątków.
        /// </summary>
        /// <param name="mode">Tryb aplikacji ("steam" / "epic") — poza Epic nie robi nic.</param>
        /// <param name="provider">Serwis wersji narzędzia (null = użyj domyślnego).</param>
        /// <param name="output">Dziennik diagnostyczny.</param>
        public static void Schedule(
            string mode,
            ILegendaryVersionProvider? provider = null,
            IDiagnosticsOutput? output = null)
        {
            if (!string.Equals(mode, "epic", StringComparison.OrdinalIgnoreCase))
                return;

            var service = provider ?? CreateDefaultProvider(output);
            if (service == null)
                return;

            lock (SyncRoot)
            {
                // Zadanie już działa — nie dublujemy harmonogramu.
                if (_running is { IsCompleted: false })
                    return;

                _running = Task.Run(async () =>
                {
                    try
                    {
                        // Tryb sprawdzamy PONOWNIE w chwili wykonania, nie tylko przy planowaniu.
                        // Użytkownik mógł odrzucić logowanie i przejść na Steam — wtedy narzędzia
                        // Epic nie ruszamy (w tym trybie nie jest potrzebne).
                        if (!IsEpicMode())
                        {
                            output?.Write("[Legendary] Tło: tryb nie jest już Epic — pomijam aktualizację.");
                            return;
                        }

                        var result = await service.EnsureInstalledAsync();

                        if (result.Success)
                        {
                            output?.Write(
                                $"[Legendary] Tło: legendary.exe zgodny z wyborem " +
                                $"({result.Installed?.DisplayName ?? "nieznany"}).");
                        }
                        else
                        {
                            // Nieudana aktualizacja tła nie może zepsuć działania aplikacji —
                            // poprzednia binarka zostaje na miejscu.
                            output?.Write($"[Legendary] Tło: aktualizacja nieudana ({result.ErrorCode}).");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // anulowanie przy zamykaniu aplikacji
                    }
                    catch (Exception ex)
                    {
                        output?.Write($"[Legendary] Tło: wyjątek ({ex.Message}).");
                    }
                });
            }
        }

        /// <summary>
        /// Bieżący tryb aplikacji ("steam" / "epic").
        /// </summary>
        /// <remarks>
        /// Domyślnie czytany z ustawień użytkownika. W testach podmieniany przez
        /// <see cref="ModeOverrideForTests"/>.
        /// </remarks>
        private static Func<string>? _modeOverride;

        /// <summary>
        /// Podmienia źródło trybu — wyłącznie dla testów.
        /// </summary>
        internal static void ModeOverrideForTests(Func<string>? modeProvider) => _modeOverride = modeProvider;

        private static bool IsEpicMode()
        {
            try
            {
                string mode = _modeOverride != null
                    ? _modeOverride()
                    : new Services.UserSettingsService().LoadUserSettings().Mode;

                return string.Equals(mode, "epic", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Nie możemy potwierdzić trybu — nie ruszamy narzędzia.
                return false;
            }
        }

        /// <summary>
        /// Domyślny serwis wersji narzędzia (z ustawieniami aplikacji).
        /// </summary>
        public static ILegendaryVersionProvider? CreateDefaultProvider(IDiagnosticsOutput? output = null)
        {
            try
            {
                return new LegendaryVersionService(output: output);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Legendary] Nie udało się utworzyć serwisa wersji: {ex.Message}");
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Storage;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Zakres pomiaru dla okna podsumowania miejsca: kanoniczny katalog biblioteki,
    /// znane katalogi instalacji (do klasyfikacji) oraz czy katalog faktycznie istnieje.
    /// Istnienie ścieżki sprawdza wywołujący (Core/UI boundary) — VM sam nie dotyka dysku.
    /// </summary>
    public readonly record struct StorageOverviewScope(
        string LibraryRoot,
        IReadOnlyList<string> KnownInstallPaths,
        bool LibraryRootExists);

    /// <summary>
    /// Abstrakcja pomiaru podsumowania (3.1-08). Świadomie NIE zawiera żadnej operacji
    /// usuwania: wersja 3.1 jest tylko-pomiarowa (brak udowodnionej allowlisty aktywnej
    /// własności / junction-safety). Kiedy Core wystawi zweryfikowany bezpieczny cleanup,
    /// integracja dojdzie jako nowe, osobne API — nigdy jako rozszerzenie tego interfejsu
    /// o „delete”.
    /// </summary>
    public interface IStorageOverviewBackend
    {
        /// <summary>
        /// Buduje plan klasyfikacji i pomiaru (bez jakiejkolwiek mutacji plików).
        /// Zwrócony <see cref="StorageCleanupPlan.State"/> niesie jawny stan sumy
        /// (Complete/Partial/Unavailable) — UI ufa jemu, nie zeru domyślnemu.
        /// </summary>
        Task<StorageCleanupPlan> PlanAsync(StorageCleanupPlanRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Producencka implementacja <see cref="IStorageOverviewBackend"/> oparta o Core
    /// <see cref="StorageCleanupPlanner"/> (planowanie/klasyfikacja) i współdzielony
    /// serwis pomiaru. Nie tworzy i nie wywołuje wykonawcy sprzątania.
    /// </summary>
    public sealed class StorageOverviewBackend : IStorageOverviewBackend
    {
        private readonly IStorageSizeMeasurementService _measurement;

        public StorageOverviewBackend(IStorageSizeMeasurementService measurement)
        {
            _measurement = measurement ?? throw new ArgumentNullException(nameof(measurement));
        }

        public Task<StorageCleanupPlan> PlanAsync(StorageCleanupPlanRequest request, CancellationToken cancellationToken)
        {
            _measurement.InvalidateUnder(request.LibraryRoot);
            return new StorageCleanupPlanner(_measurement).PlanAsync(request, cancellationToken);
        }
    }

    /// <summary>Jeden wiersz kategorii w oknie podsumowania. Wszystkie teksty są już zlokalizowane.</summary>
    public sealed class StorageOverviewCategoryRow
    {
        public StorageCleanupCategory Category { get; init; }
        public string Name { get; init; } = string.Empty;
        public string CountText { get; init; } = string.Empty;
        public string SizeText { get; init; } = string.Empty;
        public string ClassificationText { get; init; } = string.Empty;
        public string Explanation { get; init; } = string.Empty;
        public string AutomationName { get; init; } = string.Empty;

        /// <summary>True, gdy kategoria nie niesie żadnej pewnej sumy (wszystkie wpisy niezmierzone).</summary>
        public bool IsSizeUnknown { get; init; }
    }

    /// <summary>
    /// ViewModel okna „Podsumowanie miejsca” (3.1-08).
    ///
    /// Zakres 3.1 (decyzja przeglądu bezpieczeństwa): WYŁĄCZNIE pomiar i klasyfikacja.
    /// Brak jakiejkolwiek akcji usuwania w UI — nawet gdy bieżący snapshot Core oznaczy
    /// wpisy jako allowlisted, UI przedstawia je jako „tylko pomiar” i wyjaśnia, że
    /// automatyczne sprzątanie nie jest w tym wydaniu rozbudowywane. Istniejący legacy
    /// cleanup (App startup) pozostaje bez zmian i poza tym widokiem.
    /// </summary>
    public sealed class StorageOverviewViewModel : ReactiveObject, IDisposable
    {
        private const string MeasurementOnlyCategoryKey = "UI.Storage.Eligibility.MeasurementOnly";

        private readonly ILocalizationService _loc;
        private readonly IStorageOverviewBackend _backend;
        private readonly Func<StorageOverviewScope> _scopeProvider;

        private long _runId;
        private CancellationTokenSource? _runCts;

        private bool _isMeasuring;
        private string _statusText = string.Empty;
        private string _libraryPathText = string.Empty;
        private bool _isLibraryMissing;
        private string _totalMeasuredText = string.Empty;
        private string _totalNoteText = string.Empty;
        private string _driveFreeText = string.Empty;
        private string _driveTotalText = string.Empty;
        private string _lastMeasuredText = string.Empty;
        private bool _hasFreshMeasurement;

        public StorageOverviewViewModel(
            ILocalizationService localization,
            IStorageOverviewBackend backend,
            Func<StorageOverviewScope> scopeProvider)
        {
            _loc = localization ?? throw new ArgumentNullException(nameof(localization));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
        }

        /// <summary>Wiersze kategorii (przebudowywane po każdym kompletnym pomiarze).</summary>
        public ObservableCollection<StorageOverviewCategoryRow> Categories { get; } = new();

        public bool IsMeasuring
        {
            get => _isMeasuring;
            private set => this.RaiseAndSetIfChanged(ref _isMeasuring, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public string LibraryPathText
        {
            get => _libraryPathText;
            private set => this.RaiseAndSetIfChanged(ref _libraryPathText, value);
        }

        public bool IsLibraryMissing
        {
            get => _isLibraryMissing;
            private set => this.RaiseAndSetIfChanged(ref _isLibraryMissing, value);
        }

        public string TotalMeasuredText
        {
            get => _totalMeasuredText;
            private set => this.RaiseAndSetIfChanged(ref _totalMeasuredText, value);
        }

        public string TotalNoteText
        {
            get => _totalNoteText;
            private set => this.RaiseAndSetIfChanged(ref _totalNoteText, value);
        }

        public string DriveFreeText
        {
            get => _driveFreeText;
            private set => this.RaiseAndSetIfChanged(ref _driveFreeText, value);
        }

        public string DriveTotalText
        {
            get => _driveTotalText;
            private set => this.RaiseAndSetIfChanged(ref _driveTotalText, value);
        }

        public string LastMeasuredText
        {
            get => _lastMeasuredText;
            private set => this.RaiseAndSetIfChanged(ref _lastMeasuredText, value);
        }

        /// <summary>True, gdy co najmniej jeden pomiar zakończył się sukcesem w tej sesji dialogu.</summary>
        public bool HasFreshMeasurement
        {
            get => _hasFreshMeasurement;
            private set => this.RaiseAndSetIfChanged(ref _hasFreshMeasurement, value);
        }

        /// <summary>Prośba o zamknięcie okna (obsługiwana przez View).</summary>
        public event EventHandler? CloseRequested;

        // ── Leniwe komendy (wzorzec jak inne partials MainWindowViewModel) ──

        private ReactiveCommand<Unit, Unit>? _refreshCommand;
        public ReactiveCommand<Unit, Unit> RefreshCommand =>
            _refreshCommand ??= ReactiveCommand.CreateFromTask(async () => await MeasureAsync());

        private ReactiveCommand<Unit, Unit>? _cancelCommand;
        public ReactiveCommand<Unit, Unit> CancelCommand =>
            _cancelCommand ??= ReactiveCommand.Create(CancelMeasurement);

        private ReactiveCommand<Unit, Unit>? _closeCommand;
        public ReactiveCommand<Unit, Unit> CloseCommand =>
            _closeCommand ??= ReactiveCommand.Create(RequestClose);

        /// <summary>Prośba o zamknięcie okna (wywoływana przez przycisk Close lub <see cref="CloseCommand"/>).</summary>
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        // ── Pomiar ──

        /// <summary>
        /// Uruchamia (re)pomiar i klasyfikację biblioteki. Nigdy nie mutuje systemu plików.
        /// Wynik starszego przebiegu nie nadpisuje nowszego (strzeżenie po <c>_runId</c>).
        /// </summary>
        public async Task MeasureAsync(CancellationToken ct = default)
        {
            var run = Interlocked.Increment(ref _runId);

            var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var previous = Interlocked.Exchange(ref _runCts, runCts);
            try { previous?.Cancel(); } catch (ObjectDisposedException) { }

            IsMeasuring = true;
            StatusText = _loc.Get("UI.Storage.Overview.Measuring");

            try
            {
                var scope = _scopeProvider();
                var root = scope.LibraryRoot ?? string.Empty;
                LibraryPathText = root;

                if (!scope.LibraryRootExists)
                {
                    if (IsCurrentRun(run)) ApplyMissingLibrary();
                    return;
                }

                var request = new StorageCleanupPlanRequest
                {
                    LibraryRoot = root,
                    KnownInstallPaths = scope.KnownInstallPaths ?? Array.Empty<string>(),
                    MeasureLibraryTotal = true
                };

                // Preserve Avalonia's UI context before mutating Categories and bound properties.
                var plan = await _backend.PlanAsync(request, runCts.Token);

                if (!IsCurrentRun(run)) return; // późny wynik starego zadania — ignorujemy

                ApplyPlan(plan);
                HasFreshMeasurement = plan.State is StorageSizeState.Complete or StorageSizeState.Partial;
                StatusText = _loc.Get(HasFreshMeasurement ? "UI.Storage.Overview.Ready" : "UI.Storage.Overview.Error");
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentRun(run)) StatusText = _loc.Get("UI.Storage.Overview.Cancelled");
            }
            catch (Exception)
            {
                // Diagnostyka nie jest tekstem UI — UI pokazuje stabilny stan błędu.
                if (IsCurrentRun(run)) StatusText = _loc.Get("UI.Storage.Overview.Error");
            }
            finally
            {
                if (IsCurrentRun(run)) IsMeasuring = false;
                runCts.Dispose();
            }
        }

        /// <summary>Anuluje trwający pomiar. Wiersze i ostatnie wyniki pozostają widoczne.</summary>
        public void CancelMeasurement()
        {
            var cts = _runCts;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            if (IsMeasuring) StatusText = _loc.Get("UI.Storage.Overview.Cancelled");
        }

        /// <summary>
        /// Porządkuje zasoby dialogu: anuluje pomiar, rozprasza leniwe komendy.
        /// Tokenów przebiegów nie Dispose'ujemy wprost — trwające oczekujące
        /// operacje mogą je jeszcze sprawdzać (anulowania wystarczy, resztę przejmie GC).
        /// </summary>
        public void Dispose()
        {
            CancelMeasurement();
            Interlocked.Increment(ref _runId); // żadne późniejsze opóźnione wyniki nie przejdą strażnika

            _refreshCommand?.Dispose();
            _refreshCommand = null;
            _cancelCommand?.Dispose();
            _cancelCommand = null;
            _closeCommand?.Dispose();
            _closeCommand = null;
        }

        private bool IsCurrentRun(long run) => Volatile.Read(ref _runId) == run;

        private void ApplyMissingLibrary()
        {
            IsLibraryMissing = true;
            HasFreshMeasurement = false;
            Categories.Clear();
            TotalMeasuredText = string.Empty; // brak katalogu ≠ „0 B”
            TotalNoteText = _loc.Get("UI.Storage.Overview.LibraryMissing");
            DriveFreeText = FormatSpace(null);
            DriveTotalText = FormatSpace(null);
            LastMeasuredText = string.Empty;
            StatusText = _loc.Get("UI.Storage.Overview.LibraryMissing");
        }

        private void ApplyPlan(StorageCleanupPlan plan)
        {
            IsLibraryMissing = false;
            LibraryPathText = string.IsNullOrWhiteSpace(plan.LibraryRoot) ? LibraryPathText : plan.LibraryRoot;

            var provider = StorageTileSizePresenter.GetFormatProvider(_loc);

            RebuildCategoryRows(plan, provider);

            // Suma biblioteki: jawny stan z planu Core (P2: StorageCleanupPlan.State/ErrorCode).
            // Niedostępność NIE jest zerem — wtedy nie pokazujemy żadnej wartości, tylko wyjaśnienie.
            switch (plan.State)
            {
                case StorageSizeState.Unavailable:
                    TotalMeasuredText = string.Empty;
                    TotalNoteText = plan.ErrorCode switch
                    {
                        StorageSizeErrorCode.PathNotFound or StorageSizeErrorCode.PathEmpty
                            => _loc.Get("UI.Storage.Overview.LibraryMissing"),
                        StorageSizeErrorCode.AccessDenied
                            => _loc.Get("UI.Storage.Overview.TotalNotAccessible"),
                        StorageSizeErrorCode.PathIsLink
                            => _loc.Get("UI.Storage.Overview.TotalIsLink"),
                        _ => _loc.Get("UI.Storage.Overview.TotalUnavailable")
                    };
                    break;

                case StorageSizeState.Partial:
                    TotalMeasuredText = StorageSizeFormatter.Format(plan.MeasuredBytes, provider);
                    TotalNoteText = _loc.Get("UI.Storage.Overview.TotalPartial");
                    break;

                case StorageSizeState.Loading:
                    TotalMeasuredText = string.Empty;
                    TotalNoteText = _loc.Get("UI.Storage.Overview.TotalUnavailable");
                    break;

                default: // Complete albo Stale — suma prawdziwa
                    TotalMeasuredText = StorageSizeFormatter.Format(plan.MeasuredBytes, provider);
                    TotalNoteText = plan.State == StorageSizeState.Stale
                        ? _loc.Get("UI.Storage.Overview.TotalStale")
                        : string.Empty;
                    break;
            }

            DriveFreeText = FormatSpace(plan.FreeDiskSpaceBytes);
            DriveTotalText = FormatSpace(plan.TotalDiskSpaceBytes);

            LastMeasuredText = _loc.GetFormatted(
                "UI.Storage.Overview.LastMeasured",
                plan.GeneratedAtUtc.ToLocalTime().ToString("G", provider));
        }

        private void RebuildCategoryRows(StorageCleanupPlan plan, IFormatProvider provider)
        {
            Categories.Clear();

            foreach (var summary in plan.Categories)
            {
                var entries = plan.Entries.Where(e => e.Category == summary.Category).ToList();
                var unmeasured = entries.Count(e => e.SizeState == StorageSizeState.Unavailable);
                var partial = entries.Count(e => e.SizeState == StorageSizeState.Partial);
                var allUnmeasured = entries.Count > 0 && unmeasured == entries.Count;

                string sizeText = allUnmeasured
                    ? _loc.Get("UI.Storage.Overview.RowUnmeasured")
                    : StorageSizeFormatter.Format(summary.MeasuredBytes, provider);

                // Klasyfikacja w UI 3.1: wszystko jest „tylko pomiar”. Wpisy Allowlisted
                // z bieżącego snapshotu Core celowo NIE dostają osobnej etykiety „do usunięcia”
                // ani żadnej akcji kasowania (brak udowodnionej allowlisty — decyzja przeglądu
                // bezpieczeństwa). Usunięcie dojdzie dopiero wraz z certyfikowanym API Core.
                var classificationText = _loc.Get(MeasurementOnlyCategoryKey);

                var explanation = BuildExplanation(entries, unmeasured, partial);
                var name = _loc.Get(GetCategoryKey(summary.Category));

                Categories.Add(new StorageOverviewCategoryRow
                {
                    Category = summary.Category,
                    Name = name,
                    CountText = _loc.GetFormatted("UI.Storage.Overview.EntryCount", summary.EntryCount),
                    SizeText = sizeText,
                    ClassificationText = classificationText,
                    Explanation = explanation,
                    AutomationName = $"{name}. {sizeText}. {classificationText}",
                    IsSizeUnknown = allUnmeasured
                });
            }
        }

        private string BuildExplanation(IReadOnlyList<StorageCleanupEntry> entries, int unmeasured, int partial)
        {
            var parts = new List<string>();

            var reasonCode = entries
                .Select(e => e.ReasonCode)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            if (!string.IsNullOrWhiteSpace(reasonCode))
                parts.Add(_loc.Get(GetReasonKey(reasonCode!)));

            if (unmeasured > 0)
                parts.Add(_loc.GetFormatted("UI.Storage.Overview.RowUnmeasuredCount", unmeasured));
            if (partial > 0)
                parts.Add(_loc.GetFormatted("UI.Storage.Overview.RowPartialCount", partial));

            return string.Join(Environment.NewLine, parts);
        }

        private string FormatSpace(long? bytes)
        {
            if (!bytes.HasValue)
                return _loc.Get("UI.Storage.Overview.SpaceUnknown");

            return StorageSizeFormatter.Format(bytes.Value, StorageTileSizePresenter.GetFormatProvider(_loc));
        }

        private static string GetCategoryKey(StorageCleanupCategory category) => category switch
        {
            StorageCleanupCategory.ModInstallations => "UI.Storage.Category.ModInstallations",
            StorageCleanupCategory.VanillaArchive => "UI.Storage.Category.VanillaArchive",
            StorageCleanupCategory.VanillaExtractedCache => "UI.Storage.Category.VanillaExtractedCache",
            StorageCleanupCategory.InstallWorkspace => "UI.Storage.Category.InstallWorkspace",
            StorageCleanupCategory.Unclassified => "UI.Storage.Category.Unclassified",
            _ => "UI.Storage.Category.Unknown"
        };

        private static string GetReasonKey(string reasonCode) => "UI.Storage.Reason." + reasonCode;
    }

    /// <summary>
    /// Mapowanie wyniku pomiaru na krótką etykietę kafelka moda (3.1-09).
    /// Wspólny formatter i jednostki IEC z podsumowaniem miejsca (3.1-08).
    /// Stan „niedostępny” nigdy nie jest pokazywany jako 0 B.
    /// </summary>
    public static class StorageTileSizePresenter
    {
        public static string BuildText(ILocalizationService loc, StorageSizeResult result)
        {
            switch (result.State)
            {
                case StorageSizeState.Loading:
                    return loc.Get("UI.Storage.Tile.Calculating");

                case StorageSizeState.Unavailable:
                    return result.ErrorCode switch
                    {
                        StorageSizeErrorCode.PathNotFound or StorageSizeErrorCode.PathEmpty
                            => loc.Get("UI.Storage.Tile.FolderMissing"),
                        StorageSizeErrorCode.AccessDenied
                            => loc.Get("UI.Storage.Tile.NotAccessible"),
                        StorageSizeErrorCode.PathIsLink
                            => loc.Get("UI.Storage.Tile.IsLink"),
                        _ => loc.Get("UI.Storage.Tile.Unavailable")
                    };

                // Wartość pokazujemy tylko gdy pomiar ją faktycznie wyznaczył (Core: HasMeasuredValue).
                // Stan „z wartością bez wartości” nie istnieje dla serwisu — defensywnie: neutralny stan.
                case StorageSizeState.Stale when result.HasMeasuredValue:
                    return loc.GetFormatted("UI.Storage.Tile.Stale", Format(result.LogicalBytes, loc));

                case StorageSizeState.Partial when result.HasMeasuredValue:
                    return loc.GetFormatted("UI.Storage.Tile.Partial", Format(result.LogicalBytes, loc));

                default: // Complete z wyznaczoną wartością — pusty katalog to prawdziwe „0 B”
                    return result.HasMeasuredValue
                        ? Format(result.LogicalBytes, loc)
                        : loc.Get("UI.Storage.Tile.Unavailable");
            }
        }

        public static string Format(long bytes, ILocalizationService loc)
            => StorageSizeFormatter.Format(bytes, GetFormatProvider(loc));

        /// <summary>Kultura formatowania liczb = język aplikacji (PL: „1,5 KiB”), z bezpiecznym fallbackiem.</summary>
        public static IFormatProvider GetFormatProvider(ILocalizationService loc)
        {
            var code = loc.CurrentCulture;
            if (!string.IsNullOrWhiteSpace(code))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(code.Trim());
                }
                catch (CultureNotFoundException)
                {
                    // fallback poniżej
                }
            }

            return CultureInfo.InvariantCulture;
        }
    }
}

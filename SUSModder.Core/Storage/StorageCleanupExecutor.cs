namespace SUSModder.Core.Storage;

/// <summary>
/// Wykonawca planu sprzątania — w 3.1-08 **nie usuwa niczego**.
///
/// Decyzja: 3.1-08 pozostaje pomiarowo-tylko. Bezpieczeństwo usunięcia nie zostało dowiedzione
/// (brak mechanizmu własności/lease dla katalogów roboczych, niezweryfikowana retencja archiwów
/// vanilla dla wszystkich platform i wersji), więc nie ma zatwierdzonej allowlisty.
///
/// Klasa pozostaje w API, żeby warstwa UI mogła wywołać wykonanie i pokazać spójny wynik:
/// każdy wpis zwraca <see cref="StorageCleanupOutcome.Skipped"/> z powodem
/// <see cref="StorageCleanupSkipReason.SafetyNotProven"/>, a liczniki usunięcia są zerowe.
///
/// UWAGA: nie wolno w tej klasie przywrócić kasowania bez zamknięcia bramek z analizy 3.1-08
/// (inwentaryzacja, dowód własności, blokada aktywnych operacji, testy bezpieczeństwa).
/// Istniejące automatyczne sprzątanie (<c>ModsStorageCleanupService</c>) pozostaje nietknięte
/// i niezależne od tej klasy.
/// </summary>
public sealed class StorageCleanupExecutor
{
    /// <summary>
    /// Tworzy wykonawcę. Opcjonalny serwis pomiaru jest akceptowany dla zgodności API,
    /// ale nie jest używany — nic nie jest usuwane, więc nie ma czego unieważniać.
    /// </summary>
    public StorageCleanupExecutor(IStorageSizeMeasurementService? measurement = null)
    {
        _ = measurement;
    }

    /// <summary>
    /// „Wykonuje" plan. Zawsze zwraca pominięcie każdej pozycji z powodem
    /// <see cref="StorageCleanupSkipReason.SafetyNotProven"/> — żaden plik nie jest usuwany.
    /// </summary>
    public Task<StorageCleanupExecutionResult> ExecuteAsync(
        StorageCleanupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var cancelled = cancellationToken.IsCancellationRequested;

        var outcomes = plan.Entries
            .Select(entry => new StorageCleanupEntryOutcome
            {
                Entry = entry,
                Outcome = StorageCleanupOutcome.Skipped,
                Reason = cancelled
                    ? StorageCleanupSkipReason.Cancelled
                    : StorageCleanupSkipReason.SafetyNotProven,
                DiagnosticMessage = entry.ReasonCode ?? StorageCleanupRules.ReasonSafetyNotProven
            })
            .ToList();

        return Task.FromResult(new StorageCleanupExecutionResult
        {
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            Outcomes = outcomes,
            RemovedCount = 0,
            SkippedCount = outcomes.Count,
            FailedCount = 0,
            RemovedBytes = 0,
            Cancelled = cancelled
        });
    }
}

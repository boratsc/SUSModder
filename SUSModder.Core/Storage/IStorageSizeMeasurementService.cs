namespace SUSModder.Core.Storage;

/// <summary>
/// Wspólny serwis pomiaru rozmiaru dla kafelków moda (3.1-09) i podsumowania miejsca (3.1-08).
///
/// Zasady:
/// - mierzone są logiczne długości plików (nie fizyczna alokacja),
/// - wynik to bajty + stan (loading/complete/partial/unavailable/stale), nigdy sam long z domyślnym zerem,
/// - cache w pamięci per kanoniczny katalog, deduplikacja pracy, ograniczona współbieczność, cancellation,
/// - reparse pointy (junction/symlink) nigdy nie są otwierane — pomiar ścieżki prowadzącej przez link jest odrzucany,
/// - unieważnienie po operacjach zarządzanych; wynik starszego zadania nie nadpisuje świeższego.
/// </summary>
public interface IStorageSizeMeasurementService
{
    /// <summary>
    /// Mierzy rozmiar katalogu (lub pojedynczego pliku). Zwraca bajty wraz ze stanem pomiaru.
    /// </summary>
    Task<StorageSizeResult> MeasureAsync(
        string rootPath,
        StorageMeasurementOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mierzy wiele katalogów i zwraca sumę z deduplikacją identycznych oraz zagnieżdżonych ścieżek.
    /// </summary>
    Task<StorageRootMeasurement> MeasureManyAsync(
        IEnumerable<string> rootPaths,
        StorageMeasurementOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Zwraca ostatni znany wynik pomiaru bez uruchamiania nowego (albo null, gdy ścieżka nie była mierzona).
    /// </summary>
    StorageSizeResult? GetSnapshot(string rootPath);

    /// <summary>
    /// Zwraca stan do pokazania w UI: świeżą wartość, wartość oznaczoną jako nieaktualną
    /// albo stan „trwa pomiar”. Nie uruchamia pomiaru.
    /// </summary>
    StorageSizeResult GetDisplaySnapshot(string rootPath);

    /// <summary>Czy dla tej ścieżki trwa pomiar.</summary>
    bool IsMeasurementInProgress(string rootPath);

    /// <summary>
    /// Bieżąca rewizja pomiaru dla ścieżki. Rośnie przy każdym nowym zleceniu i przy unieważnieniu,
    /// więc UI porównuje z nią <see cref="StorageSizeResult.Revision"/> i odrzuca wynik starszego zadania.
    /// </summary>
    long GetRevision(string rootPath);

    /// <summary>Unieważnia pomiar ścieżki (po instalacji, aktualizacji, odinstalowaniu, sprzątaniu).</summary>
    void Invalidate(string rootPath);

    /// <summary>Unieważnia pomiar ścieżki wraz ze wszystkimi zapisanymi pod nią.</summary>
    void InvalidateUnder(string rootPath);

    /// <summary>Unieważnia wszystkie zapisane pomiary.</summary>
    void InvalidateAll();
}

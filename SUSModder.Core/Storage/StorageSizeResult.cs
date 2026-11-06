namespace SUSModder.Core.Storage;

/// <summary>
/// Wynik pomiaru rozmiaru jednego katalogu (lub pojedynczego pliku).
/// Zawiera wyłącznie logiczne długości plików — nie jest to fizyczna alokacja na dysku
/// ani gwarancja odzyskanego miejsca.
/// </summary>
public sealed record StorageSizeResult
{
    /// <summary>Kanoniczna (znormalizowana) ścieżka mierzona.</summary>
    public required string RootPath { get; init; }

    /// <summary>Suma logicznych długości plików (bajty). 0 oznacza „brak danych”, nie „brak plików” — patrz <see cref="HasValue"/>.</summary>
    public long LogicalBytes { get; init; }

    /// <summary>Stan pomiaru.</summary>
    public StorageSizeState State { get; init; } = StorageSizeState.Loading;

    /// <summary>Kod błędu (jeśli pomiar nie jest kompletny).</summary>
    public StorageSizeErrorCode ErrorCode { get; init; } = StorageSizeErrorCode.None;

    /// <summary>Liczba policzonych plików.</summary>
    public int FileCount { get; init; }

    /// <summary>Liczba odwiedzonych katalogów (łącznie z korzeniem).</summary>
    public int DirectoryCount { get; init; }

    /// <summary>Liczba pominiętych wpisów (brak dostępu, reparse point, brak pliku).</summary>
    public int SkippedEntryCount { get; init; }

    /// <summary>Liczba pominiętych reparse pointów (junction/symlink — nigdy nie są otwierane).</summary>
    public int SkippedReparsePointCount { get; init; }

    /// <summary>
    /// True, gdy sama ścieżka korzenia jest reparse pointem (junction/symlink).
    /// Taki korzeń jest odrzucany — pomiar zwraca wtedy <see cref="StorageSizeState.Unavailable"/>.
    /// Pole pozostaje dla pełności informacji o diagnostyce.
    /// </summary>
    public bool RootIsReparsePoint { get; init; }

    /// <summary>
    /// True, gdy pomiar faktycznie wyznaczył wartość (stan <see cref="StorageSizeState.Complete"/>
    /// lub <see cref="StorageSizeState.Partial"/>). Niezależne od aktualności wpisu w cache —
    /// dzięki temu wygasły lub unieważniony wynik niedostępny nie staje się „stale z zerem”.
    /// </summary>
    public bool HasMeasuredValue { get; init; }

    /// <summary>Moment pomiaru (UTC).</summary>
    public DateTimeOffset MeasuredAtUtc { get; init; }

    /// <summary>Czas trwania pomiaru.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Monotoniczna rewizja pomiaru dla tej ścieżki. Rośnie przy każdym nowym zleceniu
    /// i przy unieważnieniu — dzięki temu wynik starszego zadania nie nadpisze świeższego.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Techniczny komunikat diagnostyczny (nie jest tekstem UI — UI lokalizuje po kodzie).
    /// Może zawierać ścieżkę lokalną, dlatego nie wolno go wysyłać do telemetrii.
    /// </summary>
    public string? DiagnosticMessage { get; init; }

    /// <summary>
    /// Czy wynik niesie użyteczną wartość. Dla <see cref="StorageSizeState.Unavailable"/>
    /// i <see cref="StorageSizeState.Loading"/> jest zawsze false — niedostępność nie jest zerem.
    /// </summary>
    public bool HasValue => HasMeasuredValue && State != StorageSizeState.Unavailable;

    /// <summary>Czy suma jest kompletna (pomiar nie pominął żadnego wpisu).</summary>
    public bool IsComplete => State == StorageSizeState.Complete;

    /// <summary>Tworzy wynik w stanie „trwa pomiar”.</summary>
    public static StorageSizeResult Loading(string rootPath, long revision = 0) => new()
    {
        RootPath = rootPath,
        State = StorageSizeState.Loading
    };

    /// <summary>Tworzy wynik w stanie „pomiar niemożliwy”.</summary>
    public static StorageSizeResult Unavailable(
        string rootPath,
        StorageSizeErrorCode errorCode,
        string? diagnosticMessage = null,
        long revision = 0) => new()
    {
        RootPath = rootPath,
        State = StorageSizeState.Unavailable,
        ErrorCode = errorCode,
        HasMeasuredValue = false,
        DiagnosticMessage = diagnosticMessage,
        Revision = revision
    };
}

/// <summary>
/// Wynik pomiaru wielu katalogów — suma z deduplikacją identycznych i zagnieżdżonych ścieżek.
/// </summary>
public sealed record StorageRootMeasurement
{
    /// <summary>Wyniki pomiaru poszczególnych (zdeduplikowanych) korzeni.</summary>
    public required IReadOnlyList<StorageSizeResult> Roots { get; init; }

    /// <summary>Kanoniczne ścieżki wzięte do sumy.</summary>
    public required IReadOnlyList<string> IncludedRootPaths { get; init; }

    /// <summary>Ścieżki pominięte jako duplikaty lub zagnieżdżone w innym korzeniu.</summary>
    public required IReadOnlyList<string> DeduplicatedRootPaths { get; init; }

    /// <summary>Suma logicznych bajtów (bez podwójnego liczenia zagnieżdżonych korzeni).</summary>
    public long TotalLogicalBytes { get; init; }

    /// <summary>
    /// Stan zbiorczy. <see cref="StorageSizeState.Partial"/> oznacza, że choć jeden korzeń
    /// był niepełny albo niedostępny — suma jest wtedy zaniżona.
    /// </summary>
    public StorageSizeState State { get; init; } = StorageSizeState.Complete;

    /// <summary>Liczba korzeni z niepełnym pomiarem.</summary>
    public int PartialRootCount { get; init; }

    /// <summary>Liczba korzeni, których nie dało się zmierzyć.</summary>
    public int UnavailableRootCount { get; init; }

    /// <summary>Moment pomiaru (UTC).</summary>
    public DateTimeOffset MeasuredAtUtc { get; init; }

    /// <summary>Czy choć jeden korzeń wyznaczył wartość.</summary>
    public bool HasMeasuredValue => Roots.Any(r => r.HasMeasuredValue);

    /// <summary>Czy suma niesie użyteczną wartość.</summary>
    public bool HasValue => State != StorageSizeState.Unavailable && HasMeasuredValue;
}

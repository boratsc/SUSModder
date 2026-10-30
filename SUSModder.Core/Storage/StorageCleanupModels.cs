namespace SUSModder.Core.Storage;

/// <summary>Kategoria danych w katalogu modów — używana przez podsumowanie miejsca (3.1-08).</summary>
public enum StorageCleanupCategory
{
    /// <summary>Nieznana kategoria.</summary>
    Unknown = 0,

    /// <summary>Instalacje modów — dane chronione, nigdy nie usuwane.</summary>
    ModInstallations = 1,

    /// <summary>Archiwa vanilla (7z) w katalogu cache vanilla.</summary>
    VanillaArchive = 2,

    /// <summary>Rozpakowany cache vanilla — pozostawiany celowo, chroniony.</summary>
    VanillaExtractedCache = 3,

    /// <summary>Katalogi robocze instalacji (katalog <c>temp/</c>) — pliki do ponownego pobrania.</summary>
    InstallWorkspace = 4,

    /// <summary>Dane nieklasyfikowane — bez właściciela i bez potwierdzonego bezpieczeństwa.</summary>
    Unclassified = 5
}

/// <summary>Czy kategorię można usunąć, czy tylko zmierzyć.</summary>
public enum StorageCleanupEligibility
{
    /// <summary>
    /// Tylko pomiar. Brak dowodu, że usunięcie jest bezpieczne (np. brak dowodu braku aktywnego użycia).
    /// W 3.1-08 to domyślny stan dla wszystkich kategorii.
    /// </summary>
    MeasurementOnly = 0,

    /// <summary>
    /// Kategoria z allowlisty — po walidacji może zostać wykonana.
    /// W 3.1-08 żadna kategoria nie ma tego uprawnienia (allowlista nie została zatwierdzona).
    /// </summary>
    Allowlisted = 1,

    /// <summary>Dane chronione (instalacje, konfiguracje, logi, cache, backupy) — nigdy nie usuwane.</summary>
    Protected = 2
}

/// <summary>Powód pominięcia lub odmowy. Stabilne kody — UI lokalizuje.</summary>
public enum StorageCleanupSkipReason
{
    /// <summary>Brak powodu — kwalifikacja pozytywna.</summary>
    None = 0,

    /// <summary>Kategoria nie jest w allowlisty.</summary>
    NotAllowlisted = 1,

    /// <summary>Nie da się dowieść braku aktywnego użycia (brak mechanizmu własności/lease).</summary>
    ActiveUseNotProvable = 2,

    /// <summary>Nie da się dowieść własności pliku/katalogu.</summary>
    OwnershipNotProvable = 3,

    /// <summary>Warunek poprawności cache nie jest spełniony (brak markera, brak plików gry).</summary>
    CacheNotProven = 4,

    /// <summary>Dane chronione — poza zakresem sprzątania.</summary>
    ProtectedData = 5,

    /// <summary>Ścieżka zmieniła się od czasu planu (inny rozmiar/mtime) — pominięto.</summary>
    PathChangedSincePlan = 6,

    /// <summary>Ścieżka już nie istnieje.</summary>
    Missing = 7,

    /// <summary>Brak uprawnień.</summary>
    AccessDenied = 8,

    /// <summary>Błąd wejścia/wyjścia.</summary>
    IoError = 9,

    /// <summary>Operacja anulowana.</summary>
    Cancelled = 10,

    /// <summary>
    /// Bezpieczeństwo usunięcia nie zostało dowiedzione — 3.1-08 pozostaje pomiarowo-tylko.
    /// Executor nie usuwa niczego i zwraca ten powód.
    /// </summary>
    SafetyNotProven = 11,

    /// <summary>Ścieżka prowadzi przez reparse point (junction/symlink) — nie jest mierzona ani usuwana.</summary>
    PathIsLink = 12
}

/// <summary>Wynik wykonania pojedynczego wpisu planu.</summary>
public enum StorageCleanupOutcome
{
    /// <summary>Wpis nie był kandydatem do wykonania (kategoria poza allowlistą).</summary>
    NotAttempted = 0,

    /// <summary>Wpis usunięty (w 3.1-08 nie występuje — executor niczego nie usuwa).</summary>
    Removed = 1,

    /// <summary>Wpis pominięty (walidacja nie przeszła, bezpieczeństwo nieudowodnione albo anulowano).</summary>
    Skipped = 2,

    /// <summary>Próba usunięcia zakończyła się błędem.</summary>
    Failed = 3
}

/// <summary>Pojedynczy wpis planu sprzątania.</summary>
public sealed record StorageCleanupEntry
{
    /// <summary>Stabilny identyfikator wpisu (kategoria + ścieżka względna).</summary>
    public required string Id { get; init; }

    /// <summary>Kategoria danych.</summary>
    public required StorageCleanupCategory Category { get; init; }

    /// <summary>Kanoniczna ścieżka wpisu.</summary>
    public required string Path { get; init; }

    /// <summary>Właściciel/katalog nadrzędny (np. katalog cache vanilla dla archiwum).</summary>
    public string? OwnerRoot { get; init; }

    /// <summary>
    /// Czy wpis może zostać usunięty. W 3.1-08 zawsze <see cref="StorageCleanupEligibility.MeasurementOnly"/>
    /// albo <see cref="StorageCleanupEligibility.Protected"/> — allowlista nie została zatwierdzona.
    /// </summary>
    public StorageCleanupEligibility Eligibility { get; init; } = StorageCleanupEligibility.MeasurementOnly;

    /// <summary>Powód pominięcia (gdy nie kwalifikuje się do usunięcia).</summary>
    public StorageCleanupSkipReason SkipReason { get; init; } = StorageCleanupSkipReason.SafetyNotProven;

    /// <summary>Stabilny kod powodu — UI lokalizuje po tym kodzie.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Zmierzony rozmiar logiczny wpisu (bajty).</summary>
    public long MeasuredBytes { get; init; }

    /// <summary>Stan pomiaru rozmiaru wpisu.</summary>
    public StorageSizeState SizeState { get; init; } = StorageSizeState.Loading;

    /// <summary>Kod błędu pomiaru rozmiaru wpisu.</summary>
    public StorageSizeErrorCode SizeErrorCode { get; init; } = StorageSizeErrorCode.None;

    /// <summary>
    /// True, gdy wpis jest reparse pointem (junction/symlink). Takich wpisów nie mierzymy
    /// i nigdy nie podnosimy do korzenia pomiaru — groziłoby to skanowaniem obcego drzewa.
    /// </summary>
    public bool IsLink { get; init; }

    /// <summary>Rozmiar pliku obserwowany w chwili planowania (do rewalidacji przed usunięciem).</summary>
    public long? ObservedLength { get; init; }

    /// <summary>Czas ostatniej modyfikacji obserwowany w chwili planowania.</summary>
    public DateTimeOffset? ObservedLastWriteTimeUtc { get; init; }

    /// <summary>Czy pomiar rozmiaru tej ścieżki był niepełny.</summary>
    public bool IsSizePartial => SizeState == StorageSizeState.Partial;
}

/// <summary>Podsumowanie jednej kategorii w planie sprzątania.</summary>
public sealed record StorageCleanupCategorySummary
{
    /// <summary>Kategoria.</summary>
    public required StorageCleanupCategory Category { get; init; }

    /// <summary>Liczba wpisów.</summary>
    public int EntryCount { get; init; }

    /// <summary>Zmierzony rozmiar kategorii (bajty).</summary>
    public long MeasuredBytes { get; init; }

    /// <summary>
    /// Rozmiar, który można odzyskać po potwierdzeniu planu. W 3.1-08 zawsze 0 —
    /// allowlista nie została zatwierdzona, więc nic nie jest warte usunięcia.
    /// </summary>
    public long RecoverableBytes { get; init; }

    /// <summary>Najwyższe uprawnienie w tej grupie.</summary>
    public StorageCleanupEligibility Eligibility { get; init; } = StorageCleanupEligibility.Protected;
}

/// <summary>Plan sprzątania — lista, powody kwalifikacji, szacunek i wykluczenia.</summary>
public sealed record StorageCleanupPlan
{
    /// <summary>Kanoniczny katalog biblioteki modów.</summary>
    public required string LibraryRoot { get; init; }

    /// <summary>Moment utworzenia planu (UTC).</summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Wszystkie wykryte wpisy (podsumowanie i potencjalnie zbędne).</summary>
    public required IReadOnlyList<StorageCleanupEntry> Entries { get; init; }

    /// <summary>Podsumowanie per kategoria.</summary>
    public required IReadOnlyList<StorageCleanupCategorySummary> Categories { get; init; }

    /// <summary>
    /// Stan planu na poziomie biblioteki. <see cref="StorageSizeState.Unavailable"/> oznacza,
    /// że biblioteki nie dało się w ogóle zinwentaryzować — wtedy „0 B” nie jest poprawną odpowiedzią.
    /// </summary>
    public StorageSizeState State { get; init; } = StorageSizeState.Complete;

    /// <summary>Kod błędu inwentaryzacji (gdy <see cref="State"/> to Unavailable).</summary>
    public StorageSizeErrorCode ErrorCode { get; init; } = StorageSizeErrorCode.None;

    /// <summary>
    /// Techniczny komunikat diagnostyczny o inwentaryzacji (nie jest tekstem UI).
    /// Może zawierać ścieżkę lokalną — nie wolno go wysyłać do telemetrii.
    /// </summary>
    public string? DiagnosticMessage { get; init; }

    /// <summary>Łączny zmierzony rozmiar biblioteki (bajty).</summary>
    public long MeasuredBytes { get; init; }

    /// <summary>
    /// Szacunkowe miejsce do odzyskania — tylko pozycje z allowlisty (bajty).
    /// W 3.1-08 zawsze 0: allowlista nie została zatwierdzona.
    /// </summary>
    public long EstimatedRecoverableBytes { get; init; }

    /// <summary>Wolne miejsce na wolumenie biblioteki (bajty), jeśli udało się odczytać.</summary>
    public long? FreeDiskSpaceBytes { get; init; }

    /// <summary>Całkowity rozmiar wolumenu biblioteki (bajty), jeśli udało się odczytać.</summary>
    public long? TotalDiskSpaceBytes { get; init; }

    /// <summary>
    /// True, gdy w planie nie ma żadnej pozycji, którą można bezpiecznie usunąć.
    /// W 3.1-08 to zawsze true — aplikacja pokazuje sam pomiar, bez akcji kasowania.
    /// </summary>
    public bool IsMeasurementOnly => !Entries.Any(e => e.Eligibility == StorageCleanupEligibility.Allowlisted);

    /// <summary>Pozycje, które przeszły kwalifikację i mogą zostać wykonane po potwierdzeniu.</summary>
    public IReadOnlyList<StorageCleanupEntry> RemovableEntries =>
        Entries.Where(e => e.Eligibility == StorageCleanupEligibility.Allowlisted).ToList();
}

/// <summary>Wynik wykonania jednego wpisu planu.</summary>
public sealed record StorageCleanupEntryOutcome
{
    /// <summary>Wpis planu.</summary>
    public required StorageCleanupEntry Entry { get; init; }

    /// <summary>Wynik operacji.</summary>
    public required StorageCleanupOutcome Outcome { get; init; }

    /// <summary>Powód pominięcia lub odmowy.</summary>
    public StorageCleanupSkipReason Reason { get; init; } = StorageCleanupSkipReason.None;

    /// <summary>Techniczny komunikat diagnostyczny (nie jest tekstem UI).</summary>
    public string? DiagnosticMessage { get; init; }
}

/// <summary>Wynik wykonania planu.</summary>
public sealed record StorageCleanupExecutionResult
{
    /// <summary>Moment wykonania (UTC).</summary>
    public required DateTimeOffset ExecutedAtUtc { get; init; }

    /// <summary>Wynik każdego wpisu planu.</summary>
    public required IReadOnlyList<StorageCleanupEntryOutcome> Outcomes { get; init; }

    /// <summary>Liczba usuniętych wpisów (w 3.1-08 zawsze 0).</summary>
    public int RemovedCount { get; init; }

    /// <summary>Liczba pominiętych wpisów.</summary>
    public int SkippedCount { get; init; }

    /// <summary>Liczba wpisów z błędem.</summary>
    public int FailedCount { get; init; }

    /// <summary>Łączny rozmiar usuniętych plików (bajty) — w 3.1-08 zawsze 0.</summary>
    public long RemovedBytes { get; init; }

    /// <summary>Czy wykonanie zostało przerwane (anulowanie).</summary>
    public bool Cancelled { get; init; }
}

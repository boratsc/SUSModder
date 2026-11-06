namespace SUSModder.Core.Storage;

/// <summary>Opcje pojedynczego pomiaru.</summary>
public sealed record StorageMeasurementOptions
{
    /// <summary>Domyślne opcje — pomiar z cache, ważność cache z ustawień serwisu.</summary>
    public static StorageMeasurementOptions Default { get; } = new();

    /// <summary>
    /// Maksymalny wiek wpisu w cache, po którym wynik jest uznawany za nieaktualny.
    /// <c>null</c> — użyj wartości domyślnej serwisu. Wartość ujemna — wpis nigdy nie przepada.
    /// </summary>
    public TimeSpan? MaxCacheAge { get; init; }

    /// <summary>Pomija cache i mierzy ponownie (przycisk odświeżenia / ręczny pomiar).</summary>
    public bool ForceRefresh { get; init; }

    /// <summary>
    /// Granica zaufania dla kontraktu „no-follow”. Przodkowie mierzonej ścieżki są sprawdzani
    /// pod kątem reparse pointów tylko do tej granicy (włącznie). Powyżej granicy decyzję
    /// o zaufanym katalogu podejmuje wywołujący.
    ///
    /// Dla pomiarów w bibliotece modów należy podać katalog biblioteki — dzięki temu junction
    /// powyżej biblioteki nie blokuje pomiaru, a junction wewnątrz biblioteki blokuje.
    /// Puste (null) oznacza sprawdzenie wszystkich przodków aż do korzenia woluminu.
    /// </summary>
    public string? TrustedRoot { get; init; }
}

using SUSModder.Core.Api.Models;

namespace SUSModder.Core.Services;

/// <summary>
/// Zmiana przypisania ikony zgłoszona po synchronizacji katalogu (3.1-04).
/// </summary>
public sealed class IconAssignmentChange
{
    /// <summary>Stabilny klucz przypisania (np. <c>mod:42</c>, <c>global:vanilla</c>).</summary>
    public string AssignmentKey { get; init; } = string.Empty;

    /// <summary>
    /// Oczekiwana referencja zasobu. Null oznacza trwały tombstone: backend potwierdził
    /// w schemacie 1, że przypisanie zostało jawnie usunięte — UI ma pokazać placeholder,
    /// a nie starszą ikonę legacy.
    /// </summary>
    public CatalogIconDto? Reference { get; init; }

    /// <summary>True, gdy przypisanie zostało jawnie usunięte (tombstone).</summary>
    public bool IsRemoval => Reference is null;
}

/// <summary>
/// Argumenty zdarzenia zmian przypisań ikon (3.1-04). Zdarzenie jest wywoływane
/// na wątku synchronizacji — UI musi przeprowadzić operacje na wątku interfejsu.
/// </summary>
public sealed class IconAssignmentsChangedEventArgs : EventArgs
{
    /// <summary>Zmienione przypisania (tylko te, które faktycznie się zmieniły).</summary>
    public IReadOnlyList<IconAssignmentChange> Changes { get; init; } = Array.Empty<IconAssignmentChange>();

    /// <summary>
    /// True, gdy kontrakt wersjonowany (schemat 1) jest aktywny — UI może używać
    /// referencji z <see cref="Changes"/> i przypisań pobranych z repozytorium.
    /// </summary>
    public bool IsSchemaActive { get; init; }

    /// <summary>
    /// True, gdy backend potwierdził (odpowiedź udana) brak obsługiwanego schematu:
    /// UI ma wyczyścić wszystkie klucze wersjonowane i wrócić do ścieżki legacy.
    /// Nie jest ustawiane przy błędzie sieci — błąd zachowuje ostatni dobry stan.
    /// </summary>
    public bool ClearVersionedIcons { get; init; }

    /// <summary>
    /// True, gdy w tej synchronizacji zmieniła się aktywność schematu kontraktu
    /// (0 → 1 albo 1 → 0). Zdarzenie jest wtedy zgłaszane nawet wtedy, gdy lista
    /// zmian jest pusta — UI musi ponownie zdecydować o wiązaniu ikony wersjonowanej
    /// i wczytać pełną migawkę przypisań.
    /// </summary>
    public bool SchemaTransition { get; init; }
}

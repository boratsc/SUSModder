namespace SUSModder.Views;

/// <summary>
/// Tryb kreatora modpacków.
/// </summary>
public enum ModPackCreatorMode
{
    /// <summary>Instaluje nowy zestaw jako lokalną instancję z katalogu modów.</summary>
    InstallLocal,

    /// <summary>Udostępnia istniejący lokalny zestaw online (pre-fill z instancji).</summary>
    ShareExisting,

    /// <summary>Tworzy lokalną instancję, a następnie od razu ją udostępnia online.</summary>
    CreateAndShare
}

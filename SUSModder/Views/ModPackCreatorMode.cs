namespace SUSModder.Views;

/// <summary>
/// Tryb kreatora modpacków: udostępnienie online (API) lub utworzenie lokalnej instancji.
/// </summary>
public enum ModPackCreatorMode
{
    /// <summary>Mapuje wybraną instancję (lub legacy InstallPath) na kod/link API.</summary>
    ShareOnline,

    /// <summary>Instaluje nowy zestaw jako lokalną instancję z katalogu modów.</summary>
    InstallLocal
}

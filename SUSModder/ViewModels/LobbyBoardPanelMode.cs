namespace SUSModder.ViewModels;

/// <summary>
/// Tryb wyświetlania panelu lobby: pełny modal lub uproszczony podgląd w inspektorze.
/// </summary>
public enum LobbyBoardPanelMode
{
    /// <summary>Modal z kodami, zakładkami i publikacją kodów.</summary>
    Full,

    /// <summary>Inspektor — głównie chat (bez dodawania kodów).</summary>
    InspectorEmbed
}

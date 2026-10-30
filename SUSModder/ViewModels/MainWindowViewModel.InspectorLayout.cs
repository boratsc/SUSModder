namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>
    /// Reset sekcji inspektora przy zmianie/odznaczeniu moda.
    /// Czat ogólny jest celowo poza tym resetem — jeden stały panel okna,
    /// niezależny od zaznaczenia.
    /// </summary>
    private void ResetInspectorSections()
    {
        IsCatalogCompatibleDllSectionExpanded = false;
    }
}

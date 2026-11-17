using System;

namespace SUSModder.ViewModels;

/// <summary>
/// Czysta logika decyzji układu zakotwiczonego czatu (progi i matematyka szerokości) —
/// wyciągnięta z MainWindow, żeby dała się testować bez Avalonii (repo nie ma Headless).
/// Wszystkie szerokości w DIP.
/// </summary>
public static class ChatDockingLayout
{
    /// <summary>Szerokość stałej kolumny czatu.</summary>
    public const double ChatColumnWidth = 520;

    /// <summary>Minimalna szerokość okna dla dokowania: MinWidth 900 + kolumna 520.</summary>
    public const double MinDockedWindowWidth = 1420;

    /// <summary>Próg wąskich szczegółów (overlay z powrotem) liczony od części aplikacji.</summary>
    public const double DetailsNarrowThreshold = 1100;

    /// <summary>Dokowanie efektywne = preferencja + miejsce na pełną aplikację i kolumnę.</summary>
    public static bool IsDocked(bool anchored, double windowWidth) =>
        anchored && windowWidth >= MinDockedWindowWidth;

    /// <summary>Szerokość części aplikacji bez kolumny czatu (gdy zadokowana).</summary>
    public static double AppPartWidth(bool docked, double windowWidth) =>
        docked ? windowWidth - ChatColumnWidth : windowWidth;

    /// <summary>Szczegóły side-by-side wymagają 1100 DIP części aplikacji (1620 z kolumną).</summary>
    public static bool IsDetailsNarrow(bool docked, double windowWidth) =>
        AppPartWidth(docked, windowWidth) < DetailsNarrowThreshold;

    /// <summary>Poszerzenie o kolumnę z clampem do obszaru roboczego monitora (DIP).</summary>
    public static double WidenedWidth(double currentWidth, double workAreaWidthDip) =>
        Math.Min(currentWidth + ChatColumnWidth, workAreaWidthDip);

    /// <summary>Cofnięcie tylko gdy bieżąca szerokość to wciąż ta nadana automatycznie (ręczny resize → nie ruszaj).</summary>
    public static bool ShouldRevertAutoWidth(double currentWidth, double? autoGrantedWidth) =>
        autoGrantedWidth is double granted && Math.Abs(currentWidth - granted) < 0.5;
}

using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Zakotwiczenie czatu: czysta logika decyzji układu (progi 1420/1620, matematyka szerokości,
/// warunek cofnięcia) — testowana bez Avalonii, bo repo nie ma Avalonia.Headless.
/// </summary>
public sealed class ChatDockingLayoutTests
{
    [Theory]
    [InlineData(false, 2000, false)] // preferencja wyłączona → nakładka nawet przy dużym oknie
    [InlineData(true, 1419, false)]  // za wąskie o 1 DIP
    [InlineData(true, 1420, true)]   // próg: MinWidth 900 + kolumna 520
    [InlineData(true, 1920, true)]
    [InlineData(true, 0, false)]     // okno jeszcze niezmierzone
    public void IsDocked_RequiresPreferenceAndWidth(bool anchored, double width, bool expected) =>
        Assert.Equal(expected, ChatDockingLayout.IsDocked(anchored, width));

    [Theory]
    [InlineData(false, 1099, true)]  // bez dokowania: próg części aplikacji = próg okna
    [InlineData(false, 1100, false)]
    [InlineData(true, 1619, true)]   // z dokowaniem: 1619 - 520 = 1099 < 1100
    [InlineData(true, 1620, false)]  // 1620 - 520 = 1100 → szczegóły side-by-side
    [InlineData(true, 1420, true)]   // dokowanie bez miejsca na szczegóły obok
    public void DetailsNarrow_IsMeasuredOnAppPart(bool docked, double width, bool expected) =>
        Assert.Equal(expected, ChatDockingLayout.IsDetailsNarrow(docked, width));

    [Fact]
    public void AppPartWidth_SubtractsColumnOnlyWhenDocked()
    {
        Assert.Equal(1500, ChatDockingLayout.AppPartWidth(false, 1500));
        Assert.Equal(980, ChatDockingLayout.AppPartWidth(true, 1500));
    }

    [Theory]
    [InlineData(1270, 1920, 1790)] // normalne poszerzenie o 520
    [InlineData(1270, 1600, 1600)] // clamp do obszaru roboczego monitora
    [InlineData(1600, 1600, 1600)] // już na maksie → bez zmiany
    public void WidenedWidth_AddsColumn_ClampedToWorkArea(double current, double workArea, double expected) =>
        Assert.Equal(expected, ChatDockingLayout.WidenedWidth(current, workArea));

    [Fact]
    public void ShouldRevertAutoWidth_OnlyWhenWidthUntouched()
    {
        Assert.True(ChatDockingLayout.ShouldRevertAutoWidth(1790, 1790));
        Assert.True(ChatDockingLayout.ShouldRevertAutoWidth(1790.4, 1790)); // tolerancja zaokrągleń
        Assert.False(ChatDockingLayout.ShouldRevertAutoWidth(1500, 1790)); // ręczny resize → nie ruszaj
        Assert.False(ChatDockingLayout.ShouldRevertAutoWidth(1790, null)); // brak automatycznego poszerzenia
    }
}

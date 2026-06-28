using Avalonia;
using Avalonia.Controls;

namespace SUSModder.Controls;

/// <summary>
/// Controlka wyświetlająca "szkielet" karty moda podczas ładowania.
/// Gdy IsLoading = true, pokazuje pulsujący placeholder.
/// Gdy IsLoading = false, przepuszcza Content.
/// </summary>
public class SkeletonCard : ContentControl
{
    /// <summary>
    /// Określa czy kontrolka ma być w stanie ładowania (pokazywać skeleton).
    /// </summary>
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<SkeletonCard, bool>(nameof(IsLoading), false);

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }
}

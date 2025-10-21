using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;

namespace SUSModder.Behaviors;

/// <summary>
/// Behavior który trzęsie kontrolką aby zwrócić uwagę użytkownika
/// </summary>
public class ShakeOnLoadBehavior : Behavior<Control>
{
    /// <summary>
    /// Intensywność trzęsienia (przesunięcie w pikselach)
    /// </summary>
    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<ShakeOnLoadBehavior, double>(nameof(Intensity), 5.0);

    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    /// <summary>
    /// Liczba powtórzeń trzęsienia
    /// </summary>
    public static readonly StyledProperty<int> RepeatCountProperty =
        AvaloniaProperty.Register<ShakeOnLoadBehavior, int>(nameof(RepeatCount), 3);

    public int RepeatCount
    {
        get => GetValue(RepeatCountProperty);
        set => SetValue(RepeatCountProperty, value);
    }

    /// <summary>
    /// Opóźnienie przed rozpoczęciem animacji
    /// </summary>
    public static readonly StyledProperty<TimeSpan> DelayProperty =
        AvaloniaProperty.Register<ShakeOnLoadBehavior, TimeSpan>(nameof(Delay), TimeSpan.FromMilliseconds(500));

    public TimeSpan Delay
    {
        get => GetValue(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded += OnLoaded;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded -= OnLoaded;
        }
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AssociatedObject == null)
            return;

        // Upewnij się że kontrolka ma TranslateTransform
        if (AssociatedObject.RenderTransform is not TranslateTransform)
        {
            AssociatedObject.RenderTransform = new TranslateTransform();
        }

        var transform = (TranslateTransform)AssociatedObject.RenderTransform;

        // Poczekaj opóźnienie
        await System.Threading.Tasks.Task.Delay(Delay);

        // Animuj shake effect - bezpośrednia manipulacja TranslateTransform
        for (int i = 0; i < RepeatCount; i++)
        {
            // Przesunięcie w prawo
            await AnimatePropertyAsync(() => transform.X, x => transform.X = x, 0, Intensity, 50);
            // Przesunięcie w lewo
            await AnimatePropertyAsync(() => transform.X, x => transform.X = x, Intensity, -Intensity, 50);
            // Przesunięcie w prawo (mniejsze)
            await AnimatePropertyAsync(() => transform.X, x => transform.X = x, -Intensity, Intensity * 0.5, 50);
            // Powrót do środka
            await AnimatePropertyAsync(() => transform.X, x => transform.X = x, Intensity * 0.5, 0, 50);
        }

        // Upewnij się że jest w pozycji 0
        transform.X = 0;
    }

    private async System.Threading.Tasks.Task AnimatePropertyAsync(
        Func<double> getter,
        Action<double> setter,
        double from,
        double to,
        int durationMs)
    {
        var startTime = DateTime.Now;
        var duration = TimeSpan.FromMilliseconds(durationMs);

        while (DateTime.Now - startTime < duration)
        {
            var elapsed = DateTime.Now - startTime;
            var progress = elapsed.TotalMilliseconds / durationMs;
            
            // Easing function (sine)
            var easedProgress = Math.Sin(progress * Math.PI / 2);
            
            var currentValue = from + (to - from) * easedProgress;
            setter(currentValue);

            await System.Threading.Tasks.Task.Delay(16); // ~60 FPS
        }

        setter(to);
    }
}

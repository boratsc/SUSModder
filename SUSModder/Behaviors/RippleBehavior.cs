using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;
using Avalonia.Threading;

namespace SUSModder.Behaviors;

/// <summary>
/// Behavior dodajacy Material Design ripple effect na kontrolce.
/// Fala rozchodzi sie od punktu klikniecia, animujac scale i opacity.
/// Dziala na Panel (Grid, StackPanel), Border (mod cards)
/// oraz ContentControl (Button, FAB, menu items).
/// Dla nieobslugiwanych kontrolek stosuje uproszczony efekt flash.
/// </summary>
public class RippleBehavior : Behavior<Control>
{
    public static readonly StyledProperty<Color> RippleColorProperty =
        AvaloniaProperty.Register<RippleBehavior, Color>(nameof(RippleColor), Colors.White);

    public Color RippleColor
    {
        get => GetValue(RippleColorProperty);
        set => SetValue(RippleColorProperty, value);
    }

    public static readonly StyledProperty<double> RippleOpacityProperty =
        AvaloniaProperty.Register<RippleBehavior, double>(nameof(RippleOpacity), 0.35);

    public double RippleOpacity
    {
        get => GetValue(RippleOpacityProperty);
        set => SetValue(RippleOpacityProperty, value);
    }

    public static readonly StyledProperty<int> RippleDurationProperty =
        AvaloniaProperty.Register<RippleBehavior, int>(nameof(RippleDuration), 600);

    public int RippleDuration
    {
        get => GetValue(RippleDurationProperty);
        set => SetValue(RippleDurationProperty, value);
    }

    /// <summary>
    /// Uzyj uproszczonego efektu (Background flash) zamiast pelnego ripple.
    /// </summary>
    public static readonly StyledProperty<bool> UseSimpleFlashProperty =
        AvaloniaProperty.Register<RippleBehavior, bool>(nameof(UseSimpleFlash), false);

    public bool UseSimpleFlash
    {
        get => GetValue(UseSimpleFlashProperty);
        set => SetValue(UseSimpleFlashProperty, value);
    }

    private Canvas? _rippleCanvas;
    private bool _isSetup;
    private bool _usesSimpleFlash;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed += OnPointerPressed;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed -= OnPointerPressed;
        }
    }

    private void EnsureRippleSetup()
    {
        if (_isSetup || AssociatedObject == null)
            return;

        _isSetup = true;

        if (UseSimpleFlash)
        {
            _usesSimpleFlash = true;
            return;
        }

        // Panel controls (Grid, StackPanel, Canvas) - add Canvas as first child
        if (AssociatedObject is Panel panel)
        {
            _rippleCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
            panel.Children.Insert(0, _rippleCanvas);
            panel.ClipToBounds = true;
            return;
        }

        // Border (mod cards) - wrap Child in Grid with Canvas overlay
        if (AssociatedObject is Border border)
        {
            var originalChild = border.Child;
            var grid = new Grid { ClipToBounds = true };

            if (originalChild != null)
            {
                border.Child = null;
                grid.Children.Add(originalChild);
            }

            _rippleCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
            grid.Children.Add(_rippleCanvas);
            border.Child = grid;
            return;
        }

        // ContentControl (Button, FAB, menu items) - wrap Content in Grid with Canvas overlay
        if (AssociatedObject is ContentControl contentControl)
        {
            var originalContent = contentControl.Content;
            var grid = new Grid { ClipToBounds = true };

            if (originalContent is Control originalControl)
            {
                contentControl.Content = null; // Detach first
                grid.Children.Add(originalControl);
            }
            else if (originalContent != null)
            {
                // Non-visual content (string, etc.) - wrap in TextBlock
                contentControl.Content = null;
                grid.Children.Add(new TextBlock
                {
                    Text = originalContent.ToString(),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                });
            }

            _rippleCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
            grid.Children.Add(_rippleCanvas);
            contentControl.Content = grid;
            contentControl.ClipToBounds = true;
            return;
        }

        // Fallback: simple flash for other controls
        _usesSimpleFlash = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject == null)
            return;

        EnsureRippleSetup();

        if (_usesSimpleFlash)
        {
            return; // Simple flash is too invasive, skip for now
        }

        var point = e.GetPosition(AssociatedObject);
        var controlSize = AssociatedObject.Bounds.Size;

        double dx = Math.Max(point.X, controlSize.Width - point.X);
        double dy = Math.Max(point.Y, controlSize.Height - point.Y);
        double maxRadius = Math.Sqrt(dx * dx + dy * dy) * 1.1;

        CreateAndAnimateRipple(point, maxRadius);
    }

    private void CreateAndAnimateRipple(Point clickPoint, double maxRadius)
    {
        if (_rippleCanvas == null)
            return;

        var ellipse = new Ellipse
        {
            Fill = new SolidColorBrush(RippleColor, RippleOpacity),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };

        _rippleCanvas.Children.Add(ellipse);

        double halfSize = maxRadius;
        Canvas.SetLeft(ellipse, clickPoint.X - halfSize);
        Canvas.SetTop(ellipse, clickPoint.Y - halfSize);

        _ = AnimateRippleAsync(ellipse, maxRadius * 2,
            TimeSpan.FromMilliseconds(Math.Max(RippleDuration, 100)));
    }

    private async Task AnimateRippleAsync(Ellipse ellipse, double targetSize, TimeSpan duration)
    {
        try
        {
            var growAnimation = new Animation
            {
                Duration = duration,
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters =
                        {
                            new Setter(Avalonia.Layout.Layoutable.WidthProperty, 0.0),
                            new Setter(Avalonia.Layout.Layoutable.HeightProperty, 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.6),
                        Setters =
                        {
                            new Setter(Avalonia.Layout.Layoutable.WidthProperty, targetSize),
                            new Setter(Avalonia.Layout.Layoutable.HeightProperty, targetSize),
                            new Setter(Visual.OpacityProperty, RippleOpacity)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(Avalonia.Layout.Layoutable.WidthProperty, targetSize),
                            new Setter(Avalonia.Layout.Layoutable.HeightProperty, targetSize),
                            new Setter(Visual.OpacityProperty, 0.0)
                        }
                    }
                }
            };

            await growAnimation.RunAsync(ellipse);
        }
        catch (OperationCanceledException)
        {
            // Animacja przerwana (kontrolka usunieta)
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_rippleCanvas != null && ellipse.Parent != null)
                {
                    _rippleCanvas.Children.Remove(ellipse);
                }
            });
        }
    }
}

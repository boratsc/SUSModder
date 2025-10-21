using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;

namespace SUSModder.Behaviors;

/// <summary>
/// Behavior dodający płynną animację shuffle przy zmianie layoutu panelu
/// </summary>
public class LayoutAnimationBehavior : Behavior<Panel>
{
    private readonly Dictionary<Visual, Point> _lastPositions = new();
    private bool _isArranging;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.LayoutUpdated += OnLayoutUpdated;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.LayoutUpdated -= OnLayoutUpdated;
        }
        _lastPositions.Clear();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isArranging || AssociatedObject == null)
            return;

        try
        {
            _isArranging = true;

            var children = AssociatedObject.Children
                .OfType<Visual>()
                .Where(v => v.IsVisible && v.Bounds.Width > 0 && v.Bounds.Height > 0)
                .ToList();

            foreach (var child in children)
            {
                var currentPos = child.Bounds.Position;

                if (_lastPositions.TryGetValue(child, out var lastPos))
                {
                    var offset = lastPos - currentPos;
                    
                    if (Math.Abs(offset.X) > 0.5 || Math.Abs(offset.Y) > 0.5)
                    {
                        AnimateToPosition(child, offset);
                    }
                }

                _lastPositions[child] = currentPos;
            }

            // Usuń nieistniejące elementy
            var childSet = new HashSet<Visual>(children);
            foreach (var key in _lastPositions.Keys.Where(k => !childSet.Contains(k)).ToList())
            {
                _lastPositions.Remove(key);
            }
        }
        finally
        {
            _isArranging = false;
        }
    }

    private void AnimateToPosition(Visual element, Point offset)
    {
        var transform = new TranslateTransform
        {
            X = offset.X,
            Y = offset.Y
        };

        element.RenderTransform = transform;

        // Animuj powrót do pozycji 0,0
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward
        };

        var xKeyFrame = new KeyFrame
        {
            Cue = new Cue(1.0),
            Setters = { new Setter(TranslateTransform.XProperty, 0.0) }
        };

        var yKeyFrame = new KeyFrame
        {
            Cue = new Cue(1.0),
            Setters = { new Setter(TranslateTransform.YProperty, 0.0) }
        };

        animation.Children.Add(xKeyFrame);
        animation.Children.Add(yKeyFrame);

        animation.RunAsync(element).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (element.RenderTransform == transform)
                {
                    element.RenderTransform = null;
                }
            });
        });
    }
}

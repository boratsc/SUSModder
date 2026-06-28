using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    /// <summary>
    /// Widok pojedynczego powiadomienia toast.
    /// Automatycznie odtwarza animację slide-in przy dodaniu do drzewa wizualnego.
    /// </summary>
    public partial class ToastNotificationView : UserControl
    {
        public ToastNotificationView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Po dodaniu do drzewa wizualnego odtwarza animację slide-in.
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Animacja slide-in: przesunięcie z prawej + pojawienie się
            _ = AnimateSlideInAsync();
        }

        private async Task AnimateSlideInAsync()
        {
            // Używamy TYLKO TranslateTransform do slide – nie ruszamy Opacity,
            // żeby nie nadpisać bindingu Opacity="{Binding Opacity}" z modelu.
            // W Avalonii local value (code-behind) ma wyższy priorytet niż binding,
            // więc ustawienie Opacity=0 w kodzie anulowałoby binding i toast by zniknął po animacji.
            var transform = new TranslateTransform { X = 60 };
            RenderTransform = transform;

            // Krótkie opóźnienie aby element zdążył się wyrenderować
            await Task.Delay(10);

            // Animacja slide-in: tylko przesunięcie X, bez zmiany Opacity
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(350),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, 60.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, 0.0)
                        }
                    }
                }
            };

            await animation.RunAsync(this);

            // Po zakończeniu animacji usuń transform (optymalizacja)
            RenderTransform = null;
        }

        /// <summary>
        /// Obsługa kliknięcia przycisku zamknięcia (✕).
        /// </summary>
        private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ToastNotification toast)
            {
                var toastService = App.GetService<ToastService>();
                toastService.Dismiss(toast.Id);
            }
        }

        /// <summary>
        /// Obsługa kliknięcia w obszar toasta (jeśli ustawiony OnClick).
        /// </summary>
        private void OnToastPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Ignoruj jeśli kliknięto przycisk zamknięcia
            if (e.Source is Button)
                return;

            if (DataContext is ToastNotification toast && toast.OnClick != null)
            {
                toast.OnClick.Invoke();
            }
        }
    }
}

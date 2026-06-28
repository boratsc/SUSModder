using System;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Typ powiadomienia toast.
    /// </summary>
    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info
    }

    /// <summary>
    /// Model pojedynczego powiadomienia toast.
    /// Dziedziczy po ReactiveObject, aby wspierać binding w XAML.
    /// </summary>
    public class ToastNotification : ReactiveObject
    {
        private static long _nextId;

        private ToastType _type;
        private string _title = string.Empty;
        private string? _subtitle;
        private int _autoCloseMs;
        private bool _isDismissed;
        private double _opacity = 1.0;

        /// <summary>
        /// Unikalne ID toasta (auto-inkrementowane).
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Typ toastu – określa ikonę i kolor.
        /// </summary>
        public ToastType Type
        {
            get => _type;
            set => this.RaiseAndSetIfChanged(ref _type, value);
        }

        /// <summary>
        /// Główny tekst powiadomienia.
        /// </summary>
        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        /// <summary>
        /// Opcjonalny drugi wiersz tekstu (np. wersja, szczegóły).
        /// </summary>
        public string? Subtitle
        {
            get => _subtitle;
            set => this.RaiseAndSetIfChanged(ref _subtitle, value);
        }

        /// <summary>
        /// Czas w ms po którym toast automatycznie znika.
        /// Domyślnie: 4000 dla Success/Info, 6000 dla Warning, 8000 dla Error.
        /// </summary>
        public int AutoCloseMs
        {
            get => _autoCloseMs;
            set => this.RaiseAndSetIfChanged(ref _autoCloseMs, value);
        }

        /// <summary>
        /// Czy toast został zdismissowany (usunięty z widoku).
        /// </summary>
        public bool IsDismissed
        {
            get => _isDismissed;
            set => this.RaiseAndSetIfChanged(ref _isDismissed, value);
        }

        /// <summary>
        /// Opacity dla animacji fade-out.
        /// </summary>
        public double Opacity
        {
            get => _opacity;
            set => this.RaiseAndSetIfChanged(ref _opacity, value);
        }

        /// <summary>
        /// Opcjonalny callback wykonywany po kliknięciu toasta.
        /// </summary>
        public Action? OnClick { get; set; }

        /// <summary>
        /// Znacznik czasu utworzenia toasta.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Ikona jako string (emoji) na podstawie typu.
        /// </summary>
        public string Icon => Type switch
        {
            ToastType.Success => "✅",
            ToastType.Warning => "⚠️",
            ToastType.Error => "❌",
            ToastType.Info => "ℹ️",
            _ => "ℹ️"
        };

        /// <summary>
        /// Kolor akcentu na podstawie typu.
        /// </summary>
        public string AccentColor => Type switch
        {
            ToastType.Success => "#4CAF50",
            ToastType.Warning => "#FF9800",
            ToastType.Error => "#F44336",
            ToastType.Info => "#2196F3",
            _ => "#2196F3"
        };

        /// <summary>
        /// Kolor tła na podstawie typu (lekko przezroczysty).
        /// </summary>
        public string BackgroundColor => Type switch
        {
            ToastType.Success => "#1A4CAF50",
            ToastType.Warning => "#1AFF9800",
            ToastType.Error => "#1AF44336",
            ToastType.Info => "#1A2196F3",
            _ => "#1A2196F3"
        };

        public ToastNotification()
        {
            Id = System.Threading.Interlocked.Increment(ref _nextId);
            Timestamp = DateTime.Now;

            // Domyślny czas auto-close zależny od typu
            _autoCloseMs = Type switch
            {
                ToastType.Success => 6000,
                ToastType.Warning => 6000,
                ToastType.Error => 8000,
                ToastType.Info => 4000,
                _ => 4000
            };
        }

        /// <summary>
        /// Tworzy toast z podstawowymi parametrami.
        /// </summary>
        public static ToastNotification Create(ToastType type, string title, string? subtitle = null, int? autoCloseMs = null, Action? onClick = null)
        {
            return new ToastNotification
            {
                Type = type,
                Title = title,
                Subtitle = subtitle,
                AutoCloseMs = autoCloseMs ?? (type switch
                {
                    ToastType.Success => 6000,
                    ToastType.Warning => 6000,
                    ToastType.Error => 8000,
                    ToastType.Info => 4000,
                    _ => 4000
                }),
                OnClick = onClick
            };
        }
    }
}

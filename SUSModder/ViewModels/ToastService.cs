using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Singleton serwis zarządzający kolejką powiadomień toast.
    /// Odpowiada za enqueue, auto-close, limit widocznych toastów.
    /// Wszystkie operacje na ObservableCollection są wykonywane na UI thread.
    /// </summary>
    public class ToastService : ReactiveObject
    {
        private const int MaxVisibleToasts = 3;
        private readonly ObservableCollection<ToastNotification> _activeToasts = new();
        private readonly ObservableCollection<ToastNotification> _pendingQueue = new();
        private readonly object _lock = new();
        private bool _isProcessingQueue;

        /// <summary>
        /// Kolekcja aktualnie widocznych toastów (max 3).
        /// </summary>
        public ReadOnlyObservableCollection<ToastNotification> ActiveToasts { get; }

        private bool _hasActiveToasts;
        public bool HasActiveToasts
        {
            get => _hasActiveToasts;
            private set => this.RaiseAndSetIfChanged(ref _hasActiveToasts, value);
        }

        public ToastService()
        {
            ActiveToasts = new ReadOnlyObservableCollection<ToastNotification>(_activeToasts);
            _activeToasts.CollectionChanged += OnActiveToastsChanged;
        }

        private void OnActiveToastsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            HasActiveToasts = _activeToasts.Count > 0;
        }

        /// <summary>
        /// Dodaje nowy toast do kolejki. Jeśli jest miejsce w widocznych – pokazuje od razu,
        /// w przeciwnym razie czeka w kolejce aż zwolni się miejsce.
        /// Wywołanie z UI thread jest gwarantowane przez konsumentów (ViewModel).
        /// </summary>
        public void Enqueue(ToastNotification toast)
        {
            if (toast == null) throw new ArgumentNullException(nameof(toast));

            if (_activeToasts.Count < MaxVisibleToasts)
            {
                ShowToast(toast);
            }
            else
            {
                lock (_lock)
                {
                    _pendingQueue.Add(toast);
                }
            }
        }

        /// <summary>
        /// Ręczne zdismissowanie toasta po ID. Wywoływane z UI thread (przez przycisk ✕).
        /// </summary>
        public void Dismiss(long toastId)
        {
            var toast = _activeToasts.FirstOrDefault(t => t.Id == toastId);
            if (toast != null)
            {
                RemoveToast(toast);
            }
            else
            {
                lock (_lock)
                {
                    var pending = _pendingQueue.FirstOrDefault(t => t.Id == toastId);
                    if (pending != null)
                    {
                        _pendingQueue.Remove(pending);
                    }
                }
            }
        }

        /// <summary>
        /// Usuwa wszystkie aktywne toasty.
        /// </summary>
        public void DismissAll()
        {
            foreach (var toast in _activeToasts.ToList())
            {
                RemoveToast(toast);
            }
            lock (_lock)
            {
                _pendingQueue.Clear();
            }
        }

        /// <summary>
        /// Wyświetla toast i uruchamia auto-close timer.
        /// Musi być wywołane z UI thread (modyfikuje ObservableCollection).
        /// </summary>
        private void ShowToast(ToastNotification toast)
        {
            toast.IsDismissed = false;
            toast.PropertyChanged += OnToastPropertyChanged;
            _activeToasts.Add(toast);

            if (toast.AutoCloseMs > 0)
            {
                StartAutoCloseTimer(toast);
            }
        }

        private void OnToastPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToastNotification.IsDismissed) &&
                sender is ToastNotification toast && toast.IsDismissed)
            {
                RemoveToast(toast);
            }
        }

        /// <summary>
        /// Uruchamia licznik auto-close na UI thread.
        /// Używa DispatcherTimer zamiast Task.Delay, aby uniknąć race conditions
        /// i zapewnić, że callback wykonuje się na UI thread.
        /// </summary>
        private void StartAutoCloseTimer(ToastNotification toast)
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(toast.AutoCloseMs)
            };

            timer.Tick += (sender, args) =>
            {
                timer.Stop();

                // Fade-out animation
                toast.Opacity = 0;

                // Krótkie opóźnienie na animację fade-out, potem usuń
                var fadeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                fadeTimer.Tick += (s, a) =>
                {
                    fadeTimer.Stop();
                    if (!toast.IsDismissed)
                    {
                        RemoveToast(toast);
                    }
                };
                fadeTimer.Start();
            };

            timer.Start();
        }

        /// <summary>
        /// Usuwa toast z listy aktywnych i ewentualnie dodaje następny z kolejki.
        /// Musi być wywołane z UI thread.
        /// </summary>
        private void RemoveToast(ToastNotification toast)
        {
            if (_activeToasts.Remove(toast))
            {
                toast.PropertyChanged -= OnToastPropertyChanged;

                // Jeśli są oczekujące toasty, pokaż następny
                TryShowNextPendingToast();
            }
        }

        private void TryShowNextPendingToast()
        {
            if (_isProcessingQueue) return;

            lock (_lock)
            {
                if (_pendingQueue.Count > 0 && _activeToasts.Count < MaxVisibleToasts)
                {
                    _isProcessingQueue = true;
                    var nextToast = _pendingQueue[0];
                    _pendingQueue.RemoveAt(0);
                    ShowToast(nextToast);
                    _isProcessingQueue = false;
                }
            }
        }

        /// <summary>
        /// Tworzy i enqueue'uje toast Success.
        /// </summary>
        public void ShowSuccess(string title, string? subtitle = null, int? autoCloseMs = null, Action? onClick = null)
        {
            Enqueue(ToastNotification.Create(ToastType.Success, title, subtitle, autoCloseMs, onClick));
        }

        /// <summary>
        /// Tworzy i enqueue'uje toast Warning.
        /// </summary>
        public void ShowWarning(string title, string? subtitle = null, int? autoCloseMs = null, Action? onClick = null)
        {
            Enqueue(ToastNotification.Create(ToastType.Warning, title, subtitle, autoCloseMs, onClick));
        }

        /// <summary>
        /// Tworzy i enqueue'uje toast Error.
        /// </summary>
        public void ShowError(string title, string? subtitle = null, int? autoCloseMs = null, Action? onClick = null)
        {
            Enqueue(ToastNotification.Create(ToastType.Error, title, subtitle, autoCloseMs, onClick));
        }

        /// <summary>
        /// Tworzy i enqueue'uje toast Info.
        /// </summary>
        public void ShowInfo(string title, string? subtitle = null, int? autoCloseMs = null, Action? onClick = null)
        {
            Enqueue(ToastNotification.Create(ToastType.Info, title, subtitle, autoCloseMs, onClick));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;
using Microsoft.Extensions.Configuration;
using System.IO;
using ReactiveUI;
using System.ComponentModel;

namespace SUSModder.Views
{
    public partial class UpdateDialog : Window, INotifyPropertyChanged
    {
        // Właściwości dla bindowania
        private ObservableCollection<ModUpdateInfo> _availableUpdates = new();
        public ObservableCollection<ModUpdateInfo> AvailableUpdates
        {
            get => _availableUpdates;
            set
            {
                _availableUpdates = value;
                OnPropertyChanged();
            }
        }

        private bool _isUpdating = false;
        public bool IsUpdating
        {
            get => _isUpdating;
            set
            {
                _isUpdating = value;
                OnPropertyChanged();
            }
        }

        private int _overallProgress = 0;
        public int OverallProgress
        {
            get => _overallProgress;
            set
            {
                _overallProgress = value;
                OnPropertyChanged();
            }
        }

        private string _overallProgressText = string.Empty;
        public string OverallProgressText
        {
            get => _overallProgressText;
            set
            {
                _overallProgressText = value;
                OnPropertyChanged();
            }
        }

        private string _currentModName = string.Empty;
        public string CurrentModName
        {
            get => _currentModName;
            set
            {
                _currentModName = value;
                OnPropertyChanged();
            }
        }

        private string _currentModStatus = string.Empty;
        public string CurrentModStatus
        {
            get => _currentModStatus;
            set
            {
                _currentModStatus = value;
                OnPropertyChanged();
            }
        }

        private int _currentModProgress = 0;
        public int CurrentModProgress
        {
            get => _currentModProgress;
            set
            {
                _currentModProgress = value;
                OnPropertyChanged();
            }
        }

        private bool _updateCompleted = false;
        public bool UpdateCompleted
        {
            get => _updateCompleted;
            set
            {
                _updateCompleted = value;
                OnPropertyChanged();
            }
        }

        private string _finalMessage = string.Empty;
        public string FinalMessage
        {
            get => _finalMessage;
            set
            {
                _finalMessage = value;
                OnPropertyChanged();
            }
        }

        private bool _showFinalMessage = false;
        public bool ShowFinalMessage
        {
            get => _showFinalMessage;
            set
            {
                _showFinalMessage = value;
                OnPropertyChanged();
            }
        }

        public bool DialogResult { get; private set; } = false;

        public UpdateDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public UpdateDialog(List<ModUpdateInfo> availableUpdates) : this()
        {
            AvailableUpdates = new ObservableCollection<ModUpdateInfo>(availableUpdates);
        }

        public List<ModUpdateInfo> GetSelectedMods()
        {
            return AvailableUpdates.Where(m => m.IsSelected).ToList();
        }

        public void ShowFinalSummary(string message)
        {
            FinalMessage = message;
            ShowFinalMessage = true;
            UpdateCompleted = true;

            // Ukryj progress
            CurrentModName = string.Empty;
            CurrentModStatus = string.Empty;
            OverallProgressText = string.Empty;
        }

        // Event handlers
        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedMods = AvailableUpdates.Where(m => m.IsSelected).ToList();
            if (!selectedMods.Any())
            {
                var noSelectionDialog = new MessageDialog("Informacja", "Nie wybrano żadnych modów do aktualizacji.");
                await noSelectionDialog.ShowDialog(this);
                return;
            }

            // Tylko ustaw flagę że użytkownik potwierdził
            DialogResult = true;

            // Ukryj przyciski i pokaż progress (ale nie zamykaj dialog)
            IsUpdating = true;
            OverallProgress = 0;
            OverallProgressText = "Rozpoczynanie aktualizacji...";

            // NIE WYKONUJ TUTAJ AKTUALIZACJI - to zrobi MainWindowViewModel
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        // Pomocnicze metody dla UserInteractionService
        private async Task<bool> ShowConfirmDialogAsync(string message, string title)
        {
            var dialog = new ConfirmDialog(title, message);
            await dialog.ShowDialog(this);
            return dialog.Result;
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(this);
        }

        private async Task ShowErrorDialogAsync(string message, string title)
        {
            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(this);
        }

        private async Task<string?> ShowPromptDialogAsync(string message, string title)
        {
            await Task.CompletedTask;
            return null;
        }

        private async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory)
        {
            await Task.CompletedTask;
            return null;
        }

        private async Task<bool> UpdateSingleModWithProgressAsync(
            ModUpdateInfo modInfo,
            Action<int, string> progressCallback,
            IDiagnosticsOutput log,
            IConfiguration configuration,
            UserInteractionService userInteraction)
        {
            try
            {
                progressCallback(10, "Pobieranie informacji o modzie...");

                var selectedMods = new List<ModUpdateInfo> { modInfo };

                progressCallback(50, "Aktualizowanie...");

                await ModUpdateChecker.UpdateSelectedModsAsync(
                    selectedMods,
                    configuration,
                    log,
                    userInteraction,
                    new SimpleProgressReporter(progressCallback)
                );

                progressCallback(100, "Zakończono");
                return true;
            }
            catch (Exception ex)
            {
                log.Write($"Błąd aktualizacji {modInfo.ModName}: {ex.Message}");
                progressCallback(0, $"Błąd: {ex.Message}");
                return false;
            }
        }

        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void StartUpdate()
        {
            IsUpdating = true;
            OverallProgress = 0;
            OverallProgressText = "Rozpoczynanie aktualizacji...";
        }

        public void CompleteUpdate()
        {
            UpdateCompleted = true;
        }

        public void UpdateOverallProgress(int currentMod, int totalMods, string modName)
        {
            OverallProgress = (int)((double)currentMod / totalMods * 100);
            OverallProgressText = $"Aktualizowanie {currentMod} z {totalMods}: {modName}";
        }

        public void UpdateCurrentModProgress(int percentage, string message)
        {
            CurrentModProgress = percentage;
            CurrentModStatus = message;
        }

        // Pomocnicze klasy
        private class SimpleProgressReporter : IProgressReporter
        {
            private readonly Action<int, string> _callback;

            public SimpleProgressReporter(Action<int, string> callback)
            {
                _callback = callback;
            }

            public void Report(int percentage, string? message = null)
            {
                _callback(percentage, message ?? string.Empty);
            }
        }

        private class DebugDiagnosticsOutput : IDiagnosticsOutput
        {
            public void Write(string message)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateDialog] {message}");
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System;
using SUSModder.Core.Configuration;
using SUSModder.ViewModels;
using System.Collections.Generic;

namespace SUSModder.Views
{
    public partial class LoadServerConfigDialog : Window, INotifyPropertyChanged
    {
        private string _manualHash = "";
        private SavedConfigItem? _selectedConfig;
        private bool _useManualHash = true;

        public string ManualHash
        {
            get => _manualHash;
            set
            {
                _manualHash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ManualHash)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoad)));
            }
        }

        public SavedConfigItem? SelectedConfig
        {
            get => _selectedConfig;
            set
            {
                _selectedConfig = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConfig)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoad)));
            }
        }

        public bool UseManualHash
        {
            get => _useManualHash;
            set
            {
                _useManualHash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseManualHash)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoad)));
            }
        }

        public ObservableCollection<SavedConfigItem> SavedConfigs { get; } = new();

        public bool CanLoad => UseManualHash ? !string.IsNullOrWhiteSpace(ManualHash) : SelectedConfig != null;

        public string? ResultHash { get; private set; }
        public bool DialogResult { get; private set; }

        public new event PropertyChangedEventHandler? PropertyChanged;


        public LoadServerConfigDialog()
        {
            InitializeComponent();
            DataContext = this;
            LoadSavedConfigs();
        }

        private void LoadSavedConfigs()
        {
            try
            {
                // Użyj SQLite przez ConfigManager (z fallback do JSON)
                var configs = ConfigManager.GetTouConfigs();

                foreach (var config in configs)
                {
                    SavedConfigs.Add(new SavedConfigItem
                    {
                        Hash = config.Hash,
                        Date = config.CreatedAt
                    });
                }

                // Jeśli mamy zapisane konfiguracje, domyślnie wybierz tryb listy
                if (SavedConfigs.Any())
                {
                    UseManualHash = false;
                    SelectedConfig = SavedConfigs.First();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadServerConfigDialog] Błąd wczytywania zapisanych konfiguracji: {ex.Message}");
            }
        }

        private void OnLoadClick(object? sender, RoutedEventArgs e)
        {
            if (UseManualHash && !string.IsNullOrWhiteSpace(ManualHash))
            {
                ResultHash = ManualHash.Trim();
                DialogResult = true;
                Close();
            }
            else if (!UseManualHash && SelectedConfig != null)
            {
                ResultHash = SelectedConfig.Hash;
                DialogResult = true;
                Close();
            }
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnManualHashRadioChecked(object? sender, RoutedEventArgs e)
        {
            UseManualHash = true;
        }

        private void OnSavedConfigRadioChecked(object? sender, RoutedEventArgs e)
        {
            UseManualHash = false;
        }
    }
}

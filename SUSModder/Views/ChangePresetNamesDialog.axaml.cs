using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System;
using SUSModder.ViewModels;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SUSModder.Views
{
    public partial class ChangePresetNamesDialog : Window, INotifyPropertyChanged
    {
        public ObservableCollection<PresetFileItem> PresetFiles { get; } = new();

        public bool DialogResult { get; private set; }


        public ChangePresetNamesDialog()
        {
            InitializeComponent();
            DataContext = this;
            LoadPresetFiles();
        }

        private void LoadPresetFiles()
        {
            try
            {
                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");

                if (!Directory.Exists(targetDir))
                {
                    System.Diagnostics.Debug.WriteLine($"[ChangePresetNames] Katalog nie istnieje: {targetDir}");
                    return;
                }

                var txtFiles = Directory.GetFiles(targetDir, "*.txt");
                System.Diagnostics.Debug.WriteLine($"[ChangePresetNames] Znaleziono {txtFiles.Length} plików .txt");

                foreach (var file in txtFiles.OrderBy(f => Path.GetFileNameWithoutExtension(f)))
                {
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);

                    PresetFiles.Add(new PresetFileItem
                    {
                        OriginalName = fileNameWithoutExtension,
                        NewName = fileNameWithoutExtension,
                        FullPath = file
                    });
                }

                if (PresetFiles.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[ChangePresetNames] Brak plików .txt do wyświetlenia");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangePresetNames] Błąd wczytywania plików: {ex.Message}");
            }
        }

        private async void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var changedFiles = PresetFiles.Where(f => f.HasChanges && !string.IsNullOrWhiteSpace(f.NewName)).ToList();

                if (!changedFiles.Any())
                {
                    // Brak zmian - po prostu zamknij
                    DialogResult = true;
                    Close();
                    return;
                }

                int successCount = 0;
                int errorCount = 0;
                var errors = new List<string>();

                foreach (var fileItem in changedFiles)
                {
                    try
                    {
                        string newFileName = fileItem.NewName.Trim() + ".txt";
                        string targetDir = Path.GetDirectoryName(fileItem.FullPath) ?? "";
                        string newFilePath = Path.Combine(targetDir, newFileName);

                        // Sprawdź czy plik docelowy już istnieje
                        if (File.Exists(newFilePath) && !string.Equals(fileItem.FullPath, newFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Plik '{newFileName}' już istnieje");
                            errorCount++;
                            continue;
                        }

                        // Sprawdź czy nazwa zawiera niedozwolone znaki
                        if (newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        {
                            errors.Add($"Nazwa '{fileItem.NewName}' zawiera niedozwolone znaki");
                            errorCount++;
                            continue;
                        }

                        // Zmień nazwę pliku
                        File.Move(fileItem.FullPath, newFilePath);
                        successCount++;

                        System.Diagnostics.Debug.WriteLine($"[ChangePresetNames] Zmieniono: {fileItem.OriginalName} → {fileItem.NewName}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Błąd przy '{fileItem.OriginalName}': {ex.Message}");
                        errorCount++;
                        System.Diagnostics.Debug.WriteLine($"[ChangePresetNames] Błąd zmiany nazwy {fileItem.OriginalName}: {ex.Message}");
                    }
                }

                // Pokaż podsumowanie
                await ShowResultSummary(successCount, errorCount, errors);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangePresetNames] Błąd podczas zapisywania: {ex.Message}");
                await ShowErrorDialog($"Wystąpił błąd podczas zapisywania zmian: {ex.Message}");
            }
        }

        private async Task ShowResultSummary(int successCount, int errorCount, List<string> errors)
        {
            string message;
            string title;

            if (errorCount == 0)
            {
                message = $"Pomyślnie zmieniono nazwy {successCount} plików.";
                title = "Sukces";
            }
            else if (successCount == 0)
            {
                message = $"Nie udało się zmienić nazwy żadnego pliku.\n\nBłędy:\n{string.Join("\n", errors)}";
                title = "Błąd";
            }
            else
            {
                message = $"Zmieniono nazwy {successCount} plików.\n" +
                         $"Błędy przy {errorCount} plikach:\n\n{string.Join("\n", errors)}";
                title = "Częściowy sukces";
            }

            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(this);
        }

        private async Task ShowErrorDialog(string message)
        {
            var dialog = new MessageDialog("Błąd", message);
            await dialog.ShowDialog(this);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnResetClick(object? sender, RoutedEventArgs e)
        {
            // Resetuj wszystkie nazwy do oryginalnych
            foreach (var file in PresetFiles)
            {
                file.NewName = file.OriginalName;
            }
        }
    }
}

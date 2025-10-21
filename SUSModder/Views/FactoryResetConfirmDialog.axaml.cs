using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;

namespace SUSModder.Views
{
    public partial class FactoryResetConfirmDialog : Window
    {
        public bool Result { get; private set; }

        public FactoryResetConfirmDialog()
        {
            InitializeComponent();
        }

        public FactoryResetConfirmDialog(string modsInstallPath, string defaultModsPath)
        {
            InitializeComponent();
            Title = "Potwierdzenie resetu fabrycznego";

            // Asynchronicznie sprawdź rozmiar katalogów do usunięcia
            _ = LoadDirectoriesSizeAsync(modsInstallPath, defaultModsPath);
        }

        private async Task LoadDirectoriesSizeAsync(string modsInstallPath, string defaultModsPath)
        {
            try
            {
                await Task.Run(() =>
                {
                    long totalSize = 0;
                    int directoriesCount = 0;

                    // Oblicz rozmiar ModsInstallPath
                    if (!string.IsNullOrWhiteSpace(modsInstallPath) && Directory.Exists(modsInstallPath))
                    {
                        totalSize += GetDirectorySize(modsInstallPath);
                        directoriesCount++;
                    }

                    // Oblicz rozmiar DefaultModsPath (po rozwinięciu zmiennych środowiskowych)
                    if (!string.IsNullOrWhiteSpace(defaultModsPath))
                    {
                        string expandedDefault = Environment.ExpandEnvironmentVariables(defaultModsPath);
                        if (Directory.Exists(expandedDefault) && expandedDefault != modsInstallPath)
                        {
                            totalSize += GetDirectorySize(expandedDefault);
                            directoriesCount++;
                        }
                    }

                    if (totalSize > 0)
                    {
                        var sizeText = FormatBytes(totalSize);
                        string dirText = directoriesCount == 1 ? "katalog" : directoriesCount == 2 ? "katalogi" : "katalogów";

                        Dispatcher.UIThread.Post(() =>
                        {
                            SizeInfoText.Text = $"Rozmiar do usunięcia: {sizeText} ({directoriesCount} {dirText})";
                            SizeInfoBorder.IsVisible = true;
                        });
                    }
                });
            }
            catch
            {
                // Jeśli nie można obliczyć rozmiaru, po prostu nie pokazuj info
            }
        }

        private long GetDirectorySize(string path)
        {
            var dirInfo = new DirectoryInfo(path);
            long size = 0;

            try
            {
                // Suma plików w tym katalogu
                foreach (var file in dirInfo.GetFiles())
                {
                    size += file.Length;
                }

                // Rekurencyjnie dla podkatalogów
                foreach (var dir in dirInfo.GetDirectories())
                {
                    try
                    {
                        size += GetDirectorySize(dir.FullName);
                    }
                    catch
                    {
                        // Pomiń katalogi do których nie ma dostępu
                    }
                }
            }
            catch
            {
                // Pomiń błędy dostępu
            }

            return size;
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private void ResetButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}

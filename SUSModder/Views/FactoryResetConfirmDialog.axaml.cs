using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class FactoryResetConfirmDialog : Window
    {
        public bool Result { get; private set; }
        private readonly ILocalizationService _localizationService;

        public FactoryResetConfirmDialog()
        {
            _localizationService = App.GetService<ILocalizationService>();
            InitializeComponent();
        }

        public FactoryResetConfirmDialog(IReadOnlyList<string> directoriesToDelete) : this()
        {
            Title = _localizationService.Get("Tools.FactoryReset.WindowTitle");

            // Asynchronicznie sprawdź rozmiar katalogów do usunięcia
            _ = LoadDirectoriesSizeAsync(directoriesToDelete);
        }

        private async Task LoadDirectoriesSizeAsync(IReadOnlyList<string> directoriesToDelete)
        {
            try
            {
                await Task.Run(() =>
                {
                    long totalSize = 0;
                    int directoriesCount = 0;

                    foreach (var path in directoriesToDelete
                                 .Where(p => !string.IsNullOrWhiteSpace(p))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!Directory.Exists(path))
                            continue;

                        totalSize += GetDirectorySize(path);
                        directoriesCount++;
                    }

                    if (totalSize > 0)
                    {
                        var sizeText = FormatBytes(totalSize);
                        string dirText = GetDirectoryText(directoriesCount);

                        Dispatcher.UIThread.Post(() =>
                        {
                            SizeInfoText.Text = _localizationService.GetFormatted("Tools.FactoryReset.SizeInfo", sizeText, directoriesCount, dirText);
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

        private string GetDirectoryText(int count)
        {
            var currentLanguage = _localizationService.CurrentCulture;
            
            if (currentLanguage == "pl")
            {
                // Polska forma liczby mnogiej dla "katalog"
                if (count == 1)
                    return _localizationService.Get("Tools.FactoryReset.DirectorySingular");
                else if (count >= 2 && count <= 4)
                    return _localizationService.Get("Tools.FactoryReset.DirectoryPlural2");
                else
                    return _localizationService.Get("Tools.FactoryReset.DirectoryPlural5");
            }
            else
            {
                // Angielska forma liczby mnogiej dla "directory"
                if (count == 1)
                    return _localizationService.Get("Tools.FactoryReset.DirectorySingular");
                else
                    return _localizationService.Get("Tools.FactoryReset.DirectoryPlural");
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

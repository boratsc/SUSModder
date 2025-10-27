using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class UninstallConfirmDialog : Window
    {
        public bool Result { get; private set; }
        private readonly ILocalizationService _localizationService;

        public UninstallConfirmDialog()
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
        }

        public UninstallConfirmDialog(string modName, string installPath)
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
                
            Title = _localizationService.Get("Dialogs.Uninstall.Title");
            TitleText.Text = _localizationService.GetFormatted("Dialogs.Uninstall.MessageWithName", modName);
            MessageText.Text = _localizationService.Get("Dialogs.Confirm.DeleteMessage");

            // Asynchronicznie sprawdź rozmiar katalogu
            _ = LoadDirectorySizeAsync(installPath);
        }

        private async Task LoadDirectorySizeAsync(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            try
            {
                await Task.Run(() =>
                {
                    var size = GetDirectorySize(path);
                    var sizeMB = size / (1024.0 * 1024.0);

                    Dispatcher.UIThread.Post(() =>
                    {
                        SizeInfoText.Text = _localizationService.GetFormatted("Dialogs.Uninstall.SizeInfo", $"{sizeMB:0.##}");
                        SizeInfoBorder.IsVisible = true;
                    });
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

        private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
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

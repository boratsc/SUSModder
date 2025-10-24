using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class UpdateModConfirmDialog : Window
    {
        public bool Result { get; private set; }
        private readonly ILocalizationService _localizationService;

        public UpdateModConfirmDialog()
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
        }

        public UpdateModConfirmDialog(ModUpdateInfo updateInfo)
        {
            InitializeComponent();
            _localizationService = App.GetService<ILocalizationService>();
            
            Title = _localizationService.Get("Dialogs.UpdateMod.Title");
            TitleText.Text = _localizationService.GetFormatted("Dialogs.UpdateMod.TitleWithName", updateInfo.ModName);
            MessageText.Text = _localizationService.Get("Dialogs.UpdateMod.Message");

            // Ustaw informacje o wersjach
            var unknownVersion = _localizationService.Get("Dialogs.UpdateMod.UnknownVersion");
            CurrentModVersionText.Text = updateInfo.CurrentVersion ?? unknownVersion;
            NewModVersionText.Text = updateInfo.NewVersion ?? unknownVersion;
            AmongVersionText.Text = updateInfo.RemoteMod?.AmongVersion ?? unknownVersion;

            // Asynchronicznie sprawdź rozmiar pliku do pobrania
            var downloadLink = updateInfo.RemoteMod?.GitHubRepoOrLink;
            if (!string.IsNullOrEmpty(downloadLink))
            {
                _ = LoadDownloadSizeAsync(downloadLink);
            }
        }

        private async Task LoadDownloadSizeAsync(string downloadUrl)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                // Wykonaj HEAD request aby uzyskać rozmiar bez pobierania
                var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                {
                    var sizeBytes = response.Content.Headers.ContentLength.Value;
                    var sizeText = FormatBytes(sizeBytes);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DownloadSizeText.Text = _localizationService.GetFormatted("Dialogs.UpdateMod.DownloadSize", sizeText);
                        DownloadSizeInfoBorder.IsVisible = true;
                    });
                }
            }
            catch
            {
                // Jeśli nie można uzyskać rozmiaru, po prostu nie pokazuj info
            }
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

using ReactiveUI;
using SUSModder.Core.Models;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System;

namespace SUSModder.ViewModels
{
    public class DiscordServerViewModel : ViewModelBase
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private Bitmap? _iconBitmap;
        private bool _isIconLoading = false;
        private readonly DiscordServer _model;

        public DiscordServerViewModel(DiscordServer model)
        {
            _model = model;
        }

        // Właściwości z modelu
        public string Name => _model.Name;
        public string InviteLink => _model.InviteLink;
        public string Description => _model.Description;
        public string? IconPath => _model.IconPath;

        // Właściwości UI
        public Bitmap? IconBitmap
        {
            get => _iconBitmap;
            private set => this.RaiseAndSetIfChanged(ref _iconBitmap, value);
        }

        public bool HasIcon => IconBitmap != null;
        public bool IsIconLoading
        {
            get => _isIconLoading;
            private set => this.RaiseAndSetIfChanged(ref _isIconLoading, value);
        }

        public async Task LoadIconAsync()
        {
            if (string.IsNullOrEmpty(IconPath) || _iconBitmap != null || _isIconLoading)
                return;

            IsIconLoading = true;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Loading icon from: {IconPath}");

                using var response = await _httpClient.GetAsync(IconPath);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    IconBitmap = new Bitmap(memoryStream);
                    this.RaisePropertyChanged(nameof(HasIcon));
                    System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Successfully loaded icon for: {Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Failed to load icon for {Name}: HTTP {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Error loading icon for {Name}: {ex.Message}");
            }
            finally
            {
                IsIconLoading = false;
            }
        }
    }
}

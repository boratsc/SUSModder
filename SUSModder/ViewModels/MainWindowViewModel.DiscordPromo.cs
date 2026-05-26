using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Services;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class odpowiedzialny za rotacyjne promowanie polecanych serwerów Discord przy FAB.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private const int DiscordPromoRotationSeconds = 10;

        private readonly DispatcherTimer _discordPromoRotationTimer = new();
        private List<DiscordServerViewModel> _discordPromoServers = new();
        private int _discordPromoIndex = -1;
        private DiscordServerViewModel? _currentPromotedDiscord;
        private INotifyPropertyChanged? _currentPromotedDiscordNotifier;
        private bool _isFloatingPromoSpaceAvailable = true;

        public DiscordServerViewModel? CurrentPromotedDiscord
        {
            get => _currentPromotedDiscord;
            private set
            {
                if (_currentPromotedDiscordNotifier != null)
                {
                    _currentPromotedDiscordNotifier.PropertyChanged -= OnCurrentPromotedDiscordPropertyChanged;
                }

                this.RaiseAndSetIfChanged(ref _currentPromotedDiscord, value);

                _currentPromotedDiscordNotifier = value;
                if (_currentPromotedDiscordNotifier != null)
                {
                    _currentPromotedDiscordNotifier.PropertyChanged += OnCurrentPromotedDiscordPropertyChanged;
                }

                this.RaisePropertyChanged(nameof(HasPromotedDiscord));
                RaisePromotedDiscordDerivedProperties();
            }
        }

        public bool HasPromotedDiscord => CurrentPromotedDiscord != null;
        public bool CurrentPromotedDiscordHasIcon => CurrentPromotedDiscord?.HasIcon == true;
        public Bitmap? CurrentPromotedDiscordIconBitmap => CurrentPromotedDiscord?.IconBitmap;
        public string CurrentPromotedDiscordName => CurrentPromotedDiscord?.Name ?? string.Empty;
        public string CurrentPromotedDiscordDescription => CurrentPromotedDiscord?.Description ?? string.Empty;
        public string CurrentPromotedDiscordInviteLink => CurrentPromotedDiscord?.InviteLink ?? string.Empty;

        public bool IsFloatingPromoSpaceAvailable
        {
            get => _isFloatingPromoSpaceAvailable;
            set => this.RaiseAndSetIfChanged(ref _isFloatingPromoSpaceAvailable, value);
        }

        public ReactiveCommand<string, Unit> OpenPromotedDiscordInviteCommand { get; private set; } =
            ReactiveCommand.Create<string>(_ => { });

        private void InitializeDiscordPromo()
        {
            OpenPromotedDiscordInviteCommand = ReactiveCommand.Create<string>(OpenPromotedDiscordInvite);

            _discordPromoRotationTimer.Interval = TimeSpan.FromSeconds(DiscordPromoRotationSeconds);
            _discordPromoRotationTimer.Tick += (_, _) => RotateDiscordPromo();

            _ = LoadDiscordPromoServersAsync();
        }

        private async Task LoadDiscordPromoServersAsync()
        {
            try
            {
                var servers = await GetDiscordPromoServersAsync();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyDiscordPromoServers(servers);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiscordPromo] Failed to load promoted servers: {ex.Message}");
            }
        }

        private async Task<List<DiscordServerViewModel>> GetDiscordPromoServersAsync()
        {
            var preloaded = await WaitForPreloadedServersAsync();
            if (preloaded.Count > 0)
            {
                return preloaded;
            }

            var fetched = await FetchDiscordPromoServersAsync();
            if (fetched.Count > 0)
            {
                return fetched;
            }

            return new List<DiscordServerViewModel>();
        }

        private static async Task<List<DiscordServerViewModel>> WaitForPreloadedServersAsync()
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var preloaded = DiscordIconPreloader.GetPreloadedServers();
                if (preloaded != null && preloaded.Count > 0)
                {
                    return preloaded;
                }

                if (DiscordIconPreloader.IsPreloadCompleted)
                {
                    break;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            return DiscordIconPreloader.GetPreloadedServers() ?? new List<DiscordServerViewModel>();
        }

        private static async Task<List<DiscordServerViewModel>> FetchDiscordPromoServersAsync()
        {
            try
            {
                var basePath = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var diagnosticsOutput = new UIDiagnosticsOutput(message =>
                {
                    Debug.WriteLine($"[DiscordPromo] {message}");
                });

                var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
                var serverDataList = await discordService.GetDiscordFavoritesAsync().ConfigureAwait(false);
                var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);
                var viewModels = discordServers.Select(server => new DiscordServerViewModel(server)).ToList();

                foreach (var serverVm in viewModels)
                {
                    _ = Task.Run(async () => await serverVm.LoadIconAsync().ConfigureAwait(false));
                }

                return viewModels;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiscordPromo] Fallback fetch failed: {ex.Message}");
                return new List<DiscordServerViewModel>();
            }
        }

        private void ApplyDiscordPromoServers(List<DiscordServerViewModel> servers)
        {
            _discordPromoServers = servers
                .Where(server => !string.IsNullOrWhiteSpace(server.InviteLink))
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            _discordPromoIndex = -1;

            if (_discordPromoServers.Count == 0)
            {
                CurrentPromotedDiscord = null;
                _discordPromoRotationTimer.Stop();
                return;
            }

            RotateDiscordPromo();

            if (_discordPromoServers.Count > 1)
            {
                _discordPromoRotationTimer.Start();
            }
            else
            {
                _discordPromoRotationTimer.Stop();
            }
        }

        private void RotateDiscordPromo()
        {
            if (_disposed || _discordPromoServers.Count == 0)
            {
                CurrentPromotedDiscord = null;
                return;
            }

            _discordPromoIndex = (_discordPromoIndex + 1) % _discordPromoServers.Count;
            CurrentPromotedDiscord = _discordPromoServers[_discordPromoIndex];
        }

        private void OpenPromotedDiscordInvite(string inviteLink)
        {
            if (string.IsNullOrWhiteSpace(inviteLink))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = inviteLink,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiscordPromo] Failed to open invite link: {ex.Message}");
            }
        }

        private void OnCurrentPromotedDiscordPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DiscordServerViewModel.IconBitmap) ||
                e.PropertyName == nameof(DiscordServerViewModel.HasIcon))
            {
                RaisePromotedDiscordDerivedProperties();
            }
        }

        private void RaisePromotedDiscordDerivedProperties()
        {
            this.RaisePropertyChanged(nameof(CurrentPromotedDiscordHasIcon));
            this.RaisePropertyChanged(nameof(CurrentPromotedDiscordIconBitmap));
            this.RaisePropertyChanged(nameof(CurrentPromotedDiscordName));
            this.RaisePropertyChanged(nameof(CurrentPromotedDiscordDescription));
            this.RaisePropertyChanged(nameof(CurrentPromotedDiscordInviteLink));
        }

        #region IDisposable support

        /// <summary>
        /// Zatrzymuje timer rotacji DiscordPromo.
        /// </summary>
        private void DisposeDiscordPromoTimer()
        {
            _discordPromoRotationTimer.Stop();
        }

        /// <summary>
        /// Zwalnia bitmapę aktualnie promowanego Discorda oraz wszystkie bitmapy
        /// z listy preloaded serwerów, aby uniknąć wycieków pamięci.
        /// </summary>
        private void DisposeDiscordBitmaps()
        {
            // Dispose bitmapy aktualnie wyświetlanego serwera
            if (_currentPromotedDiscord != null)
            {
                _currentPromotedDiscord.DisposeIconBitmap();
            }

            // Dispose bitmap wszystkich serwerów w liście rotacyjnej
            foreach (var server in _discordPromoServers)
            {
                server.DisposeIconBitmap();
            }
        }

        #endregion
    }
}

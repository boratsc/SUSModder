using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SUSModder.Core.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using System.Linq;
using SUSModder.Services;
using System.Net;
using System.Net.Http;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    public class RecommendedDiscordsViewModel : ViewModelBase
    {
        private static readonly HttpClient _inviteValidationClient = CreateInviteValidationClient();
        private static readonly ConcurrentDictionary<string, (bool IsValid, DateTimeOffset CheckedAt)> _inviteValidationCache = new();
        private static readonly TimeSpan _inviteCacheTtl = TimeSpan.FromMinutes(30);

        private bool _isLoading = true;
        private string _statusKey = "RecommendedDiscords.LoadingServers";
        private object[] _statusArgs = Array.Empty<object>();

        public ObservableCollection<DiscordServerViewModel> DiscordServers { get; } = new();

        public ReactiveCommand<string, Unit> OpenDiscordLinkCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenOfficialDiscordHubCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<DiscordServerViewModel, Unit> PackActionCommand { get; }

        /// <summary>
        /// F3b: VM głównego okna (wstrzykiwany przez kod-behind panelu po podłączeniu do drzewa).
        /// Akcje zestawu reuse istniejące flow instalacji/aktualizacji — bez nowej logiki Core.
        /// </summary>
        public MainWindowViewModel? Host { get; set; }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        /// <summary>Status ładowania jako klucz + argumenty — przetłumaczony na żywo przy zmianie języka.</summary>
        public string StatusMessage => LocFmt(_statusKey, _statusArgs);

        private void SetStatus(string key, params object[] args)
        {
            _statusKey = key;
            _statusArgs = args;
            this.RaisePropertyChanged(nameof(StatusMessage));
        }

        public RecommendedDiscordsViewModel()
        {
            OpenDiscordLinkCommand = ReactiveCommand.Create<string>(OpenDiscordLink);
            OpenOfficialDiscordHubCommand = ReactiveCommand.Create(OfficialDiscordHub.Open);
            CloseCommand = ReactiveCommand.Create(() => { });
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadDiscordServersAsync);
            PackActionCommand = ReactiveCommand.CreateFromTask<DiscordServerViewModel>(RunPackActionAsync);

            // Język w locie: status + etykiety VT w wierszach (nazwy/opisy serwerów to dane z backendu).
            App.GetService<ILocalizationService>().PropertyChanged += OnLanguageChanged;

            // Załaduj dane przy inicjalizacji
            _ = LoadDiscordServersAsync();
        }

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ILocalizationService.CurrentCulture) && !string.IsNullOrEmpty(e.PropertyName))
                return;
            this.RaisePropertyChanged(nameof(StatusMessage));
            foreach (var server in DiscordServers)
                server.RefreshLocalization();
        }

        private async Task LoadDiscordServersAsync()
        {
            try
            {
                IsLoading = true;
                SetStatus("RecommendedDiscords.LoadingServers");

                var configuration = App.GetService<IConfiguration>();

                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Discord Service] {message}");
                });

                var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
                var serverCounts = await discordService.GetDiscordServerCountsAsync();

                // Poczekaj chwilę na preload ikon, aby nie dublować wywołań API
                var preloadedServers = await WaitForPreloadedServersAsync();

                if (preloadedServers != null && preloadedServers.Any())
                {
                    var filteredPreloadedServers = await FilterValidInvitesAsync(preloadedServers);
                    var sortedPreloadedServers = ApplyCountsAndSort(filteredPreloadedServers, serverCounts);

                    // Użyj preloadowanych ViewModels z ikonami
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DiscordServers.Clear();
                        foreach (var serverVM in sortedPreloadedServers)
                        {
                            DiscordServers.Add(serverVM);
                        }
                        SetStatus("RecommendedDiscords.ServersLoaded", sortedPreloadedServers.Count);
                    });
                }
                else
                {
                    // Fallback - stwórz ViewModels bez ikon
                    SetStatus("RecommendedDiscords.FetchingServers");
                    var serverDataList = await discordService.GetDiscordFavoritesAsync();
                    var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);
                    var filteredDiscordServers = await FilterValidInvitesAsync(discordServers);
                    var discordServerViewModels = filteredDiscordServers
                        .Select(server => new DiscordServerViewModel(server))
                        .ToList();
                    var sortedDiscordServers = ApplyCountsAndSort(discordServerViewModels, serverCounts);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DiscordServers.Clear();

                        if (sortedDiscordServers.Any())
                        {
                            foreach (var serverVM in sortedDiscordServers)
                            {
                                DiscordServers.Add(serverVM);
                                // Załaduj ikony w tle
                                _ = Task.Run(async () => await serverVM.LoadIconAsync());
                            }
                            SetStatus("RecommendedDiscords.ServersLoaded", sortedDiscordServers.Count);
                        }
                        else
                        {
                            if (!discordServers.Any())
                            {
                                LoadPlaceholderData();
                                SetStatus("RecommendedDiscords.UsingSampleData");
                            }
                            else
                            {
                                SetStatus("RecommendedDiscords.ServersLoaded", 0);
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Discord servers: {ex.Message}");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadPlaceholderData();
                    SetStatus("RecommendedDiscords.ConnectionError");
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = false;
                    RefreshPackInstanceDetection();
                });
            }
        }

        /// <summary>
        /// F3b: instalacja/aktualizacja zestawu z wiersza — deleguje do istniejących flow
        /// MainWindowViewModel (podgląd + instalacja / aktualizacja instancji z potwierdzeniem).
        /// </summary>
        private async Task RunPackActionAsync(DiscordServerViewModel? server)
        {
            if (server == null || server.IsPackBusy || !server.HasPack || !server.Installable)
                return;

            if (Host is not { } host)
            {
                // I3: neutralny toast zamiast cichego return (ten sam klucz co catch; serwis z DI jak ToastHost).
                App.GetService<ToastService>().ShowInfo(Loc("RecommendedDiscords.PackActionFailed"));
                return;
            }

            server.IsPackBusy = true;
            try
            {
                if (server.HasPackInstance)
                    await host.UpdateServerPackAsync(server.PackCode!, server.PackModName, server.Name);
                else
                    await host.InstallServerPackAsync(server.PackCode!);
            }
            catch (Exception ex)
            {
                // I4: neutralny komunikat zamiast cichego połknięcia (szczegóły techniczne w logu).
                System.Diagnostics.Debug.WriteLine($"[DiscordPacks] Błąd akcji zestawu: {ex.Message}");
                host.ToastService.ShowInfo(Loc("RecommendedDiscords.PackActionFailed"));
            }
            finally
            {
                server.IsPackBusy = false;
                RefreshPackInstanceDetection();
            }
        }

        /// <summary>Odświeża wykrywanie lokalnych instancji zestawów (przy otwarciu panelu i po zakończeniu flow).</summary>
        public void RefreshPackInstanceDetection()
        {
            try
            {
                var repo = App.GetService<IModInstanceRepository>();
                DiscordServerViewModel.ApplyPackInstances(DiscordServers, repo.GetPackInstances(), Host?.PackInstances);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordPacks] Nie udało się odświeżyć instancji zestawów: {ex.Message}");
            }
        }

        /// <summary>
        /// F3g: otwarcie zakładki — cichy check paczek (toast tylko dla NOWYCH aktualizacji),
        /// potem odświeżenie badge/listy zmian w wierszach. Check ma własne catch; ten dodatkowy
        /// chroni przed wyjątkiem spoza checkera, żeby odświeżenie wierszy zawsze się wykonało.
        /// </summary>
        public async Task RefreshPackStateOnTabOpenedAsync()
        {
            try
            {
                if (Host != null)
                    await Host.CheckModPackUpdatesAsync(onlyNew: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordPacks] Cichy check aktualizacji paczek nieudany: {ex.Message}");
            }
            RefreshPackInstanceDetection();
        }

        private void LoadPlaceholderData()
        {
            DiscordServers.Clear();

            var placeholderServers = new[]
            {
                new DiscordServer
                {
                    Id = 1001,
                    Name = "Among Us Polska",
                    InviteLink = "https://discord.gg/example1",
                    Description = "Największa polska społeczność Among Us. Znajdziesz tu graczy, turnieje i najnowsze informacje o grze.",
                    IconPath = null,
                    MemberCount = 420
                },
                new DiscordServer
                {
                    Id = 1002,
                    Name = "Town of Us Community",
                    InviteLink = "https://discord.gg/example2",
                    Description = "Oficjalny serwer moda Town of Us. Wsparcie techniczne, aktualizacje i społeczność.",
                    IconPath = null,
                    MemberCount = 320
                },
                new DiscordServer
                {
                    Id = 1003,
                    Name = "The Other Roles",
                    InviteLink = "https://discord.gg/example3",
                    Description = "Społeczność moda The Other Roles. Dyskusje o nowych rolach i strategiach gry.",
                    IconPath = null,
                    MemberCount = 280
                },
                new DiscordServer
                {
                    Id = 1004,
                    Name = "SUSModder Support",
                    InviteLink = "https://discord.gg/example4",
                    Description = "Oficjalny serwer wsparcia dla SUSModder. Pomoc techniczna i zgłaszanie błędów.",
                    IconPath = null,
                    MemberCount = 180
                }
            };

            foreach (var server in placeholderServers.OrderByDescending(server => server.MemberCount))
            {
                DiscordServers.Add(new DiscordServerViewModel(server));
            }
        }

        private static async Task<List<DiscordServerViewModel>> WaitForPreloadedServersAsync()
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var preloaded = DiscordIconPreloader.GetPreloadedServers();
                if (preloaded != null && preloaded.Any())
                {
                    return preloaded;
                }

                if (!DiscordIconPreloader.IsPreloading && DiscordIconPreloader.IsPreloadCompleted)
                {
                    break;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            return DiscordIconPreloader.GetPreloadedServers() ?? new List<DiscordServerViewModel>();
        }

        private static List<DiscordServerViewModel> ApplyCountsAndSort(
            IEnumerable<DiscordServerViewModel> servers,
            IReadOnlyDictionary<string, int> countsByInvite)
        {
            var list = servers.ToList();
            var countsByInviteCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var (inviteKey, count) in countsByInvite)
            {
                if (TryExtractInviteCode(inviteKey, out var inviteCode))
                {
                    countsByInviteCode[inviteCode] = count;
                }
            }

            foreach (var server in list)
            {
                if (TryExtractInviteCode(server.InviteLink, out var inviteCode) &&
                    countsByInviteCode.TryGetValue(inviteCode, out var count))
                {
                    server.MemberCount = count;
                }
            }

            return list
                .OrderByDescending(server => server.MemberCount)
                .ThenBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void OpenDiscordLink(string inviteLink)
        {
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
                System.Diagnostics.Debug.WriteLine($"Nie udało się otworzyć linku Discord: {ex.Message}");
            }
        }

        private static string Loc(string key)
        {
            return App.GetService<ILocalizationService>().Get(key);
        }

        private static string LocFmt(string key, params object[] args)
        {
            return App.GetService<ILocalizationService>().GetFormatted(key, args);
        }

        private static async Task<List<DiscordServer>> FilterValidInvitesAsync(List<DiscordServer> servers)
        {
            if (servers.Count == 0)
            {
                return servers;
            }

            using var semaphore = new SemaphoreSlim(4);

            var tasks = servers.Select(async server =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var isValid = await IsInviteValidAsync(server.InviteLink).ConfigureAwait(false);
                    return (Server: server, IsValid: isValid);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            return results
                .Where(result => result.IsValid)
                .Select(result => result.Server)
                .ToList();
        }

        private static async Task<List<DiscordServerViewModel>> FilterValidInvitesAsync(List<DiscordServerViewModel> servers)
        {
            if (servers.Count == 0)
            {
                return servers;
            }

            using var semaphore = new SemaphoreSlim(4);

            var tasks = servers.Select(async server =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var isValid = await IsInviteValidAsync(server.InviteLink).ConfigureAwait(false);
                    return (Server: server, IsValid: isValid);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            return results
                .Where(result => result.IsValid)
                .Select(result => result.Server)
                .ToList();
        }

        private static async Task<bool> IsInviteValidAsync(string inviteLink)
        {
            if (string.IsNullOrWhiteSpace(inviteLink))
            {
                return false;
            }

            if (!TryExtractInviteCode(inviteLink, out var code))
            {
                // Jeśli nie potrafimy wyciągnąć kodu, nie blokujmy wyświetlania.
                return true;
            }

            if (TryGetCachedInviteValidation(code, out var cached))
            {
                return cached;
            }

            try
            {
                var url = $"https://discord.com/api/v10/invites/{code}?with_counts=false&with_expiration=true";
                using var response = await _inviteValidationClient.GetAsync(url).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    CacheInviteValidation(code, true);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                {
                    CacheInviteValidation(code, false);
                    return false;
                }

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    // Rate limit - nie ukrywaj przy ograniczeniu.
                    return true;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (body.Contains("\"code\": 10006", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("Unknown Invite", StringComparison.OrdinalIgnoreCase))
                {
                    CacheInviteValidation(code, false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Discord Invite Validation] Error: {ex.Message}");
            }

            return true;
        }

        private static bool TryExtractInviteCode(string inviteLink, out string code)
        {
            code = string.Empty;

            var trimmed = inviteLink.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                if (IsLikelyInviteCode(trimmed))
                {
                    code = trimmed;
                    return true;
                }

                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath.Trim('/');

            if (host.EndsWith("discord.gg"))
            {
                if (path.StartsWith("invite/", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring("invite/".Length);
                }

                var candidate = path.Split('/')[0];
                if (IsLikelyInviteCode(candidate))
                {
                    code = candidate;
                    return true;
                }

                return false;
            }

            if (host.EndsWith("discord.com") || host.EndsWith("discordapp.com"))
            {
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && segments[0].Equals("invite", StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = segments[1];
                    if (IsLikelyInviteCode(candidate))
                    {
                        code = candidate;
                        return true;
                    }
                }
                else if (segments.Length >= 3 &&
                         segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                         segments[1].Equals("invites", StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = segments[2];
                    if (IsLikelyInviteCode(candidate))
                    {
                        code = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsLikelyInviteCode(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            if (candidate.Length < 2 || candidate.Length > 64)
            {
                return false;
            }

            return candidate.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_');
        }

        private static bool TryGetCachedInviteValidation(string code, out bool isValid)
        {
            isValid = false;

            if (_inviteValidationCache.TryGetValue(code, out var cached))
            {
                if (DateTimeOffset.UtcNow - cached.CheckedAt <= _inviteCacheTtl)
                {
                    isValid = cached.IsValid;
                    return true;
                }

                _inviteValidationCache.TryRemove(code, out _);
            }

            return false;
        }

        private static void CacheInviteValidation(string code, bool isValid)
        {
            _inviteValidationCache[code] = (isValid, DateTimeOffset.UtcNow);
        }

        private static HttpClient CreateInviteValidationClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("SUSModder/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            return client;
        }
    }

    public class UIDiagnosticsOutput : IDiagnosticsOutput
    {
        private readonly Action<string> _messageCallback;

        public UIDiagnosticsOutput(Action<string> messageCallback)
        {
            _messageCallback = messageCallback;
        }

        public void Write(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() => _messageCallback(message));
        }
    }
}

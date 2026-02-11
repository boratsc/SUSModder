using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SUSModder.Core.Models;
using SUSModder.Core.Configuration;
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

namespace SUSModder.ViewModels
{
    public class RecommendedDiscordsViewModel : ViewModelBase
    {
        private static readonly HttpClient _inviteValidationClient = CreateInviteValidationClient();
        private static readonly ConcurrentDictionary<string, (bool IsValid, DateTimeOffset CheckedAt)> _inviteValidationCache = new();
        private static readonly TimeSpan _inviteCacheTtl = TimeSpan.FromMinutes(30);

        private bool _isLoading = true;
        private string _statusMessage = "Ładowanie serwerów Discord...";

        public ObservableCollection<DiscordServerViewModel> DiscordServers { get; } = new();

        public ReactiveCommand<string, Unit> OpenDiscordLinkCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public RecommendedDiscordsViewModel()
        {
            OpenDiscordLinkCommand = ReactiveCommand.Create<string>(OpenDiscordLink);
            CloseCommand = ReactiveCommand.Create(() => { });
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadDiscordServersAsync);

            // Załaduj dane przy inicjalizacji
            _ = LoadDiscordServersAsync();
        }

        private async Task LoadDiscordServersAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Ładowanie serwerów Discord...";

                // Sprawdź czy ikony są już preloadowane
                var preloadedServers = DiscordIconPreloader.GetPreloadedServers();

                if (preloadedServers != null && preloadedServers.Any())
                {
                    var filteredPreloadedServers = await FilterValidInvitesAsync(preloadedServers);

                    // Użyj preloadowanych ViewModels z ikonami
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DiscordServers.Clear();
                        foreach (var serverVM in filteredPreloadedServers)
                        {
                            DiscordServers.Add(serverVM);
                        }
                        StatusMessage = $"Załadowano {filteredPreloadedServers.Count} serwerów Discord";
                    });
                }
                else
                {
                    // Fallback - stwórz ViewModels bez ikon
                    StatusMessage = "Pobieranie listy serwerów Discord...";

                    var configBuilder = new ConfigurationBuilder()
                        .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    var configuration = configBuilder.Build();

                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Discord Service] {message}");
                    });

                    var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
                    var serverDataList = await discordService.GetDiscordFavoritesAsync();
                    var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);
                    var filteredDiscordServers = await FilterValidInvitesAsync(discordServers);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DiscordServers.Clear();

                        if (filteredDiscordServers.Any())
                        {
                            foreach (var server in filteredDiscordServers)
                            {
                                var serverVM = new DiscordServerViewModel(server);
                                DiscordServers.Add(serverVM);
                                // Załaduj ikony w tle
                                _ = Task.Run(async () => await serverVM.LoadIconAsync());
                            }
                            StatusMessage = $"Załadowano {filteredDiscordServers.Count} serwerów Discord";
                        }
                        else
                        {
                            if (!discordServers.Any())
                            {
                                LoadPlaceholderData();
                                StatusMessage = "Używam danych przykładowych (problem z połączeniem)";
                            }
                            else
                            {
                                StatusMessage = "Załadowano 0 serwerów Discord";
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
                    StatusMessage = "Błąd połączenia - używam danych przykładowych";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = false;
                });
            }
        }

        private void LoadPlaceholderData()
        {
            DiscordServers.Clear();

            var placeholderServers = new[]
            {
                new DiscordServer
                {
                    Name = "Among Us Polska",
                    InviteLink = "https://discord.gg/example1",
                    Description = "Największa polska społeczność Among Us. Znajdziesz tu graczy, turnieje i najnowsze informacje o grze.",
                    IconPath = null
                },
                new DiscordServer
                {
                    Name = "Town of Us Community",
                    InviteLink = "https://discord.gg/example2",
                    Description = "Oficjalny serwer moda Town of Us. Wsparcie techniczne, aktualizacje i społeczność.",
                    IconPath = null
                },
                new DiscordServer
                {
                    Name = "The Other Roles",
                    InviteLink = "https://discord.gg/example3",
                    Description = "Społeczność moda The Other Roles. Dyskusje o nowych rolach i strategiach gry.",
                    IconPath = null
                },
                new DiscordServer
                {
                    Name = "SUSModder Support",
                    InviteLink = "https://discord.gg/example4",
                    Description = "Oficjalny serwer wsparcia dla SUSModder. Pomoc techniczna i zgłaszanie błędów.",
                    IconPath = null
                }
            };

            foreach (var server in placeholderServers)
            {
                DiscordServers.Add(new DiscordServerViewModel(server));
            }
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

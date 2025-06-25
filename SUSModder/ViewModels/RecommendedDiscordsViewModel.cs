using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Diagnostics;
using System;
using SUSModder.Core.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Linq;
using SUSModder.Services;

namespace SUSModder.ViewModels
{
    public class RecommendedDiscordsViewModel : ViewModelBase
    {
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
                    // Użyj preloadowanych ViewModels z ikonami
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DiscordServers.Clear();
                        foreach (var serverVM in preloadedServers)
                        {
                            DiscordServers.Add(serverVM);
                        }
                        StatusMessage = $"Załadowano {preloadedServers.Count} serwerów Discord";
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

                    using var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
                    var serverDataList = await discordService.GetDiscordFavoritesAsync();
                    var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DiscordServers.Clear();

                        if (discordServers.Any())
                        {
                            foreach (var server in discordServers)
                            {
                                var serverVM = new DiscordServerViewModel(server);
                                DiscordServers.Add(serverVM);
                                // Załaduj ikony w tle
                                _ = Task.Run(async () => await serverVM.LoadIconAsync());
                            }
                            StatusMessage = $"Załadowano {discordServers.Count} serwerów Discord";
                        }
                        else
                        {
                            LoadPlaceholderData();
                            StatusMessage = "Używam danych przykładowych (problem z połączeniem)";
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

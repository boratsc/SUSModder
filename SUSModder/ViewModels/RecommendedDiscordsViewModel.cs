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

namespace SUSModder.ViewModels
{
    public class RecommendedDiscordsViewModel : ViewModelBase
    {
        private bool _isLoading = true;
        private string _statusMessage = "Ładowanie serwerów Discord...";

        public ObservableCollection<DiscordServer> DiscordServers { get; } = new();

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
                StatusMessage = "Pobieranie listy serwerów Discord...";

                // Stwórz konfigurację
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                // Stwórz diagnostics output
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Discord Service] {message}");
                });

                // Pobierz dane z API
                using var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
                var serverDataList = await discordService.GetDiscordFavoritesAsync();

                // Konwertuj na model UI
                var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);

                // Aktualizuj UI na głównym wątku
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DiscordServers.Clear();

                    if (discordServers.Any())
                    {
                        foreach (var server in discordServers)
                        {
                            DiscordServers.Add(server);
                        }
                        StatusMessage = $"Załadowano {discordServers.Count} serwerów Discord";
                    }
                    else
                    {
                        // Fallback do placeholder danych jeśli API nie działa
                        LoadPlaceholderData();
                        StatusMessage = "Używam danych przykładowych (problem z połączeniem)";
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Discord servers: {ex.Message}");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Fallback do placeholder danych
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

            DiscordServers.Add(new DiscordServer
            {
                Name = "Among Us Polska",
                InviteLink = "https://discord.gg/example1",
                Description = "Największa polska społeczność Among Us. Znajdziesz tu graczy, turnieje i najnowsze informacje o grze.",
                IconPath = null
            });

            DiscordServers.Add(new DiscordServer
            {
                Name = "Town of Us Community",
                InviteLink = "https://discord.gg/example2",
                Description = "Oficjalny serwer moda Town of Us. Wsparcie techniczne, aktualizacje i społeczność.",
                IconPath = null
            });

            DiscordServers.Add(new DiscordServer
            {
                Name = "The Other Roles",
                InviteLink = "https://discord.gg/example3",
                Description = "Społeczność moda The Other Roles. Dyskusje o nowych rolach i strategiach gry.",
                IconPath = null
            });

            DiscordServers.Add(new DiscordServer
            {
                Name = "SUSModder Support",
                InviteLink = "https://discord.gg/example4",
                Description = "Oficjalny serwer wsparcia dla SUSModder. Pomoc techniczna i zgłaszanie błędów.",
                IconPath = null
            });
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

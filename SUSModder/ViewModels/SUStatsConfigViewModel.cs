using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using System;
using SUSModder.Core.Models;
using SUSModder.Core.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;
using SUSModder.Core.Diagnostics;
using Avalonia.Threading;
using System.Linq;
using Avalonia.Media;

namespace SUSModder.ViewModels
{
    public class SUStatsConfigViewModel : ViewModelBase
    {
        // Statyczne pole dla globalnego wyboru
        public static AmongToken? GlobalSelectedServer { get; set; }

        // Właściwości dla hasła
        private string _enteredPassword = string.Empty;
        private bool _isCheckingPassword = false;
        private string _passwordStatusMessage = string.Empty;
        private IBrush _passwordStatusColor = Brushes.Gray;
        private bool _isPasswordValid = false;

        // Właściwości dla serwera
        private string _validatedServerName = string.Empty;
        private string _validatedServerEndpoint = string.Empty;
        private AmongToken? _validatedServerData;

        // Właściwości dla switch
        private bool _isStatsEnabled = false;
        private bool _showStatsStatus = false;
        private string _statsStatusMessage = string.Empty;
        private string _statsStatusIcon = string.Empty;
        private IBrush _statsStatusBackgroundBrush = Brushes.Transparent;
        private IBrush _statsStatusBorderBrush = Brushes.Gray;
        private IBrush _statsStatusTextBrush = Brushes.Gray;

        public ObservableCollection<AmongToken> Servers { get; } = new();

        // Właściwości dla hasła
        public string EnteredPassword
        {
            get => _enteredPassword;
            set => this.RaiseAndSetIfChanged(ref _enteredPassword, value);
        }

        public bool IsCheckingPassword
        {
            get => _isCheckingPassword;
            set => this.RaiseAndSetIfChanged(ref _isCheckingPassword, value);
        }

        public string PasswordStatusMessage
        {
            get => _passwordStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _passwordStatusMessage, value);
        }

        public IBrush PasswordStatusColor
        {
            get => _passwordStatusColor;
            set => this.RaiseAndSetIfChanged(ref _passwordStatusColor, value);
        }

        public bool IsPasswordValid
        {
            get => _isPasswordValid;
            set => this.RaiseAndSetIfChanged(ref _isPasswordValid, value);
        }

        // Właściwości dla serwera
        public string ValidatedServerName
        {
            get => _validatedServerName;
            set => this.RaiseAndSetIfChanged(ref _validatedServerName, value);
        }

        public string ValidatedServerEndpoint
        {
            get => _validatedServerEndpoint;
            set => this.RaiseAndSetIfChanged(ref _validatedServerEndpoint, value);
        }

        // Właściwości dla switch
        public bool IsStatsEnabled
        {
            get => _isStatsEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _isStatsEnabled, value);
                OnStatsEnabledChanged(value);
            }
        }

        public bool ShowStatsStatus
        {
            get => _showStatsStatus;
            set => this.RaiseAndSetIfChanged(ref _showStatsStatus, value);
        }

        public string StatsStatusMessage
        {
            get => _statsStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _statsStatusMessage, value);
        }

        public string StatsStatusIcon
        {
            get => _statsStatusIcon;
            set => this.RaiseAndSetIfChanged(ref _statsStatusIcon, value);
        }

        public IBrush StatsStatusBackgroundBrush
        {
            get => _statsStatusBackgroundBrush;
            set => this.RaiseAndSetIfChanged(ref _statsStatusBackgroundBrush, value);
        }

        public IBrush StatsStatusBorderBrush
        {
            get => _statsStatusBorderBrush;
            set => this.RaiseAndSetIfChanged(ref _statsStatusBorderBrush, value);
        }

        public IBrush StatsStatusTextBrush
        {
            get => _statsStatusTextBrush;
            set => this.RaiseAndSetIfChanged(ref _statsStatusTextBrush, value);
        }

        // Komendy
        public ReactiveCommand<Unit, Unit> CheckPasswordCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveSelectionCommand { get; }

        // Statyczne właściwości dla kompatybilności
        public static bool HasSelectedServer => GlobalSelectedServer != null;

        public static (int id, string serverName, string token, string secret, string endpoint)? GetSelectedServerData()
        {
            if (GlobalSelectedServer == null) return null;

            return (
                GlobalSelectedServer.Id,
                GlobalSelectedServer.ServerName,
                GlobalSelectedServer.Token,
                GlobalSelectedServer.Secret,
                GlobalSelectedServer.Endpoint
            );
        }

        public static void ClearGlobalSelection()
        {
            GlobalSelectedServer = null;
            System.Diagnostics.Debug.WriteLine("[SUStats] GlobalSelectedServer został wyczyszczony");
        }

        public SUStatsConfigViewModel()
        {
            CheckPasswordCommand = ReactiveCommand.CreateFromTask(CheckPasswordAsync);
            ResetCommand = ReactiveCommand.Create(ResetConfiguration);
            SaveSelectionCommand = ReactiveCommand.Create(() => { });

            // Załaduj serwery przy inicjalizacji
            _ = LoadServersAsync();
        }

        private Task CheckPasswordAsync()
        {
            try
            {
                IsCheckingPassword = true;
                PasswordStatusMessage = "Sprawdzanie hasła...";
                PasswordStatusColor = Brushes.Orange;

                System.Diagnostics.Debug.WriteLine($"[SUStats] Sprawdzanie hasła: {EnteredPassword}");

                if (string.IsNullOrWhiteSpace(EnteredPassword))
                {
                    PasswordStatusMessage = "❌ Wprowadź hasło";
                    PasswordStatusColor = Brushes.Red;
                    return Task.CompletedTask;
                }

                // Znajdź serwer z pasującym secretem
                var matchingServer = Servers.FirstOrDefault(s => s.Secret.Equals(EnteredPassword, StringComparison.Ordinal));

                if (matchingServer != null)
                {
                    // Hasło poprawne
                    IsPasswordValid = true;
                    _validatedServerData = matchingServer;
                    ValidatedServerName = matchingServer.ServerName;
                    ValidatedServerEndpoint = matchingServer.Endpoint;

                    PasswordStatusMessage = "✅ Hasło poprawne";
                    PasswordStatusColor = Brushes.Green;

                    // Sprawdź czy ten serwer jest już wybrany
                    if (GlobalSelectedServer?.Id == matchingServer.Id)
                    {
                        IsStatsEnabled = true;
                        UpdateStatsStatus(true, "Rejestrowanie wyników jest włączone");
                    }
                    else
                    {
                        IsStatsEnabled = false;
                        UpdateStatsStatus(false, "Rejestrowanie wyników jest wyłączone");
                    }

                    System.Diagnostics.Debug.WriteLine($"[SUStats] ✅ Hasło poprawne dla serwera: {matchingServer.ServerName}");
                }
                else
                {
                    // Hasło niepoprawne
                    IsPasswordValid = false;
                    PasswordStatusMessage = "❌ Niepoprawne hasło";
                    PasswordStatusColor = Brushes.Red;
                    System.Diagnostics.Debug.WriteLine("[SUStats] ❌ Niepoprawne hasło");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd podczas sprawdzania hasła: {ex.Message}");
                PasswordStatusMessage = "❌ Błąd podczas sprawdzania hasła";
                PasswordStatusColor = Brushes.Red;
                IsPasswordValid = false;
            }
            finally
            {
                IsCheckingPassword = false;
            }

            return Task.CompletedTask;
        }

        private void OnStatsEnabledChanged(bool enabled)
        {
            if (!IsPasswordValid || _validatedServerData == null) return;

            try
            {
                if (enabled)
                {
                    // Włącz statystyki - zapisz wybór
                    GlobalSelectedServer = _validatedServerData;
                    UpdateStatsStatus(true, $"Rejestrowanie włączone dla serwera: {_validatedServerData.ServerName}");
                    System.Diagnostics.Debug.WriteLine($"[SUStats] ✅ Włączono statystyki dla serwera: {_validatedServerData.ServerName}");
                }
                else
                {
                    // Wyłącz statystyki - wyczyść wybór
                    GlobalSelectedServer = null;
                    UpdateStatsStatus(false, "Rejestrowanie wyników zostało wyłączone");
                    System.Diagnostics.Debug.WriteLine("[SUStats] ❌ Wyłączono statystyki");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd podczas zmiany stanu statystyk: {ex.Message}");
                UpdateStatsStatus(false, "Błąd podczas zmiany ustawień");
            }
        }

        private void UpdateStatsStatus(bool enabled, string message)
        {
            ShowStatsStatus = true;
            StatsStatusMessage = message;

            if (enabled)
            {
                StatsStatusIcon = "✅";
                StatsStatusBackgroundBrush = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)); // Zielone tło
                StatsStatusBorderBrush = Brushes.Green;
                StatsStatusTextBrush = Brushes.Green;
            }
            else
            {
                StatsStatusIcon = "❌";
                StatsStatusBackgroundBrush = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)); // Czerwone tło
                StatsStatusBorderBrush = Brushes.Red;
                StatsStatusTextBrush = Brushes.Red;
            }
        }

        private void ResetConfiguration()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[SUStats] 🔄 Resetowanie konfiguracji...");

                // Wyczyść wszystkie pola
                EnteredPassword = string.Empty;
                IsPasswordValid = false;
                IsStatsEnabled = false;
                ShowStatsStatus = false;
                PasswordStatusMessage = string.Empty;
                ValidatedServerName = string.Empty;
                ValidatedServerEndpoint = string.Empty;
                _validatedServerData = null;

                // Wyczyść globalny wybór
                GlobalSelectedServer = null;

                System.Diagnostics.Debug.WriteLine("[SUStats] ✅ Konfiguracja została zresetowana");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd podczas resetowania: {ex.Message}");
            }
        }

        private async Task LoadServersAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[SUStats] Ładowanie listy serwerów...");

                // Stwórz konfigurację
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                // Stwórz diagnostics output
                var diagnosticsOutput = new SUStatsDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SUStats Service] {message}");
                });

                // Pobierz dane z API
                using var suStatsService = new SUStatsService(configuration, diagnosticsOutput);
                var serverDataList = await suStatsService.GetSUStatsServersAsync();

                // Aktualizuj UI na głównym wątku
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Servers.Clear();

                    if (serverDataList.Any())
                    {
                        foreach (var server in serverDataList)
                        {
                            Servers.Add(server);
                        }
                        System.Diagnostics.Debug.WriteLine($"[SUStats] Załadowano {serverDataList.Count} serwerów");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[SUStats] ⚠️ Brak serwerów do załadowania");
                        // Dodaj testowy serwer
                        Servers.Add(new AmongToken
                        {
                            Id = 1,
                            ServerName = "Testowy serwer SUStats",
                            Token = "test_token",
                            Secret = "secret",
                            Endpoint = "https://example.com/api"
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd podczas ładowania serwerów: {ex.Message}");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Fallback - dodaj przykładowy serwer do testów
                    Servers.Clear();
                    Servers.Add(new AmongToken
                    {
                        Id = 1,
                        ServerName = "Serwer testowy (offline)",
                        Token = "test_token",
                        Secret = "secret",
                        Endpoint = "https://example.com/api"
                    });
                });
            }
        }
    }

    // Osobna klasa diagnostics dla SUStats żeby uniknąć konfliktu
    public class SUStatsDiagnosticsOutput : IDiagnosticsOutput
    {
        private readonly Action<string> _messageCallback;

        public SUStatsDiagnosticsOutput(Action<string> messageCallback)
        {
            _messageCallback = messageCallback;
        }

        public void Write(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() => _messageCallback(message));
        }
    }
}

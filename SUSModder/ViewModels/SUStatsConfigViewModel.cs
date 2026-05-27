using ReactiveUI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Diagnostics;
using System.Net;
using SUSModder.Core.Models;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services.Discord;
using SUSModder.Core.Data;
using SUSModder.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// ViewModel dla konfiguracji SUStats przez Discord OAuth2.
    /// Zarządza flow logowania Discord OAuth2 PKCE, wyborem serwera i aktywacją SUStats.
    /// </summary>
    public class SUStatsConfigViewModel : ViewModelBase
    {
        // --- Statyczne pola dla kompatybilności wstecznej (GameLaunch) ---
        public static AmongToken? GlobalSelectedServer { get; set; }

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
            Debug.WriteLine("[SUStats] GlobalSelectedServer został wyczyszczony");
        }

        /// <summary>
        /// Auto-logowanie przy starcie — przywraca aktywną guildę z SQLite.
        /// </summary>
        public static async Task TryAutoLoginOnStartupAsync()
        {
            try
            {
                Debug.WriteLine("[SUStats] TryAutoLoginOnStartupAsync: odtwarzanie z SQLite...");
                var repo = App.GetService<ISustatsCredentialsRepository>();
                var active = await repo.GetActiveAsync();
                if (active != null)
                {
                    GlobalSelectedServer = new AmongToken
                    {
                        Id = 0,
                        ServerName = active.ServerName,
                        Token = active.Token,
                        Secret = active.Secret,
                        Endpoint = active.Endpoint,
                    };
                    Debug.WriteLine($"[SUStats] Przywrócono aktywną guildę: {active.ServerName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SUStats] Błąd auto-logowania: {ex.Message}");
            }
        }

        // --- DI serwisy (null! — inicjalizowane przez konstruktor DI, design-time konstruktor ich nie używa) ---
        private readonly IDiscordOAuthService _discordOAuthService = null!;
        private readonly IClairDiscordService _clairDiscordService = null!;
        private readonly ISustatsCredentialsRepository _sustatsRepo = null!;
        private readonly IUserSettingsRepository _userSettingsRepo = null!;
        private readonly IDiscordAuthRepository _discordAuthRepo = null!;
        private readonly IDiagnosticsOutput _diagnosticsOutput = null!;
        private readonly ILocalizationService _localizationService = null!;
        private readonly OAuthLoopbackListener _loopbackListener = null!;

        // --- Stany OAuth ---
        private bool _isLoggedIn;
        private string? _discordUsername;
        private bool _isLoadingGuilds;
        private bool _isStatsEnabled;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private bool _isLoginInProgress;
        private bool _isStatsToggleVisible;

        // --- Guild selection ---
        public ObservableCollection<DiscordGuildInfo> AvailableGuilds { get; } = new();
        private DiscordGuildInfo? _selectedGuild;

        // =====================================================================
        // Properties (Notify)
        // =====================================================================

        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set
            {
                this.RaiseAndSetIfChanged(ref _isLoggedIn, value);
                this.RaisePropertyChanged(nameof(ShowLoginPanel));
                this.RaisePropertyChanged(nameof(ShowLoggedInPanel));
            }
        }

        public bool IsLoginInProgress
        {
            get => _isLoginInProgress;
            set
            {
                this.RaiseAndSetIfChanged(ref _isLoginInProgress, value);
                this.RaisePropertyChanged(nameof(ShowLoginPanel));
            }
        }

        public string? DiscordUsername
        {
            get => _discordUsername;
            set
            {
                this.RaiseAndSetIfChanged(ref _discordUsername, value);
                this.RaisePropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText =>
            _localizationService?.GetFormatted("DiscordAuth.LoggedInAs", DiscordUsername ?? "???")
            ?? $"Zalogowano jako: {DiscordUsername}";

        public bool IsLoadingGuilds
        {
            get => _isLoadingGuilds;
            set
            {
                this.RaiseAndSetIfChanged(ref _isLoadingGuilds, value);
                this.RaisePropertyChanged(nameof(ShowLoadingGuilds));
                this.RaisePropertyChanged(nameof(ShowNoGuildsMessage));
                this.RaisePropertyChanged(nameof(ShowGuildSelector));
            }
        }

        public bool IsStatsEnabled
        {
            get => _isStatsEnabled;
            set => this.RaiseAndSetIfChanged(ref _isStatsEnabled, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => this.RaiseAndSetIfChanged(ref _hasError, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref _errorMessage, value);
                HasError = !string.IsNullOrEmpty(value);
            }
        }

        public bool IsStatsToggleVisible
        {
            get => _isStatsToggleVisible;
            set => this.RaiseAndSetIfChanged(ref _isStatsToggleVisible, value);
        }

        public DiscordGuildInfo? SelectedGuild
        {
            get => _selectedGuild;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedGuild, value);
                if (value != null)
                    _ = SelectGuildAsync(value);
            }
        }

        // --- Derived properties ---
        public bool ShowLoginPanel => !IsLoggedIn && !IsLoginInProgress;
        public bool ShowLoggedInPanel => IsLoggedIn;
        public bool ShowGuildSelector => IsLoggedIn && AvailableGuilds.Count > 0;
        public bool ShowNoGuildsMessage => IsLoggedIn && AvailableGuilds.Count == 0 && !IsLoadingGuilds;
        public bool ShowLoadingGuilds => IsLoadingGuilds;

        // --- Commands ---
        public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoginCommand { get; }
        public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LogoutCommand { get; }
        public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshGuildsCommand { get; }

        // =====================================================================
        // Constructors
        // =====================================================================

        /// <summary>
        /// Konstruktor dla design-time / Avalonia designer.
        /// Bez serwisów DI — tylko inicjalizacja komend.
        /// </summary>
        public SUStatsConfigViewModel()
        {
            // Design-time: brak serwisów, tylko komendy
            LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
            LogoutCommand = ReactiveCommand.CreateFromTask(LogoutAsync);
            RefreshGuildsCommand = ReactiveCommand.CreateFromTask(RefreshGuildsAsync);
        }

        /// <summary>
        /// Konstruktor DI — pełna inicjalizacja z wszystkimi serwisami.
        /// </summary>
        public SUStatsConfigViewModel(
            IDiscordOAuthService discordOAuthService,
            IClairDiscordService clairDiscordService,
            ISustatsCredentialsRepository sustatsRepo,
            IUserSettingsRepository userSettingsRepo,
            IDiscordAuthRepository discordAuthRepo,
            IDiagnosticsOutput diagnosticsOutput,
            ILocalizationService localizationService,
            OAuthLoopbackListener loopbackListener)
        {
            _discordOAuthService = discordOAuthService;
            _clairDiscordService = clairDiscordService;
            _sustatsRepo = sustatsRepo;
            _userSettingsRepo = userSettingsRepo;
            _discordAuthRepo = discordAuthRepo;
            _diagnosticsOutput = diagnosticsOutput;
            _localizationService = localizationService;
            _loopbackListener = loopbackListener;

            LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
            LogoutCommand = ReactiveCommand.CreateFromTask(LogoutAsync);
            RefreshGuildsCommand = ReactiveCommand.CreateFromTask(RefreshGuildsAsync);

            // Przy starcie sprawdź czy użytkownik jest już zalogowany
            _ = RestoreSessionAsync();
        }

        // =====================================================================
        // OAuth Flow
        // =====================================================================

        /// <summary>
        /// Przywraca sesję po restarcie aplikacji — sprawdza czy token Discord jest ważny.
        /// </summary>
        private async Task RestoreSessionAsync()
        {
            try
            {
                if (_discordOAuthService == null) return;

                var isLoggedIn = await _discordOAuthService.IsLoggedInAsync();
                if (isLoggedIn)
                {
                    var username = await _discordOAuthService.GetUsernameAsync();
                    DiscordUsername = username ?? "Discord User";
                    IsLoggedIn = true;

                    LogDiagnostics($"[SUStats] Przywrócono sesję jako: {DiscordUsername}");
                    await RefreshGuildsAsync();
                    await RestoreActiveGuildAsync();
                }
            }
            catch (Exception ex)
            {
                LogDiagnostics($"[SUStats] Błąd przywracania sesji: {ex.Message}");
            }
        }

        /// <summary>
        /// Pełny flow logowania Discord OAuth2 PKCE:
        /// 1. Generuje URL autoryzacji
        /// 2. Otwiera przeglądarkę systemową
        /// 3. Uruchamia loopback listener na 127.0.0.1:53124
        /// 4. Czeka na kod autoryzacyjny
        /// 5. Wymienia kod na token
        /// 6. Pobiera guilds i aktualizuje UI
        /// </summary>
        private async Task LoginAsync()
        {
            try
            {
                IsLoginInProgress = true;
                ErrorMessage = string.Empty;

                LogDiagnostics("[SUStats] Rozpoczynanie logowania przez Discord OAuth...");

                // 1. Pobierz URL autoryzacji
                var startResult = await _discordOAuthService.StartLoginAsync();
                if (string.IsNullOrEmpty(startResult?.AuthUrl))
                {
                    ErrorMessage = _localizationService?.Get("DiscordAuth.LoginError")
                        ?? "Nie udało się uzyskać URL autoryzacji.";
                    IsLoginInProgress = false;
                    return;
                }

                // 2. Uruchom loopback listener
                var codeTcs = new TaskCompletionSource<string>();
                var errorTcs = new TaskCompletionSource<string>();

                _loopbackListener.CodeReceived += code => codeTcs.TrySetResult(code);
                _loopbackListener.ErrorOccurred += err => errorTcs.TrySetResult(err);

                await _loopbackListener.StartAsync(startResult.Port);

                // Sprawdź czy listener się nie wywalił przy starcie (np. port zajęty)
                if (errorTcs.Task.IsCompleted)
                {
                    var startupError = await errorTcs.Task;
                    ErrorMessage = startupError;
                    IsLoginInProgress = false;
                    return;
                }

                LogDiagnostics($"[SUStats] Nasłuchiwanie na http://127.0.0.1:{startResult.Port}/susmodder/callback");

                // 3. Otwórz przeglądarkę systemową
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = startResult.AuthUrl
                });

                LogDiagnostics($"[SUStats] Otwarto przeglądarkę: {startResult.AuthUrl}");

                // 4. Czekaj na kod autoryzacyjny (max 5 minut)
                var completedTask = await Task.WhenAny(
                    codeTcs.Task,
                    errorTcs.Task,
                    Task.Delay(TimeSpan.FromMinutes(5))
                );

                if (completedTask == errorTcs.Task || errorTcs.Task.IsCompleted)
                {
                    var err = await errorTcs.Task;
                    ErrorMessage = _localizationService?.GetFormatted("DiscordAuth.LoginError", err)
                        ?? $"Błąd autoryzacji: {err}";
                    IsLoginInProgress = false;
                    return;
                }

                if (completedTask is Task<string> codeTask && codeTask.IsCompleted)
                {
                    var code = codeTask.Result;

                    // 5. Wymień kod na token
                    var redirectUri = $"http://127.0.0.1:{startResult.Port}/susmodder/callback";
                    var completeResult = await _discordOAuthService.CompleteLoginAsync(code, redirectUri);

                    if (completeResult is not { Success: true })
                    {
                        var errorMsg = completeResult?.ErrorMessage ?? "Nieznany błąd logowania.";
                        ErrorMessage = _localizationService?.GetFormatted("DiscordAuth.LoginError", errorMsg)
                            ?? $"Błąd logowania: {errorMsg}";
                        IsLoginInProgress = false;
                        return;
                    }

                    // 6. Pobierz nazwę użytkownika
                    var username = await _discordOAuthService.GetUsernameAsync();
                    DiscordUsername = username ?? "Discord User";
                    IsLoggedIn = true;
                    IsLoginInProgress = false;

                    LogDiagnostics($"[SUStats] Zalogowano jako: {DiscordUsername}");

                    // 7. Załaduj guilds
                    await RefreshGuildsAsync();

                    // 8. Sprawdź czy jest aktywna konfiguracja SUStats
                    await RestoreActiveGuildAsync();
                }
                else
                {
                    // Timeout
                    ErrorMessage = _localizationService?.Get("DiscordAuth.SessionExpired")
                        ?? "Sesja Discord wygasła. Zaloguj się ponownie.";
                    IsLoginInProgress = false;
                }
            }
            catch (Exception ex)
            {
                LogDiagnostics($"[SUStats] Wyjątek podczas logowania: {ex.Message}");
                ErrorMessage = _localizationService?.GetFormatted("DiscordAuth.LoginError", ex.Message)
                    ?? $"Błąd logowania: {ex.Message}";
                IsLoginInProgress = false;
            }
        }

        /// <summary>
        /// Wylogowuje z Discord OAuth i czyści lokalne dane.
        /// </summary>
        private async Task LogoutAsync()
        {
            try
            {
                LogDiagnostics("[SUStats] Wylogowywanie...");
                await _discordOAuthService.LogoutAsync();

                // Wyczyść stan UI
                IsLoggedIn = false;
                DiscordUsername = null;
                AvailableGuilds.Clear();
                IsStatsEnabled = false;
                IsStatsToggleVisible = false;
                ClearGlobalSelection();
                this.RaisePropertyChanged(nameof(ShowLoginPanel));
                this.RaisePropertyChanged(nameof(ShowLoggedInPanel));
                this.RaisePropertyChanged(nameof(ShowGuildSelector));
                this.RaisePropertyChanged(nameof(ShowNoGuildsMessage));

                LogDiagnostics("[SUStats] Wylogowano pomyślnie.");
            }
            catch (Exception ex)
            {
                LogDiagnostics($"[SUStats] Błąd podczas wylogowania: {ex.Message}");
            }
        }

        /// <summary>
        /// Pobiera listę serwerów Discord z Clair API przy użyciu zapisanego tokena.
        /// </summary>
        private async Task RefreshGuildsAsync()
        {
            try
            {
                IsLoadingGuilds = true;
                ErrorMessage = string.Empty;
                LogDiagnostics("[SUStats] Odświeżanie listy serwerów...");

                // 1. Pobierz i odszyfruj access token
                var tokenInfo = await _discordAuthRepo.GetTokenInfoAsync();
                if (tokenInfo == null)
                {
                    LogDiagnostics("[SUStats] Brak tokenu Discord w bazie.");
                    IsLoadingGuilds = false;
                    return;
                }

                var accessToken = CredentialProtector.Unprotect(tokenInfo.AccessTokenEncrypted);

                // 2. Pobierz guilds z Clair API
                var guilds = await _clairDiscordService.GetAccessibleGuildsAsync(accessToken);

                // 3. Aktualizuj UI
                AvailableGuilds.Clear();
                foreach (var g in guilds.OrderByDescending(g => g.HasSustats).ThenBy(g => g.GuildName))
                {
                    AvailableGuilds.Add(g);
                }

                LogDiagnostics($"[SUStats] Załadowano {AvailableGuilds.Count} serwerów.");
                this.RaisePropertyChanged(nameof(ShowGuildSelector));
                this.RaisePropertyChanged(nameof(ShowNoGuildsMessage));
            }
            catch (Exception ex)
            {
                LogDiagnostics($"[SUStats] Błąd ładowania guilds: {ex.Message}");
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLoadingGuilds = false;
            }
        }

        /// <summary>
        /// Obsługuje wybór serwera Discord — pobiera credentials SUStats i zapisuje lokalnie.
        /// </summary>
        private async Task SelectGuildAsync(DiscordGuildInfo guild)
        {
            try
            {
                ErrorMessage = string.Empty;
                LogDiagnostics($"[SUStats] Wybrano serwer: {guild.GuildName} ({guild.GuildId})");

                if (!guild.HasSustats)
                {
                    LogDiagnostics($"[SUStats] Serwer {guild.GuildName} nie ma aktywnego SUStats.");
                    IsStatsEnabled = false;
                    IsStatsToggleVisible = false;
                    return;
                }

                // 1. Pobierz access token
                var tokenInfo = await _discordAuthRepo.GetTokenInfoAsync();
                if (tokenInfo == null)
                {
                    ErrorMessage = _localizationService?.Get("DiscordAuth.SessionExpired")
                        ?? "Sesja Discord wygasła. Zaloguj się ponownie.";
                    return;
                }

                var accessToken = CredentialProtector.Unprotect(tokenInfo.AccessTokenEncrypted);

                // 2. Pobierz credentials z Clair API
                var creds = await _clairDiscordService.GetCredentialsAsync(accessToken, guild.GuildId);
                if (creds == null)
                {
                    ErrorMessage = _localizationService?.GetFormatted("DiscordAuth.CredentialsError", "empty response")
                        ?? "Nie udało się pobrać danych uwierzytelniających.";
                    return;
                }

                // 3. Zaszyfruj token i secret przed zapisem
                creds.TokenEncrypted = CredentialProtector.Protect(creds.Token);
                creds.SecretEncrypted = CredentialProtector.Protect(creds.Secret);

                // 4. Zapisz w SQLite
                await _sustatsRepo.SaveAsync(creds);

                // 5. Zaktualizuj aktywną guildę w user_settings
                _userSettingsRepo.UpdateSetting("active_sustats_guild_id", guild.GuildId);

                // 6. Ustaw dane dla GameLaunch (backward compat)
                GlobalSelectedServer = new AmongToken
                {
                    Id = 0,
                    ServerName = creds.ServerName,
                    Token = creds.Token,
                    Secret = creds.Secret,
                    Endpoint = creds.Endpoint,
                };

                IsStatsEnabled = true;
                IsStatsToggleVisible = true;

                LogDiagnostics($"[SUStats] Credentials zapisane dla serwera: {creds.ServerName}");
            }
            catch (Exception ex)
            {
                LogDiagnostics($"[SUStats] Błąd pobierania credentials: {ex.Message}");
                ErrorMessage = _localizationService?.GetFormatted("DiscordAuth.CredentialsError", ex.Message)
                    ?? $"Błąd pobierania danych uwierzytelniających: {ex.Message}";
            }
        }

        /// <summary>
        /// Przywraca aktywną guildę z SQLite (po restarcie aplikacji).
        /// </summary>
        private async Task RestoreActiveGuildAsync()
        {
            try
            {
                var active = await _sustatsRepo.GetActiveAsync();
                if (active == null) return;

                // Znajdź pasującą guildę na liście
                var match = AvailableGuilds.FirstOrDefault(g => g.GuildId == active.GuildId);
                if (match != null)
                {
                    SelectedGuild = match;
                }

                // Odtwórz GlobalSelectedServer
                GlobalSelectedServer = new AmongToken
                {
                    Id = 0,
                    ServerName = active.ServerName,
                    Token = active.Token,
                    Secret = active.Secret,
                    Endpoint = active.Endpoint,
                };

                IsStatsEnabled = true;
                IsStatsToggleVisible = true;
            }
            catch (Exception ex)
            {
                LogDiagnostics($"[SUStats] Błąd przywracania aktywnej guildy: {ex.Message}");
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private void LogDiagnostics(string message)
        {
            _diagnosticsOutput?.Write(message);
            Debug.WriteLine(message);
        }
    }
}

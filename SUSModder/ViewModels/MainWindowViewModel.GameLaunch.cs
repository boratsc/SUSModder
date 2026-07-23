using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Diagnostics.Launch;
using SUSModder.Core.Utilities;
using SUSModder.Services;
using SUSModder.Views;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający logikę uruchamiania gry
    /// </summary>
    public partial class MainWindowViewModel
    {
        public Task LaunchAsync() => LaunchModItemAsync(SelectedMod);

        /// <summary>
        /// Uruchamia moda o podanym ID (używane przez SystemTrayService do szybkiego uruchamiania).
        /// </summary>
        public async void LaunchModById(int modId)
        {
            var configService = new ConfigService();
            var configs = configService.LoadConfig();
            var modConfig = configs.FirstOrDefault(c => c.Id == modId);

            if (modConfig == null)
                return;

            // Znajdź ModItem pasujący do konfiguracji
            var modItem = Mods?.FirstOrDefault(m =>
                m.Name == modConfig.ModName && m.ModType == modConfig.ModType);

            if (modItem == null)
                return;

            SelectedMod = modItem;
            await LaunchModItemAsync(modItem);
        }

        private void Launch() => _ = LaunchModItemAsync(SelectedMod);

        internal async Task LaunchModItemAsync(ModItem? modItem)
        {
            // 1) Walidacja wyboru
            if (modItem == null)
            {
                await ShowErrorDialogAsync("Nie wybrano wersji gry do uruchomienia.", "Błąd");
                return;
            }

            // Pobierz konfigurację wybranego moda
            var configService = new ConfigService();
            var configs = configService.LoadConfig();
            var modConfig = configs.FirstOrDefault(c => c.ModName == modItem.Name);

            if (modConfig == null)
            {
                await ShowErrorDialogAsync("Brak wybranej wersji do uruchomienia.", "Błąd");
                return;
            }

            var userSettings = _userSettingsService.LoadUserSettings();
            string mode = userSettings.Mode;

            if (!mode.Equals("epic", StringComparison.OrdinalIgnoreCase) &&
                IsVanillaModConfiguration(modConfig) &&
                !await EnsureSteamAmongUsInstallPathAsync())
            {
                return;
            }

            if (IsVanillaModConfiguration(modConfig))
            {
                modConfig = configService.LoadConfig().FirstOrDefault(c => c.ModName == modItem.Name);
                if (modConfig == null)
                {
                    await ShowErrorDialogAsync("Brak wybranej wersji do uruchomienia.", "Błąd");
                    return;
                }
            }

            // Sprawdź czy mod jest zainstalowany
            if (string.IsNullOrEmpty(modConfig.InstallPath))
            {
                await ShowErrorDialogAsync("Wybrany mod nie jest zainstalowany.", "Błąd");
                return;
            }

            // 2) Włączamy UI „busy"
            var currentSelectedMod = modItem;
            currentSelectedMod.ShowProgress = true;
            currentSelectedMod.IsInstalling = true; // Używamy tej flagi do wyłączenia przycisków

            var statsChoice = await HandleSUStatsChoice(modConfig);
            await RemoveApiSetFileIfExists(modConfig);

            if (statsChoice == null)
            {
                // Użytkownik anulował - przerwij uruchamianie
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;
                return;
            }

            if (statsChoice.Value)
            {
                // Użytkownik chce statystyki - utwórz plik
                await CreateApiSetFileIfNeeded(modConfig);
            }
            else
            {
                // Użytkownik nie chce statystyk - usuń plik i wyczyść wybór
                await RemoveApiSetFileIfExists(modConfig);
                ClearSUStatsSelection();
            }

            try
            {
                // 3) Ustalamy tryb uruchomienia
                if (mode.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    await LaunchEpicGameAsync(currentSelectedMod, modConfig);
                }
                else
                {
                    await LaunchSteamGameAsync(currentSelectedMod, modConfig);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Launch] Exception: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas uruchamiania gry: {ex.Message}", "Błąd uruchamiania");
            }
            finally
            {
                // 6) Wyłączamy UI „busy"
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.DownloadSpeed = null;
                currentSelectedMod.IsInstalling = false;
            }
        }

        private async Task LaunchEpicGameAsync(ModItem currentSelectedMod, ModConfiguration modConfig)
        {
            currentSelectedMod.InstallStatusMessage = _localizationService.Get("LaunchDiagnostics.Progress.Starting");
            currentSelectedMod.InstallProgress = 5;

            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Launch Epic] {message}");
            });

            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

            epicManager.ResetErrorState();

            var legendaryErrorDetected = false;
            string legendaryErrorContent = string.Empty;

            epicManager.EpicLaunchError += async (modName, logContent) =>
            {
                legendaryErrorDetected = true;
                legendaryErrorContent = logContent;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowEpicErrorDialogAsync(currentSelectedMod.Name, logContent);
                });
            };

            epicManager.ProgressChanged += (percentage, message) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.InstallProgress = percentage;
                    currentSelectedMod.InstallStatusMessage = message;
                });
            };

            epicManager.SpeedChanged += (speed) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.DownloadSpeed = speed;
                });
            };

            epicManager.LegendaryOutput += (message) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                    System.Diagnostics.Debug.WriteLine($"[Legendary] {message}")
                );
            };

            if (string.IsNullOrEmpty(modConfig.InstallPath))
            {
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("GameLaunch.NoInstallPath");
                return;
            }

            string actualModPath = PathSettings.GetActualModPath(modConfig.InstallPath);
            string exePath = Path.Combine(actualModPath, "Among Us.exe");

            var context = new LaunchContext
            {
                ModId = modConfig.Id,
                ModName = modConfig.ModName ?? currentSelectedMod.Name,
                ModType = modConfig.ModType ?? "full",
                PlatformMode = "epic",
                InstallPath = modConfig.InstallPath,
                ExePath = exePath,
                WasRunAsAdmin = false
            };

            currentSelectedMod.InstallProgress = 15;
            currentSelectedMod.InstallStatusMessage = _localizationService.Get("LaunchDiagnostics.Progress.Starting");

            try
            {
                // 1) Najpierw Legendary: auth / import / download / install.
                // 2) Dopiero sygnał GameLaunchStarting → start obserwacji Among Us.exe.
                var launchStarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                epicManager.GameLaunchStarting += () => launchStarting.TrySetResult();

                var launchTask = epicManager.HandleEpicGameAsync(modConfig);

                var prepareFinished = await Task.WhenAny(launchStarting.Task, launchTask);
                if (prepareFinished == launchTask && !launchStarting.Task.IsCompleted)
                {
                    // Flow zakończył się bez legendary launch (błąd / early return).
                    try
                    {
                        await launchTask;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Launch Epic] Prepare failed: {ex.Message}");
                    }

                    var failed = new LaunchResult
                    {
                        Attempt = new LaunchAttempt
                        {
                            ModId = context.ModId,
                            ModName = context.ModName,
                            ModType = context.ModType,
                            PlatformMode = context.PlatformMode,
                            InstallPath = context.InstallPath,
                            ExePath = context.ExePath,
                            StartedAtUtc = DateTimeOffset.UtcNow
                        },
                        DiagnosisCodes = { DiagnosisCode.ProcessStartFailed },
                        Severity = DiagnosisSeverity.Critical,
                        IsSuccessful = false,
                        TechnicalSummary = legendaryErrorDetected
                            ? "Epic/Legendary launch error detected."
                            : "Epic prepare/install finished without starting the game."
                    };

                    _lastLaunchResult = failed;
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;
                    await Dispatcher.UIThread.InvokeAsync(() => ShowLaunchDiagnostics(failed));
                    return;
                }

                // Legendary zaraz odpala grę — teraz dopiero nasłuchujemy procesu.
                currentSelectedMod.InstallProgress = 85;
                currentSelectedMod.InstallStatusMessage =
                    _localizationService.Get("LaunchDiagnostics.Progress.WaitingForGame");

                var supervisor = new EpicLaunchSupervisor();
                var result = await supervisor.ObserveExternalLaunchAsync(
                    context,
                    launchAction: async _ => await launchTask,
                    processAppearTimeout: TimeSpan.FromMinutes(5),
                    observationWindow: TimeSpan.FromSeconds(60),
                    cancellationToken: CancellationToken.None);

                if (legendaryErrorDetected)
                {
                    result.DiagnosisCodes.Add(DiagnosisCode.ProcessStartFailed);
                    result.Severity = DiagnosisSeverity.Critical;
                    result.IsSuccessful = false;
                    if (string.IsNullOrWhiteSpace(result.TechnicalSummary))
                        result.TechnicalSummary = "Epic/Legendary launch error detected.";
                }

                _lastLaunchResult = result;

                // Jak Steam: panel tylko przy realnym problemie (Critical), nie przy samym Warning/Stale.
                if (result.IsSuccessful)
                {
                    currentSelectedMod.InstallProgress = 100;
                    currentSelectedMod.InstallStatusMessage = _localizationService.Get("GameLaunch.GameStarted");
                }
                else
                {
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;
                    await Dispatcher.UIThread.InvokeAsync(() => ShowLaunchDiagnostics(result));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Launch Epic] Supervisor failed: {ex.Message}");

                var result = new LaunchResult
                {
                    Attempt = new LaunchAttempt
                    {
                        ModId = context.ModId,
                        ModName = context.ModName,
                        ModType = context.ModType,
                        PlatformMode = context.PlatformMode,
                        InstallPath = context.InstallPath,
                        ExePath = context.ExePath
                    },
                    DiagnosisCodes = { DiagnosisCode.ProcessStartFailed },
                    Severity = DiagnosisSeverity.Critical,
                    TechnicalSummary = legendaryErrorDetected
                        ? "Epic/Legendary launch error detected."
                        : ex.Message
                };

                if (legendaryErrorDetected && !string.IsNullOrEmpty(legendaryErrorContent))
                    result.TechnicalSummary = "Epic/Legendary launch error detected.";

                _lastLaunchResult = result;

                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                await Dispatcher.UIThread.InvokeAsync(() => ShowLaunchDiagnostics(result));
            }
        }

        private async Task LaunchSteamGameAsync(ModItem currentSelectedMod, ModConfiguration modConfig)
        {
            currentSelectedMod.InstallStatusMessage = _localizationService.Get("LaunchDiagnostics.Progress.Starting");
            currentSelectedMod.InstallProgress = 25;

            if (string.IsNullOrEmpty(modConfig.InstallPath))
            {
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("GameLaunch.NoInstallPath");
                return;
            }

            string actualModPath = PathSettings.GetActualModPath(modConfig.InstallPath);
            string exePath = Path.Combine(actualModPath, "Among Us.exe");

            var context = new LaunchContext
            {
                ModId = modConfig.Id,
                ModName = modConfig.ModName ?? currentSelectedMod.Name,
                ModType = modConfig.ModType ?? "full",
                PlatformMode = "steam",
                InstallPath = modConfig.InstallPath,
                ExePath = exePath,
                WasRunAsAdmin = false
            };

            try
            {
                currentSelectedMod.InstallProgress = 50;
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("LaunchDiagnostics.Progress.WaitingForGame");

                var supervisor = new SteamLaunchSupervisor();
                var result = await supervisor.LaunchAndObserveAsync(
                    context,
                    observationWindow: TimeSpan.FromSeconds(60),
                    cancellationToken: CancellationToken.None);

                _lastLaunchResult = result;

                if (result.IsSuccessful)
                {
                    currentSelectedMod.InstallProgress = 100;
                    currentSelectedMod.InstallStatusMessage = _localizationService.Get("GameLaunch.GameStarted");
                }
                else
                {
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;

                    await Dispatcher.UIThread.InvokeAsync(() => ShowLaunchDiagnostics(result));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Launch Steam] Supervisor failed: {ex.Message}");

                var result = new LaunchResult
                {
                    Attempt = new LaunchAttempt
                    {
                        ModId = context.ModId,
                        ModName = context.ModName,
                        ModType = context.ModType,
                        PlatformMode = context.PlatformMode,
                        InstallPath = context.InstallPath,
                        ExePath = context.ExePath
                    },
                    DiagnosisCodes = { DiagnosisCode.ProcessStartFailed },
                    Severity = DiagnosisSeverity.Critical,
                    TechnicalSummary = ex.Message
                };

                _lastLaunchResult = result;

                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;

                await Dispatcher.UIThread.InvokeAsync(() => ShowLaunchDiagnostics(result));
            }
        }

        private async Task ShowEpicErrorDialogAsync(string modName, string logContent)
        {
            try
            {
                var dialog = new EpicErrorDialog(modName, logContent);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing Epic error dialog: {ex.Message}");
                // Fallback - pokaż standardowy dialog błędu
                await ShowErrorDialogAsync($"Błąd uruchamiania gry Epic: {ex.Message}\n\nLog:\n{logContent}", "Błąd Epic Games");
            }
        }

        #region SUStats Integration

        private async Task CreateApiSetFileIfNeeded(ModConfiguration modConfig)
        {
            try
            {
                // Sprawdź czy użytkownik wybrał jakiś serwer SUStats
                if (!SUStatsConfigViewModel.HasSelectedServer)
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ⚠️ Brak wybranego serwera SUStats - pomijam tworzenie ApiSet.ini");
                    return;
                }

                // Pobierz dane wybranego serwera
                var serverData = SUStatsConfigViewModel.GetSelectedServerData();
                if (!serverData.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ❌ Nie udało się pobrać danych wybranego serwera");
                    return;
                }

                var (id, serverName, token, secret, endpoint) = serverData.Value;
                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📊 Tworzenie konfiguracji SUStats dla serwera: {serverName}");

                // Sprawdź czy mod jest zainstalowany
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ❌ Mod nie jest zainstalowany - brak ścieżki instalacji");
                    return;
                }

                if (!Directory.Exists(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Katalog moda nie istnieje: {modConfig.InstallPath}");
                    return;
                }

                // Uwzględnij strukturę Epic (podkatalog AmongUs) - używamy PathSettings.GetActualModPath
                string actualModPath = PathSettings.GetActualModPath(modConfig.InstallPath);
                
                // Stwórz ścieżkę do BepInEx\plugins
                string bepInExPluginsPath = Path.Combine(actualModPath, "BepInEx", "plugins");
                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📁 Ścieżka BepInEx\\plugins: {bepInExPluginsPath}");

                // Sprawdź czy katalog BepInEx\plugins istnieje
                if (!Directory.Exists(bepInExPluginsPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ⚠️ Katalog BepInEx\\plugins nie istnieje: {bepInExPluginsPath}");
                    System.Diagnostics.Debug.WriteLine("[ApiSet] 📁 Tworzenie katalogu BepInEx\\plugins...");

                    try
                    {
                        Directory.CreateDirectory(bepInExPluginsPath);
                        System.Diagnostics.Debug.WriteLine("[ApiSet] ✅ Katalog BepInEx\\plugins utworzony pomyślnie");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Nie udało się utworzyć katalogu BepInEx\\plugins: {ex.Message}");
                        return;
                    }
                }

                // Ścieżka do pliku ApiSet.ini
                string apiSetPath = Path.Combine(bepInExPluginsPath, "ApiSet.ini");
                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📄 Ścieżka pliku ApiSet.ini: {apiSetPath}");

                // Diagnostics output do logowania
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] {message}");
                });

                // Zapisz plik ApiSet.ini - używamy ApiSetManager z Core
                bool success = await Task.Run(() =>
                    ApiSetManager.SaveApiSetFile(
                        apiSetPath,
                        token,
                        endpoint,
                        secret,
                        diagnosticsOutput
                    )
                );

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ✅ Konfiguracja SUStats zapisana pomyślnie");
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] 🎯 Serwer: {serverName}");
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] 📍 Lokalizacja: {apiSetPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Nie udało się zapisać konfiguracji SUStats");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ BŁĄD podczas tworzenia ApiSet.ini: {ex.Message}");
            }
        }

        private async Task RemoveApiSetFileIfExists(ModConfiguration modConfig)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ApiSet] 🗑️ Sprawdzanie czy usunąć plik ApiSet.ini...");

                // Sprawdź czy mod jest zainstalowany
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ⚠️ Mod nie jest zainstalowany - brak ścieżki instalacji");
                    return;
                }

                if (!Directory.Exists(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ⚠️ Katalog moda nie istnieje: {modConfig.InstallPath}");
                    return;
                }

                // Uwzględnij strukturę Epic (podkatalog AmongUs)
                string actualModPath = PathSettings.GetActualModPath(modConfig.InstallPath);
                
                // Stwórz ścieżkę do BepInEx\plugins\ApiSet.ini
                string bepInExPluginsPath = Path.Combine(actualModPath, "BepInEx", "plugins");
                string apiSetPath = Path.Combine(bepInExPluginsPath, "ApiSet.ini");

                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📄 Sprawdzanie pliku: {apiSetPath}");

                // Diagnostics output do logowania
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] {message}");
                });

                // Usuń plik ApiSet.ini jeśli istnieje - używamy ApiSetManager z Core
                bool success = await Task.Run(() =>
                    ApiSetManager.RemoveApiSetFile(
                        apiSetPath,
                        diagnosticsOutput
                    )
                );

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ✅ Operacja usuwania zakończona pomyślnie");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Nie udało się usunąć pliku ApiSet.ini");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ BŁĄD podczas usuwania ApiSet.ini: {ex.Message}");
            }
        }

        private async Task<bool?> HandleSUStatsChoice(ModConfiguration modConfig)
        {
            try
            {
                // Sprawdź czy użytkownik wybrał jakiś serwer SUStats
                if (!SUStatsConfigViewModel.HasSelectedServer)
                {
                    System.Diagnostics.Debug.WriteLine("[SUStats Choice] ⚠️ Brak wybranego serwera SUStats - pomijam dialog");
                    return true; // Kontynuuj bez statystyk
                }

                // Pobierz dane wybranego serwera
                var serverData = SUStatsConfigViewModel.GetSelectedServerData();
                if (!serverData.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine("[SUStats Choice] ❌ Nie udało się pobrać danych serwera");
                    return true; // Kontynuuj bez statystyk
                }

                var (id, serverName, token, secret, endpoint) = serverData.Value;
                System.Diagnostics.Debug.WriteLine($"[SUStats Choice] 🤔 Pokazuję dialog wyboru dla serwera: {serverName}");

                // Pokaż dialog wyboru
                var dialog = new SUStatsConfirmDialog(serverName);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);

                    if (dialog.DialogResult == true)
                    {
                        if (dialog.UseStats)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ✅ Użytkownik wybrał: TAK - uruchom z statystykami");
                            return true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SUStats Choice] 🗑️ Użytkownik wybrał: NIE - skasuj i uruchom");
                            return false;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ❌ Użytkownik anulował uruchamianie");
                        return null; // Anulowano
                    }
                }

                return true; // Fallback - kontynuuj
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ❌ BŁĄD podczas obsługi wyboru: {ex.Message}");
                return true; // W przypadku błędu - kontynuuj bez statystyk
            }
        }

        private static bool IsVanillaModConfiguration(ModConfiguration config) =>
            config.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
            (config.Id == 0 && config.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase));

        private static bool HasAmongUsExeAtInstallPath(string? installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
                return false;

            var actualPath = PathSettings.GetActualModPath(installPath);
            return GameLocator.IsValidAmongUsInstallDirectory(actualPath);
        }

        /// <summary>
        /// Po starcie aplikacji (Steam) monituje o ścieżkę Among Us, gdy auto-wykrywanie się nie powiodło.
        /// </summary>
        public Task PromptAmongUsPathOnStartupIfNeededAsync()
        {
            var mode = _userSettingsService.LoadUserSettings().Mode;
            if (string.IsNullOrEmpty(mode) || mode.Equals("epic", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            return EnsureSteamAmongUsInstallPathAsync();
        }

        private async Task<bool> EnsureSteamAmongUsInstallPathAsync()
        {
            var configService = new ConfigService();
            var configs = configService.LoadConfig();
            var vanilla = configs.FirstOrDefault(IsVanillaModConfiguration);

            if (vanilla != null && HasAmongUsExeAtInstallPath(vanilla.InstallPath))
                return true;

            var discovered = GameLocator.TryFindSteamPath();
            if (discovered != null)
            {
                await RegisterVanillaPathAndRefreshAsync(discovered);
                return true;
            }

            while (true)
            {
                var dialogResult = await ShowAmongUsNotFoundModalAsync();
                if (dialogResult != AmongUsNotFoundResult.Browse)
                    return false;

                var selectedFile = await ShowSelectFileDialogAsync(
                    "Among Us executable|*.exe",
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

                if (string.IsNullOrWhiteSpace(selectedFile))
                    continue;

                if (!File.Exists(selectedFile) ||
                    !string.Equals(Path.GetFileName(selectedFile), AmongUsPathDiscovery.GameExeName, StringComparison.OrdinalIgnoreCase))
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("Dialogs.AmongUsNotFound.InvalidFileMessage"),
                        _localizationService.Get("Dialogs.AmongUsNotFound.InvalidFileTitle"));
                    continue;
                }

                var installDirectory = Path.GetDirectoryName(selectedFile);
                if (string.IsNullOrWhiteSpace(installDirectory) ||
                    !GameLocator.IsValidAmongUsInstallDirectory(installDirectory))
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("Dialogs.AmongUsNotFound.InvalidFileMessage"),
                        _localizationService.Get("Dialogs.AmongUsNotFound.InvalidFileTitle"));
                    continue;
                }

                await RegisterVanillaPathAndRefreshAsync(installDirectory);
                return true;
            }
        }

        private async Task RegisterVanillaPathAndRefreshAsync(string installDirectory)
        {
            var configService = new ConfigService();
            _loadedConfigs = configService.LoadConfig();
            GameLocator.TryRegisterSteamVanillaPath(_loadedConfigs, _configuration!, installDirectory);
            await RefreshModsListAsync(preloadedConfigs: _loadedConfigs);
        }

        private async Task<AmongUsNotFoundResult> ShowAmongUsNotFoundModalAsync()
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return await Dispatcher.UIThread.InvokeAsync(ShowAmongUsNotFoundModalAsync);

            var completionSource = new TaskCompletionSource<AmongUsNotFoundResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _amongUsNotFoundCompletionSource = completionSource;

            var vm = new AmongUsNotFoundViewModel(_localizationService);
            vm.CloseRequested += OnAmongUsNotFoundCloseRequested;
            AmongUsNotFoundViewModel = vm;
            IsAmongUsNotFoundVisible = true;

            return await completionSource.Task;
        }

        private void OnAmongUsNotFoundCloseRequested(object? sender, EventArgs e)
        {
            if (sender is AmongUsNotFoundViewModel vm)
                DismissAmongUsNotFoundModal(vm.Result);
        }

        private void DismissAmongUsNotFoundModal(AmongUsNotFoundResult result)
        {
            if (AmongUsNotFoundViewModel != null)
            {
                AmongUsNotFoundViewModel.CloseRequested -= OnAmongUsNotFoundCloseRequested;
                AmongUsNotFoundViewModel = null;
            }

            if (!IsAmongUsNotFoundVisible && _amongUsNotFoundCompletionSource == null)
                return;

            IsAmongUsNotFoundVisible = false;
            _amongUsNotFoundCompletionSource?.TrySetResult(result);
            _amongUsNotFoundCompletionSource = null;
        }

        private void ClearSUStatsSelection()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[SUStats Choice] 🧹 Czyszczenie wyboru serwera SUStats...");

                // Wyczyść globalny wybór
                SUStatsConfigViewModel.ClearGlobalSelection();

                System.Diagnostics.Debug.WriteLine("[SUStats Choice] ✅ Wybór serwera SUStats wyczyszczony");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ❌ BŁĄD podczas czyszczenia wyboru: {ex.Message}");
            }
        }

        #endregion
    }
}

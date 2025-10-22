using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;
using SUSModder.Core.Models;
using SUSModder.Services;
using SUSModder.Views;
using SUSModder.ViewModels.Helpers;
using System.IO;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający operacje na modach: Install, Update, Uninstall
    /// </summary>
    public partial class MainWindowViewModel
    {
        #region Install

        private async void Install()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            var currentSelectedMod = SelectedMod;

            // Zwiększ licznik aktywnych instalacji
            lock (_installationLock)
            {
                _activeInstallationsCount++;
                System.Diagnostics.Debug.WriteLine($"[Install] Rozpoczęto instalację {currentSelectedMod.Name}. Aktywnych instalacji: {_activeInstallationsCount}");
            }

            try
            {
                // Ustaw flagę instalacji
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.IsInstalling = true;
                    currentSelectedMod.ShowProgress = true;
                });

                // Pobierz konfigurację moda
                var configService = new ConfigService();
                var allConfigs = configService.LoadConfig();
                var modConfig = allConfigs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                if (modConfig == null)
                {
                    await _userInteractionService.ShowErrorAsync("Nie znaleziono konfiguracji moda.", "Błąd");
                    return;
                }

                string platform = DeterminePlatform();
                bool success = false;

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    success = await InstallEpicModAsync(currentSelectedMod, modConfig);
                }
                else
                {
                    success = await InstallSteamModAsync(currentSelectedMod, modConfig, allConfigs);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Install] Exception: {ex.Message}");
            }
            finally
            {
                // Ukryj progress bar
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.ShowProgress = false;
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;
                    currentSelectedMod.IsInstalling = false;
                });

                // Zmniejsz licznik aktywnych instalacji
                lock (_installationLock)
                {
                    _activeInstallationsCount--;
                    System.Diagnostics.Debug.WriteLine($"[Install] Zakończono instalację {currentSelectedMod.Name}. Aktywnych instalacji: {_activeInstallationsCount}");
                }

                // Jeśli to była ostatnia instalacja, pokaż wszystkie oczekujące dialogi DLL
                await ShowPendingDllDialogsIfNeeded();

                // Odśwież statystyki status bara
                await RefreshStatusBarAsync();
            }
        }

        /// <summary>
        /// Instalacja moda z wyborem wersji - pokazuje dialog wyboru wersji przed instalacją
        /// </summary>
        private async Task InstallWithVersionSelection()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            try
            {
                // Pobierz konfigurację moda
                var configService = new ConfigService();
                var allConfigs = configService.LoadConfig();
                var modConfig = allConfigs.FirstOrDefault(c => c.ModName == SelectedMod.Name);

                if (modConfig == null)
                {
                    await _userInteractionService.ShowErrorAsync("Nie znaleziono konfiguracji moda.", "Błąd");
                    return;
                }

                // Pobierz konfigurację aplikacji
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                // Utwórz ViewModel dialogu
                var versionSelectionVM = new VersionSelectionViewModel(modConfig, configuration);

                // Pokaż dialog
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow == null)
                    return;

                var dialog = new VersionSelectionDialog
                {
                    DataContext = versionSelectionVM
                };

                var selectedVersion = await dialog.ShowDialog<ModVersionHistory?>(mainWindow);

                if (selectedVersion == null)
                {
                    System.Diagnostics.Debug.WriteLine("[InstallWithVersionSelection] Użytkownik anulował wybór wersji");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[InstallWithVersionSelection] Wybrano wersję: {selectedVersion.ModVersion}");

                // Zainstaluj wybraną wersję
                await InstallSpecificVersionAsync(SelectedMod, modConfig, selectedVersion, allConfigs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InstallWithVersionSelection] Błąd: {ex.Message}");
                await _userInteractionService.ShowErrorAsync($"Błąd podczas instalacji: {ex.Message}", "Błąd");
            }
        }

        /// <summary>
        /// Instaluje konkretną wersję moda
        /// </summary>
        private async Task InstallSpecificVersionAsync(
            ModItem modItem,
            ModConfiguration modConfig,
            ModVersionHistory selectedVersion,
            List<ModConfiguration> allConfigs)
        {
            // Zwiększ licznik aktywnych instalacji
            lock (_installationLock)
            {
                _activeInstallationsCount++;
                System.Diagnostics.Debug.WriteLine($"[InstallSpecificVersion] Rozpoczęto instalację {modItem.Name} v{selectedVersion.ModVersion}. Aktywnych instalacji: {_activeInstallationsCount}");
            }

            try
            {
                // Ustaw flagę instalacji
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.IsInstalling = true;
                    modItem.ShowProgress = true;
                });

                // Nadpisz wersję w konfiguracji moda
                var tempModConfig = new ModConfiguration
                {
                    Id = modConfig.Id,
                    ModName = modConfig.ModName,
                    ModType = modConfig.ModType,
                    ModVersion = selectedVersion.ModVersion,
                    AmongVersion = selectedVersion.AmongVersion,
                    GitHubRepoOrLink = selectedVersion.GitHubRepoOrLink ?? modConfig.GitHubRepoOrLink,
                    EpicGitHubRepoOrLink = selectedVersion.EpicGitHubRepoOrLink ?? modConfig.EpicGitHubRepoOrLink,
                    Description = modConfig.Description,
                    PngFileName = modConfig.PngFileName,
                    DllInstallPath = modConfig.DllInstallPath
                };

                string platform = DeterminePlatform();
                bool success = false;

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    success = await InstallEpicModAsync(modItem, tempModConfig);
                }
                else
                {
                    success = await InstallSteamModAsync(modItem, tempModConfig, allConfigs);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InstallSpecificVersion] Exception: {ex.Message}");
                await _userInteractionService.ShowErrorAsync($"Błąd podczas instalacji: {ex.Message}", "Błąd");
            }
            finally
            {
                // Ukryj progress bar
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.ShowProgress = false;
                    modItem.InstallProgress = 0;
                    modItem.InstallStatusMessage = string.Empty;
                    modItem.IsInstalling = false;
                });

                // Zmniejsz licznik aktywnych instalacji
                lock (_installationLock)
                {
                    _activeInstallationsCount--;
                    System.Diagnostics.Debug.WriteLine($"[InstallSpecificVersion] Zakończono instalację {modItem.Name}. Aktywnych instalacji: {_activeInstallationsCount}");
                }

                // Jeśli to była ostatnia instalacja, pokaż wszystkie oczekujące dialogi DLL
                await ShowPendingDllDialogsIfNeeded();

                // Odśwież statystyki status bara
                await RefreshStatusBarAsync();
            }
        }

        private async Task<bool> InstallEpicModAsync(ModItem currentSelectedMod, ModConfiguration modConfig)
        {
            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Install Epic] {message}");
            });

            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

            // Wyczyść log przed instalacją
            epicManager.ClearLegendaryLog();

            // Subskrybuj progress
            epicManager.ProgressChanged += (percentage, message) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.InstallProgress = percentage;
                    currentSelectedMod.InstallStatusMessage = message;
                    System.Diagnostics.Debug.WriteLine($"[Epic Progress] {percentage}% - {message}");
                });
            };

            // Subskrybuj zakończenie instalacji
            epicManager.InstallationCompleted += (completedModConfig) =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: InstallationCompleted callback for: {completedModConfig.ModName}");
                    await RefreshModsListAsync();
                    SelectedMod = Mods.FirstOrDefault(m => m.Name == completedModConfig.ModName);
                });
            };

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Calling ModifyEpicAsync for {modConfig.ModName}");
                await epicManager.ModifyEpicAsync(modConfig, null, null);
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync returned successfully");

                ShowDllSelectionWindow(currentSelectedMod, "epic");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync threw exception: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> InstallSteamModAsync(ModItem currentSelectedMod, ModConfiguration modConfig, System.Collections.Generic.List<ModConfiguration> allConfigs)
        {
            var progressReporter = new UIProgressReporter((percentage, message) =>
            {
                currentSelectedMod.InstallProgress = percentage;
                currentSelectedMod.InstallStatusMessage = message;
            });

            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Install Steam] {message}");
            });

            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            var configuration = configBuilder.Build();

            var modManager = new ModManager(configuration);

            try
            {
                var callbacks = new ModManagerUserCallbacks
                {
                    ConfirmAsync = _userInteractionService.ShowConfirmAsync,
                    ShowErrorAsync = _userInteractionService.ShowErrorAsync,
                    ShowInfoAsync = _userInteractionService.ShowInfoAsync
                };

                await modManager.ModifyAsync(
                    modConfig,
                    allConfigs,
                    progressReporter,
                    diagnosticsOutput,
                    callbacks,
                    "steam"
                );

                // Aktualizuj ścieżkę instalacji w UI
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.InstallPath = modConfig.InstallPath;
                });

                RefreshModsSortingKeepSelection(currentSelectedMod);
                ShowDllSelectionWindow(currentSelectedMod, "steam");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Install Steam] Exception: {ex.Message}");
                return false;
            }
        }

        private void ShowDllSelectionWindow(ModItem mod, string platform)
        {
            lock (_installationLock)
            {
                if (_activeInstallationsCount > 0)
                {
                    // Jeśli są aktywne instalacje, dodaj dialog do kolejki
                    _pendingDllDialogs.Add((mod, platform));
                    System.Diagnostics.Debug.WriteLine($"[DLL Dialog] Dodano do kolejki: {mod.Name} ({platform}). Oczekujących dialogów: {_pendingDllDialogs.Count}");
                    return;
                }
            }

            // Jeśli nie ma aktywnych instalacji, pokaż dialog od razu
            ShowDllSelectionWindowInternal(mod, platform);
        }

        private void ShowDllSelectionWindowInternal(ModItem mod, string platform)
        {
            System.Diagnostics.Debug.WriteLine($"[DLL Dialog] Pokazywanie okna dla: {mod.Name} ({platform})");
            
            var dllSelectionWindow = new Window
            {
                Title = $"Dodatkowe modyfikacje DLL dla {mod.Name}",
                Width = 650,
                Height = 600,
                Content = new DllModSelectionView
                {
                    DataContext = new DllModSelectionViewModel(
                        _dllModificationService,
                        ModItemAdapter.ToConfig(mod),
                        platform
                    )
                }
            };
            System.Diagnostics.Debug.WriteLine($"DEBUG {platform} Path: {mod.InstallPath}");
            dllSelectionWindow.Show();
        }

        private async Task ShowPendingDllDialogsIfNeeded()
        {
            List<(ModItem mod, string platform)> dialogsToShow;
            
            lock (_installationLock)
            {
                // Sprawdź czy są jeszcze aktywne instalacje
                if (_activeInstallationsCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[DLL Dialog] Wciąż są aktywne instalacje ({_activeInstallationsCount}), czekam...");
                    return;
                }

                // Pobierz wszystkie oczekujące dialogi
                dialogsToShow = new List<(ModItem, string)>(_pendingDllDialogs);
                _pendingDllDialogs.Clear();
            }

            // Pokaż wszystkie oczekujące dialogi
            if (dialogsToShow.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DLL Dialog] Pokazywanie {dialogsToShow.Count} oczekujących dialogów...");
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var (mod, platform) in dialogsToShow)
                    {
                        ShowDllSelectionWindowInternal(mod, platform);
                        
                        // Małe opóźnienie między dialogami, aby się nie nałożyły wizualnie
                        Task.Delay(100).Wait();
                    }
                });
            }
        }

        #endregion

        #region Update

        private async void Update()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            var currentSelectedMod = SelectedMod;
            bool showNoUpdateMessage = false;
            bool updateSuccessful = false;
            string? successMessage = null;

            try
            {
                // Pokaż progress bar
                currentSelectedMod.ShowProgress = true;
                currentSelectedMod.IsInstalling = true;
                currentSelectedMod.InstallStatusMessage = "Sprawdzanie aktualizacji...";
                currentSelectedMod.InstallProgress = 10;

                // 1. Sprawdź czy jest dostępna aktualizacja
                var configService = new ConfigService();
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(currentSelectedMod.Name);

                if (updatedModConfig == null)
                {
                    currentSelectedMod.InstallStatusMessage = "Brak dostępnych aktualizacji";
                    currentSelectedMod.InstallProgress = 100;
                    await Task.Delay(1500);
                    showNoUpdateMessage = true;
                    return;
                }

                // 2. Rozpocznij aktualizację bez potwierdzenia (ręczna akcja użytkownika)
                currentSelectedMod.InstallProgress = 20;

                // 3. Aktualizuj konfigurację w pliku
                currentSelectedMod.InstallStatusMessage = "Aktualizowanie konfiguracji...";
                bool configUpdated = await configService.UpdateSingleModConfigAsync(updatedModConfig);

                if (!configUpdated)
                {
                    await ShowErrorDialogAsync("Nie udało się zaktualizować konfiguracji moda.", "Błąd aktualizacji");
                    return;
                }

                currentSelectedMod.InstallProgress = 30;

                // 4. Przeładuj konfigurację i zaktualizuj UI
                currentSelectedMod.InstallStatusMessage = "Przeładowywanie konfiguracji...";
                await Task.Run(() =>
                {
                    var configs = configService.LoadConfig();
                    _loadedConfigs = configs;
                });

                // Zaktualizuj właściwości ModItem z nową konfiguracją
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.ModVersion = updatedModConfig.ModVersion;
                    currentSelectedMod.AmongVersion = updatedModConfig.AmongVersion;
                    currentSelectedMod.Description = updatedModConfig.Description;
                    currentSelectedMod.GitHubRepoOrLink = updatedModConfig.GitHubRepoOrLink;
                    currentSelectedMod.EpicGitHubRepoOrLink = updatedModConfig.EpicGitHubRepoOrLink;
                });

                currentSelectedMod.InstallProgress = 40;

                // 5. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(currentSelectedMod.InstallPath))
                {
                    await ReinstallModAsync(currentSelectedMod, configService, updatedModConfig);
                }

                // 6. Finalizacja
                currentSelectedMod.InstallProgress = 100;
                currentSelectedMod.InstallStatusMessage = "Aktualizacja zakończona";

                // Odśwież sortowanie zachowując zaznaczenie
                RefreshModsSortingKeepSelection(currentSelectedMod);

                await Task.Delay(1500);
                updateSuccessful = true;
                successMessage = $"Mod '{currentSelectedMod.Name}' został pomyślnie zaktualizowany do wersji {updatedModConfig.ModVersion}.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Exception: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas aktualizacji: {ex.Message}", "Błąd aktualizacji");
            }
            finally
            {
                // Ukryj progress bar
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;

                // Odśwież statystyki status bara
                await RefreshStatusBarAsync();
                
                // Odśwież natychmiastowo status dostępnych aktualizacji w status barze
                await CheckForModUpdatesForStatusBarAsync();
            }

            // Pokaż komunikaty po zakończeniu finally (poza blokiem try-finally)
            if (showNoUpdateMessage)
            {
                await ShowMessageAsync("Informacja", $"Mod '{currentSelectedMod.Name}' jest już w najnowszej wersji.");
            }
            else if (updateSuccessful && successMessage != null)
            {
                await ShowMessageAsync("Sukces", successMessage);
            }
        }

        private async Task ReinstallModAsync(ModItem currentSelectedMod, ConfigService configService, ModConfiguration updatedModConfig)
        {
            // UNINSTALL
            currentSelectedMod.InstallStatusMessage = "Odinstalowywanie starej wersji...";

            if (Directory.Exists(currentSelectedMod.InstallPath))
            {
                Directory.Delete(currentSelectedMod.InstallPath, true);
                System.Diagnostics.Debug.WriteLine($"Usunięto katalog: {currentSelectedMod.InstallPath}");
            }

            // Aktualizuj konfigurację - usuń ścieżkę instalacji
            var configs = configService.LoadConfig();
            var modConfig = configs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);
            if (modConfig != null)
            {
                modConfig.InstallPath = string.Empty;
                ConfigManager.SaveConfig(configs);
            }

            currentSelectedMod.InstallPath = string.Empty;
            currentSelectedMod.InstallProgress = 60;

            // INSTALL
            currentSelectedMod.InstallStatusMessage = "Instalowanie nowej wersji...";

            // Pobierz zaktualizowaną konfigurację
            var updatedConfigs = configService.LoadConfig();
            var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

            if (updatedConfig != null)
            {
                // Progress reporter
                var progressReporter = new UIProgressReporter((percentage, message) =>
                {
                    // Mapuj progress 60-100% dla install
                    currentSelectedMod.InstallProgress = 60 + (percentage * 40 / 100);
                    currentSelectedMod.InstallStatusMessage = $"Instalowanie: {message}";
                });

                // Diagnostics output
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Update-Install] {message}");
                });

                // Silent user interaction
                var silentUserInteraction = new InstallationSilentUserInteraction();

                // Sprawdź platformę
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                string platform = configuration.GetSection("Configuration")["Mode"] ?? "steam";

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    // Epic installation
                    var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                    var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                    await epicManager.ModifyEpicAsync(updatedConfig, null, null);
                }
                else
                {
                    // Steam installation
                    var modManager = new ModManager(configuration);
                    var callbacks = new ModManagerUserCallbacks
                    {
                        ConfirmAsync = silentUserInteraction.ShowConfirmAsync,
                        ShowErrorAsync = silentUserInteraction.ShowErrorAsync,
                        ShowInfoAsync = silentUserInteraction.ShowInfoAsync
                    };

                    await modManager.ModifyAsync(
                        updatedConfig,
                        updatedConfigs,
                        progressReporter,
                        diagnosticsOutput,
                        callbacks,
                        "steam"
                    );
                }

                // Aktualizuj ścieżkę instalacji w UI
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.InstallPath = updatedConfig.InstallPath;
                });
            }
        }

        #endregion

        #region Uninstall

        private async void Uninstall()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            var currentSelectedMod = SelectedMod;

            try
            {
                if (string.IsNullOrEmpty(currentSelectedMod.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine("Mod nie jest zainstalowany.");
                    return;
                }

                // Pokaż progress bar
                currentSelectedMod.ShowProgress = true;
                currentSelectedMod.IsInstalling = true;
                currentSelectedMod.InstallProgress = 25;
                currentSelectedMod.InstallStatusMessage = "Rozpoczynanie odinstalowywania...";

                // Pokaż ładny dialog potwierdzenia usunięcia
                bool confirmed = false;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dialog = new UninstallConfirmDialog(currentSelectedMod.Name, currentSelectedMod.InstallPath);
                    
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop 
                        && desktop.MainWindow != null)
                    {
                        await dialog.ShowDialog(desktop.MainWindow);
                        confirmed = dialog.Result;
                    }
                });

                if (!confirmed)
                {
                    currentSelectedMod.ShowProgress = false;
                    currentSelectedMod.IsInstalling = false;
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;
                    return;
                }

                currentSelectedMod.InstallProgress = 25;
                currentSelectedMod.InstallStatusMessage = "Usuwanie plików...";

                // Użyj nowego FileSystemUtilities z Core
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Uninstall] {message}");
                });

                bool deleteSuccess = await FileSystemUtilities.SafeDeleteDirectoryAsync(
                    currentSelectedMod.InstallPath,
                    currentSelectedMod.Name,
                    diagnosticsOutput,
                    ShowConfirmDialogAsync
                );

                if (!deleteSuccess)
                {
                    await ShowErrorDialogAsync(
                        $"Nie udało się całkowicie usunąć katalogu moda '{currentSelectedMod.Name}'.\n\n" +
                        $"Katalog: {currentSelectedMod.InstallPath}\n\n" +
                        "Niektóre pliki mogą nadal istnieć. Spróbuj usunąć je ręcznie lub zrestartować komputer i spróbować ponownie.",
                        "Ostrzeżenie - Niepełne usunięcie"
                    );

                    // Mimo niepełnego usunięcia, kontynuuj proces odinstalowania
                    System.Diagnostics.Debug.WriteLine($"OSTRZEŻENIE: Niepełne usunięcie katalogu: {currentSelectedMod.InstallPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Pomyślnie usunięto katalog: {currentSelectedMod.InstallPath}");
                }

                currentSelectedMod.InstallProgress = 75;
                currentSelectedMod.InstallStatusMessage = "Aktualizowanie konfiguracji...";

                // Aktualizuj konfigurację
                var configService = new ConfigService();
                var configs = configService.LoadConfig();
                var modConfig = configs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                if (modConfig != null)
                {
                    modConfig.InstallPath = string.Empty;
                    ConfigManager.SaveConfig(configs);
                }

                // Aktualizuj UI
                currentSelectedMod.InstallPath = string.Empty;
                currentSelectedMod.InstallProgress = 100;
                currentSelectedMod.InstallStatusMessage = "Odinstalowanie zakończone";

                // Odśwież sortowanie bez utraty zaznaczenia
                RefreshModsSortingKeepSelection(currentSelectedMod);

                System.Diagnostics.Debug.WriteLine($"[Uninstall] SUCCESS: Odinstalowanie moda '{currentSelectedMod.Name}' zakończone pomyślnie");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Uninstall] Exception: {ex.Message}");
                currentSelectedMod.InstallStatusMessage = $"Błąd: {ex.Message}";
            }
            finally
            {
                // Ukryj progress bar
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;

                // Odśwież statystyki status bara
                await RefreshStatusBarAsync();
            }
        }

        #endregion
    }
}

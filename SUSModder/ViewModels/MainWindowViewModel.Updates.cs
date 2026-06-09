using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający logikę sprawdzania i przetwarzania aktualizacji modów
    /// </summary>
    public partial class MainWindowViewModel
    {
        private DateTime _lastInteractiveModUpdateCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan InteractiveModUpdateCheckCooldown = TimeSpan.FromMinutes(2);

        private bool ShouldRunInteractiveModUpdateCheck()
            => DateTime.UtcNow - _lastInteractiveModUpdateCheckUtc > InteractiveModUpdateCheckCooldown;

        private async Task CheckForModUpdatesAsync()
        {
            try
            {
                _lastInteractiveModUpdateCheckUtc = DateTime.UtcNow;
                var updateManager = new ModUpdateManager();
                var result = await updateManager.CheckForUpdatesAsync();

                if (!result.Success)
                {
                    await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji: {result.ErrorMessage}", "Błąd");
                    return;
                }

                // Jeśli config został zaktualizowany, odśwież listę modów
                if (result.ConfigWasUpdated)
                {
                    await RefreshModsListAsync(deferIfToolModalOpen: true);
                }

                // Pokaż dialogi aktualizacji dla każdego moda z dostępną aktualizacją
                if (result.InstalledModUpdates.Any())
                {
                    // Powiadomienie toast o znalezionych aktualizacjach
                    ToastService.ShowInfo(
                        _localizationService.GetFormatted("Toast.ModUpdatesFound", result.InstalledModUpdates.Count));

                    await ProcessUpdatesWithIndividualDialogsAsync(result.InstalledModUpdates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during update check: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji: {ex.Message}", "Błąd");
            }
        }

        /// <summary>
        /// Automatycznie aktualizuje mody z włączoną auto-aktualizacją — w tle, bez dialogów.
        /// Wywoływane z okresowego odświeżania (timer co 5 minut) oraz po sprawdzeniu ręcznym.
        /// Ładuje AutoUpdateEnabled bezpośrednio z Installation Map, aby uniknąć
        /// problemów z timingiem (flaga jest ustawiana asynchronicznie w tle podczas RefreshModsListAsync).
        /// </summary>
        private async Task ProcessAutoUpdatesSilentlyAsync(List<ModUpdateInfo> availableUpdates)
        {
            try
            {
                int updatedCount = 0;

                foreach (var modUpdate in availableUpdates)
                {
                    try
                    {
                        // Ładujemy AutoUpdateEnabled bezpośrednio z Installation Map,
                        // aby uniknąć race condition z fire-and-forget taskiem w RefreshModsListAsync
                        bool isAutoUpdateEnabled = await IsModAutoUpdateEnabledAsync(modUpdate.ModName, modUpdate.LocalMod?.InstallPath);

                        if (!isAutoUpdateEnabled)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoUpdate] {modUpdate.ModName}: auto-aktualizacja wyłączona - pomijam");
                            continue;
                        }

                        // Znajdź ModItem dla aktualizacji (może być null jeśli nie załadowany)
                        var modItem = Mods.FirstOrDefault(m => m.Name == modUpdate.ModName);
                        if (modItem == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoUpdate] {modUpdate.ModName}: ModItem nie znaleziony w kolekcji - pomijam");
                            continue;
                        }

                        // Cicha aktualizacja — bez żadnych dialogów
                        System.Diagnostics.Debug.WriteLine($"[AutoUpdate] {modUpdate.ModName}: rozpoczynam cichą aktualizację ({modUpdate.CurrentVersion} → {modUpdate.NewVersion})");

                        // Toast informacyjny na start (użytkownik widzi co się dzieje)
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ToastService.ShowInfo(
                                _localizationService.GetFormatted("Toast.ModUpdateStarted", modUpdate.ModName));
                        });

                        bool success = await UpdateSingleModWithDialogAsync(modItem, modUpdate, progressDialog: null);

                        if (success)
                        {
                            updatedCount++;

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ToastService.ShowInfo(
                                    _localizationService.GetFormatted("Toast.ModUpdated", modUpdate.ModName, modUpdate.NewVersion));
                            });

                            System.Diagnostics.Debug.WriteLine($"[AutoUpdate] {modUpdate.ModName}: aktualizacja zakończona sukcesem");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoUpdate] {modUpdate.ModName}: aktualizacja nie powiodła się");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoUpdate] Błąd auto-aktualizacji {modUpdate.ModName}: {ex.Message}");
                    }
                }

                if (updatedCount > 0)
                {
                    // Odśwież listę i status (cicho — bez skeletonu i bez zamykania paneli)
                    await RefreshModsListAsync(deferIfToolModalOpen: true);
                    AvailableUpdatesCount = 0;
                    await CheckForModUpdatesForStatusBarAsync(force: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoUpdate] Błąd w ProcessAutoUpdatesSilentlyAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Sprawdza czy mod ma włączoną auto-aktualizację, ładując ustawienie
        /// bezpośrednio z Installation Map (omija asynchroniczne ładowanie w RefreshModsListAsync).
        /// </summary>
        private async Task<bool> IsModAutoUpdateEnabledAsync(string modName, string? installPath)
        {
            // 1. Najpierw sprawdź w Mods (szybka ścieżka jeśli już załadowane)
            var modItem = Mods.FirstOrDefault(m => m.Name == modName);
            if (modItem != null)
                return modItem.AutoUpdateEnabled;

            // 2. Jeśli nie ma w Mods, spróbuj załadować z Installation Map
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                try
                {
                    var installMap = await InstallationMapManager.LoadInstallationMapAsync(installPath);
                    if (installMap?.FullMod != null)
                        return installMap.FullMod.AutoUpdateEnabled;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoUpdate] Błąd ładowania Installation Map dla {modName}: {ex.Message}");
                }
            }

            return false;
        }

        private async Task<bool> ShowUpdateModConfirmDialogAsync(ModUpdateInfo updateInfo)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new UpdateModConfirmDialog(updateInfo);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                    return dialog.Result;
                }

                return false;
            });
        }

        private async Task ProcessUpdatesWithIndividualDialogsAsync(List<ModUpdateInfo> availableUpdates)
        {
            var successfulUpdates = new List<string>();
            var failedUpdates = new List<string>();
            var skippedUpdates = new List<string>();
            var autoUpdatedMods = new List<string>();

            foreach (var modUpdate in availableUpdates)
            {
                try
                {
                    // Stwórz lub znajdź ModItem (potrzebny do sprawdzenia auto-update)
                    var modItem = await GetOrCreateModItemAsync(modUpdate);

                    // Sprawdź czy auto-aktualizacja jest włączona (skip dialog potwierdzenia)
                    bool isAutoUpdate = modItem.AutoUpdateEnabled;

                    if (!isAutoUpdate)
                    {
                        // Pokaż dialog potwierdzenia tylko gdy auto-update jest wyłączone
                        bool confirmed = await ShowUpdateModConfirmDialogAsync(modUpdate);

                        if (!confirmed)
                        {
                            skippedUpdates.Add(modUpdate.ModName);
                            continue;
                        }
                    }

                    if (isAutoUpdate)
                    {
                        // AUTO-UPDATE: cicha aktualizacja w tle (bez dialogów)
                        bool success = await UpdateSingleModWithDialogAsync(modItem, modUpdate, progressDialog: null);

                        if (success)
                        {
                            autoUpdatedMods.Add(modUpdate.ModName);

                            // Toast z informacją o zaktualizowanym modzie
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ToastService.ShowInfo(
                                    _localizationService.GetFormatted("Toast.ModUpdated", modUpdate.ModName, modUpdate.NewVersion));
                            });
                        }
                        else
                        {
                            failedUpdates.Add(modUpdate.ModName);
                        }
                    }
                    else
                    {
                        // INTERAKTYWNA AKTUALIZACJA: pokaż dialog postępu
                        UpdateProgressDialog? progressDialog = null;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            progressDialog = new UpdateProgressDialog(modUpdate.ModName);
                            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                            if (mainWindow != null)
                            {
                                progressDialog.Show(mainWindow);
                            }
                        });

                        // Wykonaj aktualizację z przekazaniem dialogu do aktualizacji postępu
                        bool success = await UpdateSingleModWithDialogAsync(modItem, modUpdate, progressDialog);

                        // Zamknij dialog postępu
                        if (progressDialog != null && progressDialog.IsVisible)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressDialog.Close();
                            });
                        }

                        if (success)
                        {
                            successfulUpdates.Add($"{modUpdate.ModName} ({modUpdate.CurrentVersion} → {modUpdate.NewVersion})");
                        }
                        else
                        {
                            failedUpdates.Add(modUpdate.ModName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating {modUpdate.ModName}: {ex.Message}");
                    failedUpdates.Add(modUpdate.ModName);
                }
            }

            // Odśwież listę modów
            await RefreshModsListAsync();

            // Natychmiast wyzeruj licznik dostępnych aktualizacji po udanej aktualizacji
            AvailableUpdatesCount = 0;
            await CheckForModUpdatesForStatusBarAsync(force: true);

            // Pokaż podsumowanie dla interaktywnych aktualizacji
            if (successfulUpdates.Any() || failedUpdates.Any() || skippedUpdates.Any())
            {
                await ShowUpdateSummaryAsync(successfulUpdates, failedUpdates, skippedUpdates);
            }

            // Toast podsumowujący auto-aktualizacje
            if (autoUpdatedMods.Any())
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ToastService.ShowInfo(
                        _localizationService.GetFormatted("Toast.ModUpdatesAutoUpdated", autoUpdatedMods.Count));
                });
            }
        }

        private async Task ShowUpdateSummaryAsync(List<string> successful, List<string> failed, List<string> skipped)
        {
            if (successful.Count == 0 && failed.Count == 0 && skipped.Count == 0)
                return;

            var messageBuilder = new System.Text.StringBuilder();
            messageBuilder.AppendLine("🎉 Podsumowanie aktualizacji\n");

            if (successful.Any())
            {
                messageBuilder.AppendLine($"✅ Pomyślnie zaktualizowano ({successful.Count}):");
                foreach (var update in successful)
                {
                    messageBuilder.AppendLine($"   • {update}");
                }
                messageBuilder.AppendLine();
            }

            if (failed.Any())
            {
                messageBuilder.AppendLine($"❌ Nie udało się zaktualizować ({failed.Count}):");
                foreach (var failure in failed)
                {
                    messageBuilder.AppendLine($"   • {failure}");
                }
                messageBuilder.AppendLine();
            }

            if (skipped.Any())
            {
                messageBuilder.AppendLine($"⏭️ Pominięto ({skipped.Count}):");
                foreach (var skipped_mod in skipped)
                {
                    messageBuilder.AppendLine($"   • {skipped_mod}");
                }
            }

            await ShowMessageAsync("Aktualizacja zakończona", messageBuilder.ToString());
        }

        private async Task<ModItem> GetOrCreateModItemAsync(ModUpdateInfo modUpdate)
        {
            var existingModItem = Mods.FirstOrDefault(m => m.Name == modUpdate.ModName);

            if (existingModItem == null)
            {
                var modItem = new ModItem
                {
                    Id = modUpdate.LocalMod?.Id ?? 0,
                    Name = modUpdate.LocalMod?.ModName ?? modUpdate.ModName,
                    ModVersion = modUpdate.LocalMod?.ModVersion ?? modUpdate.CurrentVersion,
                    InstallPath = modUpdate.LocalMod?.InstallPath ?? "",
                    Description = modUpdate.LocalMod?.Description ?? "",
                };

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Mods.Add(modItem);
                });

                return modItem;
            }

            return existingModItem;
        }

        private async Task<bool> UpdateSingleModWithDialogAsync(ModItem modItem, ModUpdateInfo updateInfo, UpdateProgressDialog? progressDialog)
        {
            _activeInstallationsCount++;
            SyncIsAnyModInstalling();
            try
            {
                // Pokaż postęp na karcie moda (dla auto-aktualizacji bez dialogu)
                if (progressDialog == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        modItem.IsInstalling = true;
                        modItem.ShowProgress = true;
                        modItem.InstallStatusMessage = "Pobieranie nowej konfiguracji...";
                        modItem.InstallProgress = 5;
                    });
                }

                var configService = new ConfigService();

                // 1. Pobierz zaktualizowaną konfigurację
                UpdateModProgress(modItem, progressDialog, 10, "Pobieranie nowej konfiguracji...");
                
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(modItem.Name);
                if (updatedModConfig == null) return false;

                // 2. Aktualizuj konfigurację w pliku
                UpdateModProgress(modItem, progressDialog, 20, "Aktualizowanie konfiguracji...");
                await configService.UpdateSingleModConfigAsync(updatedModConfig);

                // 3. Zaktualizuj właściwości ModItem
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.ModVersion = updatedModConfig.ModVersion;
                    modItem.AmongVersion = updatedModConfig.AmongVersion;
                    modItem.Description = updatedModConfig.Description;
                    modItem.GitHubRepoOrLink = updatedModConfig.GitHubRepoOrLink;
                    modItem.EpicGitHubRepoOrLink = updatedModConfig.EpicGitHubRepoOrLink;
                });

                UpdateModProgress(modItem, progressDialog, 30, "Przygotowywanie do reinstalacji...");

                // 4. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(modItem.InstallPath))
                {
                    // KROK 0: Zapisz listę zainstalowanych DLL PRZED usunięciem
                    List<DllModInstallation> installedDlls = new List<DllModInstallation>();
                    bool oldAutoUpdateEnabled = false;

                    try
                    {
                        var installMap = await InstallationMapManager.LoadInstallationMapAsync(modItem.InstallPath);
                        if (installMap?.InstalledDlls != null && installMap.InstalledDlls.Any())
                        {
                            installedDlls = new List<DllModInstallation>(installMap.InstalledDlls);
                            System.Diagnostics.Debug.WriteLine($"[Update] Zachowuję {installedDlls.Count} zainstalowanych DLL: {string.Join(", ", installedDlls.Select(d => d.ModName))}");
                        }

                        // Zachowaj ustawienie auto-aktualizacji PRZED usunięciem
                        if (installMap?.FullMod != null)
                        {
                            oldAutoUpdateEnabled = installMap.FullMod.AutoUpdateEnabled;
                            System.Diagnostics.Debug.WriteLine($"[Update] Zachowuję AutoUpdateEnabled={oldAutoUpdateEnabled} dla {modItem.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Update] Nie udało się odczytać Installation Map: {ex.Message}");
                    }

                    // UNINSTALL
                    UpdateModProgress(modItem, progressDialog, 40, "Odinstalowywanie starej wersji...");

                    if (Directory.Exists(modItem.InstallPath))
                    {
                        bool deleteSuccess = await SafeDeleteDirectoryAsync(modItem.InstallPath, modItem.Name);
                        if (!deleteSuccess)
                        {
                            throw new InvalidOperationException($"Nie udało się usunąć starej wersji moda '{modItem.Name}'. Aktualizacja została przerwana.");
                        }
                    }

                    // Aktualizuj konfigurację - usuń ścieżkę instalacji
                    var configs = configService.LoadConfig();
                    var modConfig = configs.FirstOrDefault(c => c.ModName == modItem.Name);
                    if (modConfig != null)
                    {
                        modConfig.InstallPath = string.Empty;
                        ConfigManager.SaveConfig(configs);
                    }

                    // NIE czyścimy modItem.InstallPath - dzięki temu IsInstalled pozostaje true
                    // podczas aktualizacji, UI nie przełącza się na "Nie zainstalowano",
                    // a pasek postępu (ShowProgress) jest widoczny.
                    // Nowa ścieżka zostanie przypisana po zakończeniu instalacji.
                    
                    UpdateModProgress(modItem, progressDialog, 50, "Rozpoczynanie instalacji nowej wersji...");

                    // INSTALL
                    var updatedConfigs = configService.LoadConfig();
                    var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == modItem.Name);

                    if (updatedConfig != null)
                    {
                        // Progress reporter dla instalacji
                        var progressReporter = new UIProgressReporter((percentage, message) =>
                        {
                            int mappedProgress = 50 + (percentage * 50 / 100);
                            UpdateModProgress(modItem, progressDialog, mappedProgress, $"Instalowanie: {message}");
                        });

                        var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update-{modItem.Name}] {message}");
                        });

                        var silentUserInteraction = new InstallationSilentUserInteraction();

                        var userSettings = _userSettingsService.LoadUserSettings();
                        string platform = userSettings.Mode;

                        if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_userInteractionService == null)
                            {
                                System.Diagnostics.Debug.WriteLine("UserInteractionService is null - cannot proceed with Epic update");
                                return false;
                            }

                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);
                            epicManager.SpeedChanged += (speed) =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    modItem.DownloadSpeed = speed;
                                });
                            };
                            await epicManager.ModifyEpicAsync(updatedConfig, new object(), new object());
                        }
                        else
                        {
                            var configBuilder = new ConfigurationBuilder()
                                .SetBasePath(SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory())
                                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                            var configuration = configBuilder.Build();
                            
                            var modManager = new ModManager(configuration);
                            var callbacks = new ModManagerUserCallbacks
                            {
                                ConfirmAsync = silentUserInteraction.ShowConfirmAsync,
                                ShowErrorAsync = silentUserInteraction.ShowErrorAsync,
                                ShowInfoAsync = silentUserInteraction.ShowInfoAsync,
                                RunSteamQrDownloadAsync = _userInteractionService.RunSteamQrDownloadAsync
                            };

                            var installResult = await modManager.ModifyAsync(
                                updatedConfig,
                                updatedConfigs,
                                progressReporter,
                                diagnosticsOutput,
                                callbacks,
                                "steam",
                                onSpeedUpdate: (speed) =>
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        modItem.DownloadSpeed = speed;
                                    });
                                }
                            );

                            if (!installResult.Success)
                                throw new InvalidOperationException(installResult.ErrorMessage ?? "Update failed");
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            modItem.InstallPath = updatedConfig.InstallPath;
                        });

                        // KROK 3.5: Przywróć ustawienie auto-aktualizacji (nowa Installation Map
                        // tworzona przez ModManager/EpicVersionManager ma domyślnie false)
                        if (oldAutoUpdateEnabled && !string.IsNullOrEmpty(updatedConfig.InstallPath))
                        {
                            try
                            {
                                var newInstallMap = await InstallationMapManager.LoadInstallationMapAsync(updatedConfig.InstallPath);
                                if (newInstallMap?.FullMod != null)
                                {
                                    newInstallMap.FullMod.AutoUpdateEnabled = true;
                                    newInstallMap.FullMod.LastUpdated = DateTime.Now;
                                    await InstallationMapManager.SaveInstallationMapAsync(updatedConfig.InstallPath, newInstallMap);
                                    System.Diagnostics.Debug.WriteLine($"[Update] Przywrócono AutoUpdateEnabled=true dla {modItem.Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Update] Nie udało się przywrócić AutoUpdateEnabled dla {modItem.Name}: {ex.Message}");
                            }
                        }

                        // KROK 4: Przywróć zainstalowane DLL (jeśli były)
                        if (installedDlls.Any())
                        {
                            UpdateModProgress(modItem, progressDialog, 90, $"Przywracanie {installedDlls.Count} modów DLL...");
                            System.Diagnostics.Debug.WriteLine($"[Update] Przywracanie {installedDlls.Count} modów DLL...");

                            await RestoreDllModsAfterUpdateAsync(updatedConfig, installedDlls, platform, diagnosticsOutput);
                        }
                    }
                }

                UpdateModProgress(modItem, progressDialog, 100, "Aktualizacja zakończona");
                await Task.Delay(500);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating single mod {modItem.Name}: {ex.Message}");
                UpdateModProgress(modItem, progressDialog, 100, $"Błąd: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.DownloadSpeed = null;
                });
                await Task.Delay(1500);
                return false;
            }
            finally
            {
                _activeInstallationsCount--;
                SyncIsAnyModInstalling();

                // Resetuj stan instalacji na karcie moda (dla auto-aktualizacji)
                if (progressDialog == null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        modItem.ShowProgress = false;
                        modItem.IsInstalling = false;
                        modItem.InstallStatusMessage = string.Empty;
                        modItem.InstallProgress = 0;
                    });
                }
            }
        }

        /// <summary>
        /// Aktualizuje postęp zarówno na dialogu (jeśli istnieje), jak i na karcie moda.
        /// Gdy progressDialog jest null (auto-aktualizacja), postęp widoczny jest na modItem.
        /// </summary>
        private void UpdateModProgress(ModItem modItem, UpdateProgressDialog? progressDialog, int progress, string message)
        {
            progressDialog?.UpdateProgress(progress, message);

            if (progressDialog == null)
            {
                // Auto-aktualizacja: pokaż postęp na karcie moda
                Dispatcher.UIThread.Post(() =>
                {
                    modItem.ShowProgress = true;
                    modItem.InstallProgress = progress;
                    modItem.InstallStatusMessage = message;
                });
            }
        }
    
        private async Task<bool> UpdateSingleModAsync(ModItem modItem, ModUpdateInfo updateInfo)
        {
            IsAnyModInstalling = true;
            try
            {
                var configService = new ConfigService();

                // 1. Pobierz zaktualizowaną konfigurację
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.InstallProgress = 10;
                    modItem.InstallStatusMessage = "Pobieranie nowej konfiguracji...";
                });
                
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(modItem.Name);
                if (updatedModConfig == null) return false;

                // 2. Aktualizuj konfigurację w pliku
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.InstallProgress = 20;
                    modItem.InstallStatusMessage = "Aktualizowanie konfiguracji...";
                });
                
                await configService.UpdateSingleModConfigAsync(updatedModConfig);

                // 3. Zaktualizuj właściwości ModItem
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.ModVersion = updatedModConfig.ModVersion;
                    modItem.AmongVersion = updatedModConfig.AmongVersion;
                    modItem.Description = updatedModConfig.Description;
                    modItem.GitHubRepoOrLink = updatedModConfig.GitHubRepoOrLink;
                    modItem.EpicGitHubRepoOrLink = updatedModConfig.EpicGitHubRepoOrLink;
                });

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.InstallProgress = 30;
                    modItem.InstallStatusMessage = "Przygotowywanie do reinstalacji...";
                });

                // 4. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(modItem.InstallPath))
                {
                    // UNINSTALL
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        modItem.InstallProgress = 40;
                        modItem.InstallStatusMessage = "Odinstalowywanie starej wersji...";
                    });

                    if (Directory.Exists(modItem.InstallPath))
                    {
                        bool deleteSuccess = await SafeDeleteDirectoryAsync(modItem.InstallPath, modItem.Name);
                        if (!deleteSuccess)
                        {
                            throw new InvalidOperationException($"Nie udało się usunąć starej wersji moda '{modItem.Name}'. Aktualizacja została przerwana.");
                        }
                    }

                    // Aktualizuj konfigurację - usuń ścieżkę instalacji
                    var configs = configService.LoadConfig();
                    var modConfig = configs.FirstOrDefault(c => c.ModName == modItem.Name);
                    if (modConfig != null)
                    {
                        modConfig.InstallPath = string.Empty;
                        ConfigManager.SaveConfig(configs);
                    }

                    modItem.InstallPath = string.Empty;
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        modItem.InstallProgress = 50;
                        modItem.InstallStatusMessage = "Rozpoczynanie instalacji nowej wersji...";
                    });

                    // INSTALL
                    var updatedConfigs = configService.LoadConfig();
                    var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == modItem.Name);

                    if (updatedConfig != null)
                    {
                        // Progress reporter dla instalacji
                        var progressReporter = new UIProgressReporter((percentage, message) =>
                        {
                            int mappedProgress = 50 + (percentage * 50 / 100);
                            Dispatcher.UIThread.Post(() =>
                            {
                                modItem.InstallProgress = mappedProgress;
                                modItem.InstallStatusMessage = $"Instalowanie: {message}";
                            });
                        });

                        var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update-{modItem.Name}] {message}");
                        });

                        var silentUserInteraction = new InstallationSilentUserInteraction();

                        var userSettings = _userSettingsService.LoadUserSettings();
                        string platform = userSettings.Mode;

                        if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_userInteractionService == null)
                            {
                                System.Diagnostics.Debug.WriteLine("UserInteractionService is null - cannot proceed with Epic update");
                                return false;
                            }

                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);
                            epicManager.SpeedChanged += (speed) =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    modItem.DownloadSpeed = speed;
                                });
                            };
                            await epicManager.ModifyEpicAsync(updatedConfig, new object(), new object());
                        }
                        else
                        {
                            var configBuilder = new ConfigurationBuilder()
                                .SetBasePath(SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory())
                                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                            var configuration = configBuilder.Build();
                            
                            var modManager = new ModManager(configuration);
                            var callbacks = new ModManagerUserCallbacks
                            {
                                ConfirmAsync = silentUserInteraction.ShowConfirmAsync,
                                ShowErrorAsync = silentUserInteraction.ShowErrorAsync,
                                ShowInfoAsync = silentUserInteraction.ShowInfoAsync,
                                RunSteamQrDownloadAsync = _userInteractionService.RunSteamQrDownloadAsync
                            };

                            var installResult = await modManager.ModifyAsync(
                                updatedConfig,
                                updatedConfigs,
                                progressReporter,
                                diagnosticsOutput,
                                callbacks,
                                "steam",
                                onSpeedUpdate: (speed) =>
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        modItem.DownloadSpeed = speed;
                                    });
                                }
                            );

                            if (!installResult.Success)
                                throw new InvalidOperationException(installResult.ErrorMessage ?? "Update failed");
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            modItem.InstallPath = updatedConfig.InstallPath;
                        });
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.InstallProgress = 100;
                    modItem.InstallStatusMessage = "Aktualizacja zakończona";
                    modItem.DownloadSpeed = null;
                });
                
                await Task.Delay(500);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating single mod {modItem.Name}: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.InstallStatusMessage = $"Błąd: {ex.Message}";
                    modItem.DownloadSpeed = null;
                });
                return false;
            }
            finally
            {
                IsAnyModInstalling = false;
            }
        }

        private async Task<bool> SafeDeleteDirectoryAsync(string directoryPath, string modName = "")
        {
            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Delete] {message}");
            });

            // Używamy nowego FileSystemUtilities z Core
            return await SUSModder.Core.Utilities.FileSystemUtilities.SafeDeleteDirectoryAsync(
                directoryPath,
                modName,
                diagnosticsOutput,
                ShowConfirmDialogAsync
            );
        }

        /// <summary>
        /// Sprawdza dostępne aktualizacje modów DLL i wyświetla dialog
        /// </summary>
        private async Task CheckDllUpdates()
        {
            try
            {
                _diagnosticsOutput?.Write("[CheckDllUpdates] Rozpoczynam sprawdzanie aktualizacji DLL...");

                var configService = new ConfigService();
                var dllUpdateManager = new DllUpdateManager(_dllModificationService, configService, _diagnosticsOutput ?? new UIDiagnosticsOutput(_ => { }));

                // Pobierz platform (steam/epic)
                string platform = DeterminePlatform().ToLower();

                // Pobierz listę aktualizacji
                var updates = await dllUpdateManager.CheckDllUpdatesAsync(platform);

                if (updates == null || !updates.Any())
                {
                    _diagnosticsOutput?.Write("[CheckDllUpdates] Brak dostępnych aktualizacji");
                    return;
                }

                _diagnosticsOutput?.Write($"[CheckDllUpdates] Znaleziono {updates.Count} aktualizacji");

                // Dla każdego DLL pokazujemy osobny dialog (jak dla modów FULL)
                foreach (var updateInfo in updates)
                {
                    try
                    {
                        // Pokaż dialog potwierdzenia dla tego DLL
                        bool confirmed = await ShowDllUpdateConfirmDialogAsync(updateInfo);
                        
                        if (!confirmed)
                        {
                            _diagnosticsOutput?.Write($"[CheckDllUpdates] Użytkownik pominął aktualizację {updateInfo.DllMod.ModName}");
                            continue;
                        }

                        // Pokaż dialog postępu
                        DllUpdateProgressDialog? progressDialog = null;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            progressDialog = new DllUpdateProgressDialog(updateInfo.DllMod.ModName);
                            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                            if (mainWindow != null)
                            {
                                progressDialog.Show(mainWindow);
                            }
                        });

                        // Wykonaj aktualizację z progresem
                        var result = await UpdateDllWithProgressAsync(updateInfo, platform, progressDialog);

                        // Zamknij dialog postępu
                        if (progressDialog != null && progressDialog.IsVisible)
                        {
                            await Task.Delay(1000); // Krótka pauza żeby użytkownik zobaczył "100%"
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressDialog.Close();
                            });
                        }

                        // Pokaż wynik
                        if (result.SuccessfulUpdates > 0)
                        {
                            var successMessage = $"✅ Pomyślnie zaktualizowano {updateInfo.DllMod.ModName} w {result.SuccessfulUpdates} lokalizacjach";
                            if (result.FailedUpdates > 0)
                            {
                                successMessage += $"\n\n❌ Nieudane aktualizacje: {result.FailedUpdates}\n• " + 
                                    string.Join("\n• ", result.FailedLocations);
                            }
                            await ShowMessageAsync("Aktualizacja zakończona", successMessage);
                        }
                        else
                        {
                            await ShowErrorDialogAsync(
                                $"Nie udało się zaktualizować {updateInfo.DllMod.ModName} w żadnej lokalizacji.\n\n" +
                                $"Nieudane lokalizacje:\n• " + string.Join("\n• ", result.FailedLocations),
                                "Błąd aktualizacji");
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnosticsOutput?.Write($"[CheckDllUpdates ERROR] Błąd dla {updateInfo.DllMod.ModName}: {ex.Message}");
                        await ShowErrorDialogAsync(
                            $"Błąd podczas aktualizacji {updateInfo.DllMod.ModName}: {ex.Message}",
                            "Błąd");
                    }
                }

                // Odśwież listę modów po wszystkich aktualizacjach
                await RefreshModsListAsync();
                _diagnosticsOutput?.Write("[CheckDllUpdates] Wszystkie aktualizacje DLL zakończone");
            }
            catch (Exception ex)
            {
                _diagnosticsOutput?.Write($"[CheckDllUpdates ERROR] {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji DLL: {ex.Message}", "Błąd");
            }
        }

        /// <summary>
        /// Pokazuje dialog potwierdzenia aktualizacji DLL
        /// </summary>
        private async Task<bool> ShowDllUpdateConfirmDialogAsync(DllUpdateInfo updateInfo)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new DllUpdateConfirmDialog(updateInfo);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                    return dialog.Result;
                }

                return false;
            });
        }

        /// <summary>
        /// Aktualizuje DLL z pokazaniem postępu
        /// </summary>
        private async Task<DllUpdateResult> UpdateDllWithProgressAsync(
            DllUpdateInfo updateInfo,
            string platform,
            DllUpdateProgressDialog? progressDialog)
        {
            var result = new DllUpdateResult
            {
                DllName = updateInfo.DllMod.ModName,
                TotalLocations = updateInfo.SelectedLocations.Count
            };

            int current = 0;
            foreach (var fullMod in updateInfo.SelectedLocations)
            {
                current++;
                
                try
                {
                    // Aktualizuj progress
                    progressDialog?.UpdateProgress(
                        current, 
                        result.TotalLocations, 
                        fullMod.ModName,
                        "Pobieranie i instalacja...");

                    _diagnosticsOutput?.Write($"[DllUpdate] Aktualizowanie {updateInfo.DllMod.ModName} w {fullMod.ModName}");

                    var installedPath = await _dllModificationService.InstallDllToModAsync(
                        updateInfo.DllMod,
                        fullMod,
                        platform
                    );

                    if (!string.IsNullOrEmpty(installedPath))
                    {
                        result.SuccessfulUpdates++;
                        result.UpdatedLocations.Add(fullMod.ModName);
                        _diagnosticsOutput?.Write($"[DllUpdate] ✓ Zaktualizowano w {fullMod.ModName}");
                    }
                    else
                    {
                        result.FailedUpdates++;
                        result.FailedLocations.Add(fullMod.ModName);
                        _diagnosticsOutput?.Write($"[DllUpdate] ✗ Nie udało się zaktualizować w {fullMod.ModName}");
                    }
                }
                catch (Exception ex)
                {
                    _diagnosticsOutput?.Write($"[ERROR] Błąd aktualizacji w {fullMod.ModName}: {ex.Message}");
                    result.FailedUpdates++;
                    result.FailedLocations.Add(fullMod.ModName);
                    
                    progressDialog?.SetError($"Błąd w {fullMod.ModName}");
                    await Task.Delay(1000);
                }
            }

            progressDialog?.SetCompleted();
            return result;
        }

        /// <summary>
        /// Przywraca zainstalowane mody DLL po aktualizacji moda FULL
        /// </summary>
        private async Task RestoreDllModsAfterUpdateAsync(
            ModConfiguration fullMod,
            List<DllModInstallation> dllsToRestore,
            string platform,
            IDiagnosticsOutput log)
        {
            if (!dllsToRestore.Any())
                return;

            var configs = ConfigManager.LoadConfig();
            var configService = new ConfigService();
            var dllModificationService = new DllModificationService(configService, log);

            // Odśwież fullMod z zapisanej konfiguracji aby mieć aktualny InstallPath
            var updatedFullMod = configs.FirstOrDefault(c => c.Id == fullMod.Id);
            if (updatedFullMod == null)
            {
                log.Write($"[Update] ❌ Nie znaleziono zaktualizowanej konfiguracji dla {fullMod.ModName} - nie można przywrócić DLL");
                return;
            }

            int restoredCount = 0;
            int failedCount = 0;

            foreach (var dllInfo in dllsToRestore)
            {
                try
                {
                    var dllConfig = configs.FirstOrDefault(c => c.Id == dllInfo.ModId);

                    if (dllConfig == null)
                    {
                        log.Write($"[Update] Nie znaleziono konfiguracji dla DLL (ID: {dllInfo.ModId}, nazwa: {dllInfo.ModName}) - pomijam");
                        failedCount++;
                        continue;
                    }

                    log.Write($"[Update] Przywracanie DLL: {dllConfig.ModName} v{dllInfo.ModVersion}");

                    var installedPath = await dllModificationService.InstallDllToModAsync(
                        dllConfig,
                        updatedFullMod,  // Używamy zaktualizowanej konfiguracji z InstallPath
                        platform
                    );

                    if (!string.IsNullOrEmpty(installedPath))
                    {
                        restoredCount++;
                        log.Write($"[Update] ✓ Przywrócono: {dllConfig.ModName}");
                    }
                    else
                    {
                        failedCount++;
                        log.Write($"[Update] ✗ Nie udało się przywrócić: {dllConfig.ModName}");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    log.Write($"[ERROR] Błąd podczas przywracania DLL {dllInfo.ModName}: {ex.Message}");
                }
            }

            log.Write($"[Update] Podsumowanie przywracania DLL: {restoredCount} sukces, {failedCount} niepowodzenie");
        }
    }
}

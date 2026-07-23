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
        private sealed record CompletedModUpdate(
            int ModId,
            string ModName,
            string CurrentVersion,
            string NewVersion);
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
            var successfulUpdates = new List<CompletedModUpdate>();
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
                            successfulUpdates.Add(new CompletedModUpdate(
                                modUpdate.LocalMod?.Id ?? 0,
                                modUpdate.ModName,
                                modUpdate.CurrentVersion,
                                modUpdate.NewVersion));
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

        private async Task ShowUpdateSummaryAsync(
            List<CompletedModUpdate> successful, List<string> failed, List<string> skipped)
        {
            if (successful.Count == 0 && failed.Count == 0 && skipped.Count == 0)
                return;

            var title = _localizationService.Get("MainWindow.UpdateSuccess.Title");
            var messageBuilder = new System.Text.StringBuilder();

            if (successful.Any())
            {
                var successLine = _localizationService.GetFormatted(
                    "MainWindow.UpdateSuccess.SuccessCount", successful.Count);
                messageBuilder.AppendLine(successLine);
                foreach (var update in successful)
                {
                    messageBuilder.AppendLine(
                        $"   • {update.ModName} ({update.CurrentVersion} → {update.NewVersion})");
                }
                messageBuilder.AppendLine();

                // Dodaj informację o changelogu
                var changelogHint = _localizationService.Get(
                    "ModChangelog.Button") + " — " +
                    _localizationService.Get("MainWindow.UpdateSuccess.ChangelogHint");
                messageBuilder.AppendLine(changelogHint);
                messageBuilder.AppendLine();
            }

            if (failed.Any())
            {
                var failLine = _localizationService.GetFormatted(
                    "MainWindow.UpdateSuccess.FailCount", failed.Count);
                messageBuilder.AppendLine(failLine);
                foreach (var failure in failed)
                {
                    messageBuilder.AppendLine($"   • {failure}");
                }
                messageBuilder.AppendLine();
            }

            if (skipped.Any())
            {
                var skipLine = _localizationService.GetFormatted(
                    "MainWindow.UpdateSuccess.SkipCount", skipped.Count);
                messageBuilder.AppendLine(skipLine);
                foreach (var skippedMod in skipped)
                {
                    messageBuilder.AppendLine($"   • {skippedMod}");
                }
            }

            await ShowMessageAsync(title, messageBuilder.ToString());
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
            string backupPath = "";
            bool hasBackup = false;
            var configService = new ConfigService();
            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Update-{modItem.Name}] {message}");
            });
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
                    // KROK 0: Snapshot addonów DLL i flag FULL PRZED usunięciem katalogu.
                    var dllModificationService = new DllModificationService(configService, diagnosticsOutput);
                    var addonPreservationService = new FullModAddonPreservationService(
                        configService,
                        dllModificationService,
                        diagnosticsOutput);
                    var addonSnapshot = await addonPreservationService.CaptureFromInstallationMapAsync(new ModConfiguration
                    {
                        Id = modItem.Id,
                        ModName = modItem.Name,
                        ModType = "full",
                        ModVersion = modItem.ModVersion,
                        InstallPath = modItem.InstallPath
                    });
                    System.Diagnostics.Debug.WriteLine($"[Update] Snapshot DLL dla {modItem.Name}: {addonSnapshot.DllAddons.Count} addonów, AutoUpdateEnabled={addonSnapshot.FullModAutoUpdateEnabled}");

                    // ATOMIC UPDATE: zamiast usuwać stary mod PRZED instalacją,
                    // przenosimy go do backupu. Jeśli instalacja się nie powiedzie,
                    // przywracamy backup — stara wersja pozostaje nienaruszona.
                    UpdateModProgress(modItem, progressDialog, 40, "Przygotowywanie backupu starej wersji...");

                    backupPath = modItem.InstallPath + ".backup";

                    if (Directory.Exists(modItem.InstallPath))
                    {
                        // Usuń stary backup jeśli istnieje (pozostałość po poprzedniej nieudanej aktualizacji)
                        if (Directory.Exists(backupPath))
                        {
                            await SafeDeleteDirectoryAsync(backupPath, modItem.Name + " (stary backup)");
                        }

                        // Przenieś aktualną instalację do backupu (atomowe rename, szybkie)
                        Directory.Move(modItem.InstallPath, backupPath);
                        hasBackup = true;
                        System.Diagnostics.Debug.WriteLine($"[Update] Backup utworzony: {backupPath}");
                    }

                    // Aktualizuj konfigurację - usuń ścieżkę instalacji przed reinstalacją
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

                        // KROK 4: Przywróć flagi FULL oraz zainstalowane DLL (jeśli były)
                        if (!addonSnapshot.IsEmpty)
                        {
                            UpdateModProgress(
                                modItem,
                                progressDialog,
                                90,
                                _localizationService.GetFormatted("Updates.FullMod.RestoringDllAddons", addonSnapshot.DllAddons.Count));
                            System.Diagnostics.Debug.WriteLine($"[Update] Przywracanie {addonSnapshot.DllAddons.Count} addonów DLL...");
                        }

                        var restoreResult = await addonPreservationService.RestoreToFullModAsync(updatedConfig, addonSnapshot, platform);
                        System.Diagnostics.Debug.WriteLine($"[Update] DLL restore: {restoreResult.RestoredCount} OK, {restoreResult.SkippedCount} pominięto, {restoreResult.FailedCount} błędów");

                        if (restoreResult.HasProblems)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ToastService.ShowWarning(
                                    _localizationService.GetFormatted(
                                        "Updates.FullMod.DllRestorePartialSuccess",
                                        restoreResult.RestoredCount,
                                        restoreResult.SkippedCount,
                                        restoreResult.FailedCount));
                            });
                        }
                    }
                }

                UpdateModProgress(modItem, progressDialog, 100, "Aktualizacja zakończona");
                await Task.Delay(500);

                // Posprzątaj backup po udanej aktualizacji
                if (hasBackup && Directory.Exists(backupPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Update] Usuwam backup po udanej aktualizacji: {backupPath}");
                    await SafeDeleteDirectoryAsync(backupPath, modItem.Name + " (backup)");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating single mod {modItem.Name}: {ex.Message}");
                UpdateModProgress(modItem, progressDialog, 100, $"Błąd: {ex.Message}");

                // Przywróć backup jeśli instalacja się nie powiodła
                if (hasBackup && Directory.Exists(backupPath))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[Update] Przywracam backup po nieudanej aktualizacji: {backupPath}");
                        // Usuń nieudaną instalację (jeśli cokolwiek powstało)
                        if (!string.IsNullOrEmpty(modItem.InstallPath) && Directory.Exists(modItem.InstallPath))
                        {
                            await SafeDeleteDirectoryAsync(modItem.InstallPath, modItem.Name + " (failed)");
                        }
                        // Przywróć backup
                        if (!string.IsNullOrEmpty(modItem.InstallPath))
                        {
                            Directory.Move(backupPath, modItem.InstallPath);
                            System.Diagnostics.Debug.WriteLine($"[Update] Backup przywrócony: {modItem.InstallPath}");
                        }

                        // Przywróć ścieżkę instalacji w konfiguracji
                        try
                        {
                            var restoredConfigs = configService.LoadConfig();
                            var restoredConfig = restoredConfigs.FirstOrDefault(c => c.ModName == modItem.Name);
                            if (restoredConfig != null)
                            {
                                restoredConfig.InstallPath = modItem.InstallPath;
                                ConfigManager.SaveConfig(restoredConfigs);
                            }
                        }
                        catch (Exception restoreEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update] Nie udało się przywrócić ścieżki w konfiguracji: {restoreEx.Message}");
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Update] CRITICAL: Nie udało się przywrócić backupu! {restoreEx.Message}");
                    }
                }

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

                // ── Podziel na auto-update i manualne ──
                var installedFullMods = configService.LoadConfig()
                    .Where(c => c.ModType == "full" && !string.IsNullOrEmpty(c.InstallPath))
                    .ToList();

                var (autoUpdates, manualUpdates) = await dllUpdateManager.FilterAutoUpdateDllsAsync(updates, installedFullMods);

                // ── Wykonaj ciche auto-update ──
                if (autoUpdates.Any())
                {
                    _diagnosticsOutput?.Write($"[CheckDllUpdates] Uruchamiam ciche auto-update {autoUpdates.Count} DLL...");
                    var autoResults = await dllUpdateManager.RunAutoUpdatesAsync(autoUpdates, platform);

                    int autoSuccess = autoResults.Sum(r => r.SuccessfulUpdates);
                    int autoFailed = autoResults.Sum(r => r.FailedUpdates);

                    if (autoSuccess > 0)
                    {
                        // Użyj klucza i18n dla auto-aktualizacji
                        var msg = _localizationService.GetFormatted("Dialogs.ModUpdatesAutoUpdated", autoSuccess);
                        ToastService.ShowInfo(msg);
                    }
                    else if (autoFailed > 0)
                    {
                        _diagnosticsOutput?.Write($"[CheckDllUpdates] ⚠ Wszystkie auto-update DLL nieudane ({autoFailed}), ponowna próba przy następnym sprawdzeniu");
                    }

                    _diagnosticsOutput?.Write($"[CheckDllUpdates] Auto-update zakończone: {autoSuccess} OK, {autoFailed} FAIL");
                }

                // Jeśli nie ma ręcznych aktualizacji, zakończ
                if (!manualUpdates.Any())
                {
                    if (autoUpdates.Any())
                    {
                        // Odśwież listę modów po auto-update
                        await RefreshModsListAsync();
                    }
                    _diagnosticsOutput?.Write("[CheckDllUpdates] Brak ręcznych aktualizacji DLL do pokazania");
                    return;
                }

                // ── Pokaż dialogi dla ręcznych aktualizacji ──
                foreach (var updateInfo in manualUpdates)
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
                            var successMessage = _localizationService.GetFormatted(
                                "Updates.DllUpdateSuccess",
                                updateInfo.DllMod.ModName,
                                result.SuccessfulUpdates);
                            if (result.FailedUpdates > 0)
                            {
                                successMessage += "\n\n" + _localizationService.GetFormatted(
                                    "Updates.FailedUpdates", result.FailedUpdates) + "\n• " +
                                    string.Join("\n• ", result.FailedLocations);
                            }
                            await ShowMessageAsync(
                                _localizationService.Get("Updates.UpdateCompleted"),
                                successMessage);
                        }
                        else
                        {
                            await ShowErrorDialogAsync(
                                _localizationService.GetFormatted(
                                    "Updates.DllUpdateFailed",
                                    updateInfo.DllMod.ModName,
                                    string.Join("\n• ", result.FailedLocations)),
                                _localizationService.Get("Updates.DllUpdateFailedTitle"));
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnosticsOutput?.Write($"[CheckDllUpdates ERROR] Błąd dla {updateInfo.DllMod.ModName}: {ex.Message}");
                        await ShowErrorDialogAsync(
                            _localizationService.GetFormatted(
                                "Updates.DllUpdateError",
                                updateInfo.DllMod.ModName,
                                ex.Message),
                            _localizationService.Get("Updates.DllUpdateFailedTitle"));
                    }
                }

                // Odśwież listę modów po wszystkich aktualizacjach
                await RefreshModsListAsync();
                _diagnosticsOutput?.Write("[CheckDllUpdates] Wszystkie aktualizacje DLL zakończone");
            }
            catch (Exception ex)
            {
                _diagnosticsOutput?.Write($"[CheckDllUpdates ERROR] {ex.Message}");
                await ShowErrorDialogAsync(
                    _localizationService.GetFormatted("Updates.DllCheckError", ex.Message),
                    _localizationService.Get("Updates.DllUpdateFailedTitle"));
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

    }
}

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
        private async Task CheckForModUpdatesAsync()
        {
            try
            {
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
                    await RefreshModsListAsync();
                }

                // Pokaż dialogi aktualizacji dla każdego moda z dostępną aktualizacją
                if (result.InstalledModUpdates.Any())
                {
                    await ProcessUpdatesWithIndividualDialogsAsync(result.InstalledModUpdates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during update check: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji: {ex.Message}", "Błąd");
            }
        }

        private async Task ShowUpdateDialogAsync(List<ModUpdateInfo> availableUpdates)
        {
            var updateDialog = new UpdateDialog(availableUpdates);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (mainWindow != null)
            {
                updateDialog.Show(mainWindow);

                while (!updateDialog.DialogResult && updateDialog.IsVisible)
                {
                    await Task.Delay(100);
                }

                if (updateDialog.DialogResult && updateDialog.IsVisible)
                {
                    var selectedMods = updateDialog.GetSelectedMods();
                    if (selectedMods.Any())
                    {
                        await ProcessSelectedUpdatesWithProgressAsync(selectedMods, updateDialog);
                    }
                }

                if (updateDialog.IsVisible)
                {
                    updateDialog.Close();
                }

                await RefreshModsListAsync();
            }
        }

        private async Task<bool> ShowUpdateModConfirmDialogAsync(ModUpdateInfo updateInfo)
        {
            var dialog = new UpdateModConfirmDialog(updateInfo);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                return dialog.Result;
            }

            return false;
        }

        private async Task ProcessUpdatesWithIndividualDialogsAsync(List<ModUpdateInfo> availableUpdates)
        {
            var successfulUpdates = new List<string>();
            var failedUpdates = new List<string>();
            var skippedUpdates = new List<string>();

            foreach (var modUpdate in availableUpdates)
            {
                try
                {
                    // Pokaż dialog potwierdzenia dla każdego moda
                    bool confirmed = await ShowUpdateModConfirmDialogAsync(modUpdate);
                    
                    if (!confirmed)
                    {
                        skippedUpdates.Add(modUpdate.ModName);
                        continue;
                    }

                    // Stwórz lub znajdź ModItem
                    var modItem = await GetOrCreateModItemAsync(modUpdate);

                    // Pokaż dialog postępu aktualizacji
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating {modUpdate.ModName}: {ex.Message}");
                    failedUpdates.Add(modUpdate.ModName);
                }
            }

            // Odśwież listę modów
            await RefreshModsListAsync();

            // Pokaż podsumowanie
            await ShowUpdateSummaryAsync(successfulUpdates, failedUpdates, skippedUpdates);
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

        private async Task ProcessSelectedUpdatesWithProgressAsync(List<ModUpdateInfo> selectedMods, UpdateDialog dialog)
        {
            int totalMods = selectedMods.Count;
            int currentMod = 0;
            var successfulUpdates = new List<string>();
            var failedUpdates = new List<string>();
            var skippedUpdates = new List<string>();

            foreach (var modUpdate in selectedMods)
            {
                currentMod++;

                try
                {
                    // Pokaż dialog potwierdzenia dla każdego moda
                    bool confirmed = await ShowUpdateModConfirmDialogAsync(modUpdate);
                    
                    if (!confirmed)
                    {
                        skippedUpdates.Add(modUpdate.ModName);
                        continue;
                    }

                    // Aktualizuj ogólny progress
                    dialog.UpdateOverallProgress(currentMod, totalMods, modUpdate.ModName);

                    // Stwórz lub znajdź ModItem
                    var modItem = await GetOrCreateModItemAsync(modUpdate);

                    // Wykonaj aktualizację z progress callbackami
                    bool success = await UpdateSingleModWithProgressAsync(modItem, modUpdate, dialog);

                    if (success)
                    {
                        successfulUpdates.Add($"{modUpdate.ModName} ({modUpdate.CurrentVersion} → {modUpdate.NewVersion})");
                    }
                    else
                    {
                        failedUpdates.Add(modUpdate.ModName);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating {modUpdate.ModName}: {ex.Message}");
                    dialog.UpdateCurrentModProgress(100, $"Błąd: {ex.Message}");
                    failedUpdates.Add(modUpdate.ModName);
                    await Task.Delay(1000);
                }
            }

            // Przygotuj końcową wiadomość
            var finalMessageBuilder = new System.Text.StringBuilder();
            finalMessageBuilder.AppendLine("🎉 Aktualizacja zakończona!");
            finalMessageBuilder.AppendLine();

            if (successfulUpdates.Any())
            {
                finalMessageBuilder.AppendLine($"✅ Pomyślnie zaktualizowano ({successfulUpdates.Count}):");
                foreach (var update in successfulUpdates)
                {
                    finalMessageBuilder.AppendLine($"   • {update}");
                }
                finalMessageBuilder.AppendLine();
            }

            if (failedUpdates.Any())
            {
                finalMessageBuilder.AppendLine($"❌ Nie udało się zaktualizować ({failedUpdates.Count}):");
                foreach (var failure in failedUpdates)
                {
                    finalMessageBuilder.AppendLine($"   • {failure}");
                }
                finalMessageBuilder.AppendLine();
            }

            if (skippedUpdates.Any())
            {
                finalMessageBuilder.AppendLine($"⏭️ Pominięto ({skippedUpdates.Count}):");
                foreach (var skipped in skippedUpdates)
                {
                    finalMessageBuilder.AppendLine($"   • {skipped}");
                }
                finalMessageBuilder.AppendLine();
            }

            finalMessageBuilder.AppendLine("Możesz teraz zamknąć to okno.");

            // Przekaż końcową wiadomość do dialogu
            dialog.ShowFinalSummary(finalMessageBuilder.ToString());
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
            try
            {
                var configService = new ConfigService();

                // 1. Pobierz zaktualizowaną konfigurację
                progressDialog?.UpdateProgress(10, "Pobieranie nowej konfiguracji...");
                
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(modItem.Name);
                if (updatedModConfig == null) return false;

                // 2. Aktualizuj konfigurację w pliku
                progressDialog?.UpdateProgress(20, "Aktualizowanie konfiguracji...");
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

                progressDialog?.UpdateProgress(30, "Przygotowywanie do reinstalacji...");

                // 4. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(modItem.InstallPath))
                {
                    // UNINSTALL
                    progressDialog?.UpdateProgress(40, "Odinstalowywanie starej wersji...");

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
                    
                    progressDialog?.UpdateProgress(50, "Rozpoczynanie instalacji nowej wersji...");

                    // INSTALL
                    var updatedConfigs = configService.LoadConfig();
                    var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == modItem.Name);

                    if (updatedConfig != null)
                    {
                        // Progress reporter dla instalacji
                        var progressReporter = new UIProgressReporter((percentage, message) =>
                        {
                            int mappedProgress = 50 + (percentage * 50 / 100);
                            progressDialog?.UpdateProgress(mappedProgress, $"Instalowanie: {message}");
                        });

                        var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update-{modItem.Name}] {message}");
                        });

                        var silentUserInteraction = new InstallationSilentUserInteraction();

                        var configBuilder = new ConfigurationBuilder()
                            .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                        var configuration = configBuilder.Build();

                        string platform = configuration.GetSection("Configuration")["Mode"] ?? "steam";

                        if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_userInteractionService == null)
                            {
                                System.Diagnostics.Debug.WriteLine("UserInteractionService is null - cannot proceed with Epic update");
                                return false;
                            }

                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);
                            await epicManager.ModifyEpicAsync(updatedConfig, new object(), new object());
                        }
                        else
                        {
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

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            modItem.InstallPath = updatedConfig.InstallPath;
                        });
                    }
                }

                progressDialog?.UpdateProgress(100, "Aktualizacja zakończona");
                await Task.Delay(500);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating single mod {modItem.Name}: {ex.Message}");
                progressDialog?.UpdateProgress(100, $"Błąd: {ex.Message}");
                await Task.Delay(1500);
                return false;
            }
        }

        private async Task<bool> UpdateSingleModAsync(ModItem modItem, ModUpdateInfo updateInfo)
        {
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

                        var configBuilder = new ConfigurationBuilder()
                            .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                        var configuration = configBuilder.Build();

                        string platform = configuration.GetSection("Configuration")["Mode"] ?? "steam";

                        if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_userInteractionService == null)
                            {
                                System.Diagnostics.Debug.WriteLine("UserInteractionService is null - cannot proceed with Epic update");
                                return false;
                            }

                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);
                            await epicManager.ModifyEpicAsync(updatedConfig, new object(), new object());
                        }
                        else
                        {
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
                });
                return false;
            }
        }

        private async Task<bool> UpdateSingleModWithProgressAsync(ModItem modItem, ModUpdateInfo updateInfo, UpdateDialog dialog)
        {
            try
            {
                var configService = new ConfigService();

                // 1. Pobierz zaktualizowaną konfigurację
                dialog.UpdateCurrentModProgress(10, "Pobieranie nowej konfiguracji...");
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(modItem.Name);
                if (updatedModConfig == null) return false;

                // 2. Aktualizuj konfigurację w pliku
                dialog.UpdateCurrentModProgress(20, "Aktualizowanie konfiguracji...");
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

                dialog.UpdateCurrentModProgress(30, "Przygotowywanie do reinstalacji...");

                // 4. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(modItem.InstallPath))
                {
                    // UNINSTALL
                    dialog.UpdateCurrentModProgress(40, "Odinstalowywanie starej wersji...");

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
                    dialog.UpdateCurrentModProgress(50, "Rozpoczynanie instalacji nowej wersji...");

                    // INSTALL
                    // Pobierz zaktualizowaną konfigurację
                    var updatedConfigs = configService.LoadConfig();
                    var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == modItem.Name);

                    if (updatedConfig != null)
                    {
                        // Progress reporter dla instalacji
                        var progressReporter = new UIProgressReporter((percentage, message) =>
                        {
                            // Mapuj progress 50-100% dla install
                            int mappedProgress = 50 + (percentage * 50 / 100);
                            dialog.UpdateCurrentModProgress(mappedProgress, $"Instalowanie: {message}");
                        });

                        // Diagnostics output
                        var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update-{modItem.Name}] {message}");
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
                            if (_userInteractionService == null)
                            {
                                System.Diagnostics.Debug.WriteLine("UserInteractionService is null - cannot proceed with Epic update");
                                return false;
                            }

                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                            // Przekaż puste obiekty zamiast null
                            await epicManager.ModifyEpicAsync(updatedConfig, new object(), new object());
                        }
                        else
                        {
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
                            modItem.InstallPath = updatedConfig.InstallPath;
                        });
                    }
                }

                dialog.UpdateCurrentModProgress(100, "Aktualizacja zakończona");
                await Task.Delay(500); // Krótka pauza żeby użytkownik zobaczył ukończenie
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating single mod {modItem.Name}: {ex.Message}");
                dialog.UpdateCurrentModProgress(100, $"Błąd: {ex.Message}");
                return false;
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
    }
}

using System;
using System.Diagnostics;
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
    /// Partial class zawierający logikę uruchamiania gry
    /// </summary>
    public partial class MainWindowViewModel
    {
        public async Task LaunchAsync()
        {
            await Task.Run(() => Launch());
        }

        private async void Launch()
        {
            // 1) Walidacja wyboru
            if (SelectedMod == null)
            {
                await ShowErrorDialogAsync("Nie wybrano wersji gry do uruchomienia.", "Błąd");
                return;
            }

            // Pobierz konfigurację wybranego moda
            var configService = new ConfigService();
            var configs = configService.LoadConfig();
            var modConfig = configs.FirstOrDefault(c => c.ModName == SelectedMod.Name);

            if (modConfig == null)
            {
                await ShowErrorDialogAsync("Brak wybranej wersji do uruchomienia.", "Błąd");
                return;
            }

            // Sprawdź czy mod jest zainstalowany
            if (string.IsNullOrEmpty(modConfig.InstallPath))
            {
                await ShowErrorDialogAsync("Wybrany mod nie jest zainstalowany.", "Błąd");
                return;
            }

            // 2) Włączamy UI „busy"
            var currentSelectedMod = SelectedMod;
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
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                string mode = configuration.GetSection("Configuration")["Mode"] ?? "steam";

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
                currentSelectedMod.IsInstalling = false;
            }
        }

        private async Task LaunchEpicGameAsync(ModItem currentSelectedMod, ModConfiguration modConfig)
        {
            // 4) Obsługa Epic z progress parserem
            currentSelectedMod.InstallStatusMessage = "Uruchamianie Epic...";
            currentSelectedMod.InstallProgress = 5;

            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Launch Epic] {message}");
            });

            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

            epicManager.ResetErrorState();

            epicManager.EpicLaunchError += async (modName, logContent) =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowEpicErrorDialogAsync(currentSelectedMod.Name, logContent);
                });
            };

            // NOWE: Subskrybuj progress z legendary
            epicManager.ProgressChanged += (percentage, message) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.InstallProgress = percentage;
                    currentSelectedMod.InstallStatusMessage = message;
                });
            };

            // Podpinamy event do przekazywania linii do debug output
            epicManager.LegendaryOutput += (message) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                    System.Diagnostics.Debug.WriteLine($"[Legendary] {message}")
                );
            };

            // Wywołujemy Epic
            await epicManager.HandleEpicGameAsync(modConfig);
        }

        private async Task LaunchSteamGameAsync(ModItem currentSelectedMod, ModConfiguration modConfig)
        {
            // 5) Obsługa Steam / vanilla
            currentSelectedMod.InstallStatusMessage = "Uruchamiam Steam...";
            currentSelectedMod.InstallProgress = 25;

            if (string.IsNullOrEmpty(modConfig.InstallPath))
            {
                throw new InvalidOperationException("InstallPath nie może być pusty.");
            }

            // Uwzględnij strukturę Epic (podkatalog AmongUs)
            string actualModPath = PathSettings.GetActualModPath(modConfig.InstallPath);
            string exePath = Path.Combine(actualModPath, "Among Us.exe");
            string steamAppIdPath = Path.Combine(actualModPath, "steam_appid.txt");

            try
            {
                // Zapisz steam_appid.txt
                await File.WriteAllTextAsync(steamAppIdPath, "945360");

                currentSelectedMod.InstallProgress = 50;

                if (File.Exists(exePath))
                {
                    currentSelectedMod.InstallStatusMessage = "Uruchamiam grę...";
                    currentSelectedMod.InstallProgress = 75;

                    // Uruchom Steam
                    Process.Start(new ProcessStartInfo("steam://") { UseShellExecute = true });

                    // Poczekaj chwilę i uruchom grę
                    await Task.Delay(1000);
                    Process.Start(exePath);

                    currentSelectedMod.InstallProgress = 100;
                    currentSelectedMod.InstallStatusMessage = "Gra uruchomiona";

                    // Poczekaj chwilę żeby użytkownik zobaczył komunikat
                    await Task.Delay(1500);
                }
                else
                {
                    await ShowErrorDialogAsync(
                        "Nie znaleziono pliku Among Us.exe w wybranej ścieżce.",
                        "Błąd");
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Problem z utworzeniem pliku steam_appid.txt: {ex.Message}");

                await ShowErrorDialogAsync(
                    $"Problem z utworzeniem pliku steam_appid.txt: {ex.Message}. " +
                    "Próba uruchomienia przez Steam URI.",
                    "Błąd");

                try
                {
                    currentSelectedMod.InstallStatusMessage = "Uruchamiam przez Steam URI...";
                    currentSelectedMod.InstallProgress = 75;

                    Process.Start(new ProcessStartInfo("steam://rungameid/945360")
                    {
                        UseShellExecute = true
                    });

                    currentSelectedMod.InstallProgress = 100;
                    currentSelectedMod.InstallStatusMessage = "Uruchomiono przez Steam";
                    await Task.Delay(1500);
                }
                catch (Exception uriEx)
                {
                    await ShowErrorDialogAsync(
                        $"Nie udało się uruchomić gry przez Steam URI: {uriEx.Message}",
                        "Błąd");
                }
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

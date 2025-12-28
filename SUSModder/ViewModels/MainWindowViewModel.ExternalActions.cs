using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SUSModder.Core.Utilities;
using SUSModder.Views;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający akcje zewnętrzne: donacje, Discord, SUStats, lobby settings, fix black screen, shortcuts
    /// </summary>
    public partial class MainWindowViewModel
    {
        #region External Links & Windows

        private void ShowRecommendedDiscords()
        {
            IsPaneOpen = false;
            try
            {
                var discordsWindow = new RecommendedDiscordsWindow();
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    discordsWindow.Show(mainWindow);
                }
                else
                {
                    discordsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening Discord servers window: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync($"Nie udało się otworzyć okna Discord serwerów: {ex.Message}", "Błąd");
                });
            }
        }

        private async void ShowSUStatsConfig()
        {
            try
            {
                IsPaneOpen = false;
                // Zamknij inne panele i pokaż panel SUStats Config
                IsInfoPanelVisible = false;
                IsAdditionalActionsVisible = false;
                IsDllModificationsVisible = false;
                IsAppSettingsVisible = false;
                IsSUStatsConfigVisible = true;
                SelectedMod = null; // Zamknij panel wybranego moda
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening SUStats config panel: {ex.Message}");
                await ShowErrorDialogAsync($"Nie udało się otworzyć okna konfiguracji SUStats: {ex.Message}", "Błąd");
            }
        }

        #endregion

        #region Game Settings & Tools

        private async Task ShowLobbySetDialog()
        {
            var dialog = new LobbySetDialog();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                if (dialog.DialogResult)
                {
                    await ShowMessageAsync("Sukces", $"Ustawiono liczbę graczy na {dialog.PlayerCount}");
                }
            }
        }

        private async Task ExecuteFixBlackScreenAsync()
        {
            try
            {
                IsPaneOpen = false;

                // Sprawdź platformę dla opcji Firewall
                string platform = DeterminePlatform().ToLower();
                bool isSteamPlatform = platform == "steam";

                // Pokaż dialog wyboru opcji naprawy
                var dialog = new RepairOptionsDialog(isSteamPlatform);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    dialog.Show();
                    return;
                }

                // Sprawdź wybraną opcję
                switch (dialog.SelectedOption)
                {
                    case RepairOption.BlackScreen:
                        // Potwierdzenie dla naprawy czarnego ekranu
                        var confirmResult = await ShowConfirmDialogAsync(
                            _localizationService.Get("UI.Repair.BlackScreen.ConfirmMessage"),
                            _localizationService.Get("Dialogs.Confirm.Title"));

                        if (!confirmResult)
                            return;

                        // Operacje na plikach w background thread
                        await Task.Run(() => FixBlackScreen.ExecuteFixCore());

                        // Dialog sukcesu na UI thread
                        await ShowMessageAsync(
                            _localizationService.Get("Dialogs.Success.Title"),
                            _localizationService.Get("UI.Repair.BlackScreen.Success"));
                        break;

                    case RepairOption.Certificates:
                        if (OperatingSystem.IsWindows())
                        {
                            await ExecuteFixCertificatesAsync();
                        }
                        else
                        {
                            await ShowMessageAsync(
                                _localizationService.Get("Dialogs.Info.Title"),
                                "This feature is only available on Windows.");
                        }
                        break;

                    case RepairOption.Regions:
                        await ExecuteFixRegionsAsync();
                        break;

                    case RepairOption.Firewall:
                        if (OperatingSystem.IsWindows())
                        {
                            await ExecuteFixFirewallAsync();
                        }
                        else
                        {
                            await ShowMessageAsync(
                                _localizationService.Get("Dialogs.Info.Title"),
                                "This feature is only available on Windows.");
                        }
                        break;

                    case RepairOption.EpicLogout:
                        await ExecuteEpicLogoutAsync();
                        break;

                    case RepairOption.EpicLogin:
                        await ExecuteEpicLoginAsync();
                        break;

                    case RepairOption.None:
                    default:
                        // Użytkownik anulował
                        break;
                }
            }
            catch (Exception ex)
            {
                // Dialog błędu na UI thread
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        private async Task ExecuteEpicLogoutAsync()
        {
            try
            {
                // Potwierdzenie przed wylogowaniem
                var confirmResult = await ShowConfirmDialogAsync(
                    _localizationService.Get("UI.Repair.EpicAuth.LogoutConfirm"),
                    _localizationService.Get("Dialogs.Confirm.Title"));

                if (!confirmResult)
                    return;

                // Utwórz instancję EpicVersionManager
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[EpicAuth] {message}");
                });
                
                var epicManager = new SUSModder.Core.GameIntegration.EpicVersionManager(
                    diagnosticsOutput,
                    new EpicUserInteractionAdapter(_userInteractionService)
                );

                var result = await epicManager.LogoutAsync();

                if (result)
                {
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Success.Title"),
                        _localizationService.Get("UI.Repair.EpicAuth.LogoutSuccess"));
                }
                else
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("UI.Repair.EpicAuth.LogoutError"),
                        _localizationService.Get("Dialogs.Error.Title"));
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.EpicAuth.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        private async Task ExecuteEpicLoginAsync()
        {
            try
            {
                // Utwórz instancję EpicVersionManager
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[EpicAuth] {message}");
                });
                
                var epicManager = new SUSModder.Core.GameIntegration.EpicVersionManager(
                    diagnosticsOutput,
                    new EpicUserInteractionAdapter(_userInteractionService)
                );

                var result = await epicManager.LoginAsync();

                if (result)
                {
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Success.Title"),
                        _localizationService.Get("UI.Repair.EpicAuth.LoginSuccess"));
                }
                else
                {
                    // Użytkownik anulował lub wystąpił błąd - obsługa jest już w EpicVersionManager
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.EpicAuth.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task ExecuteFixCertificatesAsync()
        {
            string tempSstFile = Path.Combine(Path.GetTempPath(), "roots.sst");

            try
            {
                // Potwierdzenie dla naprawy certyfikatów
                var confirmResult = await ShowConfirmDialogAsync(
                    _localizationService.Get("UI.Repair.Certificates.ConfirmMessage"),
                    _localizationService.Get("Dialogs.Confirm.Title"));

                if (!confirmResult)
                    return;

                System.Diagnostics.Debug.WriteLine("[FixCertificates] Rozpoczynam naprawę certyfikatów...");

                // Krok 1: Generowanie pliku SST z certyfikatami Windows Update (certutil.exe)
                System.Diagnostics.Debug.WriteLine("[FixCertificates] Krok 1: Generowanie pliku SST...");
                
                var certutilProcess = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c certutil.exe -generateSSTFromWU \"{tempSstFile}\"",
                    UseShellExecute = true,
                    Verb = "runas", // Wymaga uprawnień administratora
                    CreateNoWindow = true
                };

                using (var process = Process.Start(certutilProcess))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException(_localizationService.Get("UI.Repair.Certificates.ProcessError"));
                    }
                    await Task.Run(() => process.WaitForExit());

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            string.Format(_localizationService.Get("UI.Repair.Certificates.CertutilError"), process.ExitCode));
                    }
                }

                // Sprawdź czy plik SST został utworzony
                if (!File.Exists(tempSstFile))
                {
                    throw new FileNotFoundException(_localizationService.Get("UI.Repair.Certificates.SstNotFound"));
                }

                System.Diagnostics.Debug.WriteLine("[FixCertificates] Krok 2: Importowanie certyfikatów do magazynu...");

                // Krok 2: Import certyfikatów do magazynu LocalMachine\Root (PowerShell)
                string psScript = $@"
                    $sstStore = Get-ChildItem -Path '{tempSstFile}'
                    $sstStore | Import-Certificate -CertStoreLocation Cert:\LocalMachine\Root
                ";

                var psProcess = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = true,
                    Verb = "runas", // Wymaga uprawnień administratora
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psProcess))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException(_localizationService.Get("UI.Repair.Certificates.ProcessError"));
                    }
                    await Task.Run(() => process.WaitForExit());

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            string.Format(_localizationService.Get("UI.Repair.Certificates.ImportError"), process.ExitCode));
                    }
                }

                System.Diagnostics.Debug.WriteLine("[FixCertificates] Naprawa certyfikatów zakończona pomyślnie.");

                // Dialog sukcesu
                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Success.Title"),
                    _localizationService.Get("UI.Repair.Certificates.Success"));
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Użytkownik anulował UAC prompt
                System.Diagnostics.Debug.WriteLine("[FixCertificates] Użytkownik anulował prompt UAC.");
                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Info.Title"),
                    _localizationService.Get("UI.Repair.Cancelled"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FixCertificates] Błąd: {ex.Message}");
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Certificates.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
            finally
            {
                // Posprzątaj plik tymczasowy
                try
                {
                    if (File.Exists(tempSstFile))
                    {
                        File.Delete(tempSstFile);
                        System.Diagnostics.Debug.WriteLine("[FixCertificates] Usunięto plik tymczasowy SST.");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FixCertificates] Nie udało się usunąć pliku tymczasowego: {ex.Message}");
                }
            }
        }

        private async Task ExecuteFixRegionsAsync()
        {
            try
            {
                // Potwierdzenie dla naprawy regionów
                var confirmResult = await ShowConfirmDialogAsync(
                    _localizationService.Get("UI.Repair.Regions.ConfirmMessage"),
                    _localizationService.Get("Dialogs.Confirm.Title"));

                if (!confirmResult)
                    return;

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string regionInfoPath = Path.Combine(userProfile, @"AppData\LocalLow\Innersloth\Among Us\regionInfo.json");

                System.Diagnostics.Debug.WriteLine($"[FixRegions] Próba usunięcia pliku: {regionInfoPath}");

                if (File.Exists(regionInfoPath))
                {
                    await Task.Run(() => File.Delete(regionInfoPath));
                    System.Diagnostics.Debug.WriteLine("[FixRegions] Plik regionInfo.json został usunięty.");

                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Success.Title"),
                        _localizationService.Get("UI.Repair.Regions.Success"));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[FixRegions] Plik regionInfo.json nie istnieje.");
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Info.Title"),
                        _localizationService.Get("UI.Repair.Regions.NotFound"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FixRegions] Błąd: {ex.Message}");
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task ExecuteFixFirewallAsync()
        {
            try
            {
                // Pobierz zainstalowane mody (full mods z InstallPath)
                var installedMods = Mods
                    .Where(m => !string.IsNullOrEmpty(m.InstallPath) && m.ModType == "full")
                    .Select(m => new FirewallModItem { Name = m.Name, InstallPath = m.InstallPath! })
                    .ToList();

                if (!installedMods.Any())
                {
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Info.Title"),
                        _localizationService.Get("UI.Repair.Firewall.NoModsInstalled"));
                    return;
                }

                // Pokaż dialog wyboru moda
                var dialog = new FirewallModSelectionDialog(installedMods);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
                else
                {
                    dialog.Show();
                    return;
                }

                // Sprawdź czy użytkownik wybrał mod
                if (dialog.SelectedMod == null)
                {
                    return; // Anulowano
                }

                var selectedMod = dialog.SelectedMod;
                string amongUsExePath = Path.Combine(selectedMod.InstallPath, "Among Us.exe");

                // Sprawdź czy plik exe istnieje
                if (!File.Exists(amongUsExePath))
                {
                    await ShowErrorDialogAsync(
                        string.Format(_localizationService.Get("UI.Repair.Firewall.ExeNotFound"), amongUsExePath),
                        _localizationService.Get("Dialogs.Error.Title"));
                    return;
                }

                // Potwierdzenie
                var confirmResult = await ShowConfirmDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Firewall.ConfirmMessage"), selectedMod.Name),
                    _localizationService.Get("Dialogs.Confirm.Title"));

                if (!confirmResult)
                    return;

                System.Diagnostics.Debug.WriteLine($"[FixFirewall] Dodawanie wyjątków dla: {amongUsExePath}");

                // Nazwa reguły
                string ruleName = $"Among Us - {selectedMod.Name}";
                string escapedPath = amongUsExePath.Replace("\"", "\\\"");

                // Komendy netsh do dodania reguł (przychodzące i wychodzące)
                string addInboundRule = $"netsh advfirewall firewall add rule name=\"{ruleName} (Inbound)\" dir=in action=allow program=\"{escapedPath}\" enable=yes";
                string addOutboundRule = $"netsh advfirewall firewall add rule name=\"{ruleName} (Outbound)\" dir=out action=allow program=\"{escapedPath}\" enable=yes";

                // Najpierw usuń istniejące reguły (jeśli są) - by nie tworzyć duplikatów
                string deleteInboundRule = $"netsh advfirewall firewall delete rule name=\"{ruleName} (Inbound)\"";
                string deleteOutboundRule = $"netsh advfirewall firewall delete rule name=\"{ruleName} (Outbound)\"";

                // Połącz wszystkie komendy
                string combinedCommands = $"{deleteInboundRule} & {deleteOutboundRule} & {addInboundRule} & {addOutboundRule}";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {combinedCommands}",
                    UseShellExecute = true,
                    Verb = "runas", // Wymaga uprawnień administratora
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException(_localizationService.Get("UI.Repair.Firewall.ProcessError"));
                    }
                    await Task.Run(() => process.WaitForExit());

                    // netsh zwraca 0 przy sukcesie
                    if (process.ExitCode != 0)
                    {
                        // Niektóre komendy delete mogą zwrócić błąd jeśli reguła nie istnieje - to nie jest krytyczne
                        System.Diagnostics.Debug.WriteLine($"[FixFirewall] Exit code: {process.ExitCode} - może być OK jeśli reguły nie istniały wcześniej");
                    }
                }

                System.Diagnostics.Debug.WriteLine("[FixFirewall] Reguły firewalla zostały dodane.");

                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Success.Title"),
                    string.Format(_localizationService.Get("UI.Repair.Firewall.Success"), selectedMod.Name));
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Użytkownik anulował UAC prompt
                System.Diagnostics.Debug.WriteLine("[FixFirewall] Użytkownik anulował prompt UAC.");
                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Info.Title"),
                    _localizationService.Get("UI.Repair.Cancelled"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FixFirewall] Błąd: {ex.Message}");
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Firewall.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        private void ShowRoles()
        {
            if (SelectedMod != null)
            {
                // SelectedMod.Id to ID moda z config.json
                // Przekazujemy to samo ID jako configId i modId
                var rolesWindow = new RolesWindow(SelectedMod.Id, SelectedMod.Id, SelectedMod.Name);
                rolesWindow.Show();
            }
        }

        #endregion

        #region File & Folder Operations

        private void OpenFolder()
        {
            if (SelectedMod?.InstallPath != null && Directory.Exists(SelectedMod.InstallPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SelectedMod.InstallPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Nie udało się otworzyć folderu: {ex.Message}");
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await ShowErrorDialogAsync($"Nie udało się otworzyć folderu: {ex.Message}", "Błąd");
                    });
                }
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync("Folder instalacji nie istnieje lub mod nie jest zainstalowany.", "Błąd");
                });
            }
        }

        private void CreateShortcut()
        {
            if (SelectedMod?.InstallPath != null && Directory.Exists(SelectedMod.InstallPath))
            {
                try
                {
                    // Uwzględnij strukturę Epic (podkatalog AmongUs)
                    string actualModPath = PathSettings.GetActualModPath(SelectedMod.InstallPath);
                    string amongUsExePath = Path.Combine(actualModPath, "Among Us.exe");
                    if (File.Exists(amongUsExePath))
                    {
                        // Sprawdź czy jesteśmy na Windows
                        if (OperatingSystem.IsWindows())
                        {
                            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            string shortcutPath = Path.Combine(desktopPath, $"{SelectedMod.Name}.lnk");

                            // Dla workingDirectory używamy actualModPath, aby skrót działał poprawnie
                            CreateWindowsShortcut(amongUsExePath, shortcutPath, actualModPath);
                        }
                        else
                        {
                            // Na innych platformach możesz pokazać komunikat lub zaimplementować inne rozwiązanie
                            System.Diagnostics.Debug.WriteLine("Shortcut creation is only supported on Windows");
                            // Opcjonalnie: pokaż komunikat użytkownikowi
                            _ = ShowMessageAsync("Informacja", "Tworzenie skrótów jest dostępne tylko na Windows.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Nie udało się utworzyć skrótu: {ex.Message}");
                }
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async void CreateWindowsShortcut(string targetPath, string shortcutPath, string workingDirectory)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    await ShowErrorDialogAsync("Nie można uzyskać dostępu do WScript.Shell", "Błąd");
                    return;
                }

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    await ShowErrorDialogAsync("Nie można utworzyć instancji WScript.Shell", "Błąd");
                    return;
                }

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.Description = $"Skrót do {Path.GetFileNameWithoutExtension(targetPath)}";
                shortcut.Save();

                System.Diagnostics.Debug.WriteLine($"Shortcut created: {shortcutPath}");

                // Dialog sukcesu
                await ShowMessageAsync("Sukces", $"Skrót został utworzony na pulpicie:\n{Path.GetFileName(shortcutPath)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating Windows shortcut: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas tworzenia skrótu: {ex.Message}", "Błąd");
            }
        }

        #endregion
    }
}

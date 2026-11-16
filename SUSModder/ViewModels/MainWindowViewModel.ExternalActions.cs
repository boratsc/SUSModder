using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;
using SUSModder.Core.Services.Localization;
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
            IsInfoPanelVisible = false;
            IsAdditionalActionsVisible = false;
            IsDllModificationsVisible = false;
            IsSUStatsConfigVisible = false;
            IsAppSettingsVisible = false;
            IsDllInstallDialogVisible = false;
            IsRecommendedDiscordsVisible = true;
            IsRepairOptionsVisible = false;
            CloseDllSelectionModal();
            SelectedMod = null;
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
                IsRecommendedDiscordsVisible = false;
                IsRepairOptionsVisible = false;
                CloseDllSelectionModal();
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

        private async Task ExecuteFixBlackScreenAsync()
        {
            try
            {
                IsPaneOpen = false;

                string platform = DeterminePlatform().ToLower();
                IsRepairSteamPlatform = platform == "steam";

                IsInfoPanelVisible = false;
                IsAdditionalActionsVisible = false;
                IsDllModificationsVisible = false;
                IsSUStatsConfigVisible = false;
                IsAppSettingsVisible = false;
                IsRecommendedDiscordsVisible = false;
                IsDllInstallDialogVisible = false;
                IsRepairOptionsVisible = true;
                CloseDllSelectionModal();
                SelectedMod = null;

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Dialog błędu na UI thread
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        private async Task ExecuteRepairOptionFromModalAsync(string optionKey)
        {
            if (!Enum.TryParse<RepairOption>(optionKey, ignoreCase: true, out var selectedOption))
            {
                return;
            }

            try
            {
                IsRepairOptionsVisible = false;
                await ExecuteRepairOptionAsync(selectedOption);
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
        }

        private async Task ExecuteRepairOptionAsync(RepairOption selectedOption)
        {
            switch (selectedOption)
            {
                case RepairOption.BlackScreen:
                    var confirmResult = await ShowConfirmDialogAsync(
                        _localizationService.Get("UI.Repair.BlackScreen.ConfirmMessage"),
                        _localizationService.Get("Dialogs.Confirm.Title"));

                    if (!confirmResult)
                    {
                        return;
                    }

                    await Task.Run(() => FixBlackScreen.ExecuteFixCore());

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

                case RepairOption.SteamLoginCheck:
                    await ExecuteSteamLoginCheckRepairAsync();
                    break;

                case RepairOption.EpicLogout:
                    await ExecuteEpicLogoutAsync();
                    break;

                case RepairOption.EpicLogin:
                    await ExecuteEpicLoginAsync();
                    break;

                case RepairOption.EnvironmentCheck:
                    await ShowEnvironmentCheckReportAsync();
                    break;

                case RepairOption.None:
                default:
                    break;
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
                            FormatCertutilFailureMessage(process.ExitCode));
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
                        var importError = CertificateRepairErrorMapper.Classify(process.ExitCode);
                        throw new InvalidOperationException(
                            string.Format(
                                _localizationService.Get("UI.Repair.Certificates.ImportError"),
                                $"{importError.HexCode} ({importError.ExitCode})"));
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

        private string FormatCertutilFailureMessage(int exitCode)
        {
            var classified = CertificateRepairErrorMapper.Classify(exitCode);
            var detailKey = CertificateRepairErrorMapper.GetDetailLocalizationKey(classified.Kind);
            var detail = _localizationService.Get(detailKey);

            if (string.IsNullOrWhiteSpace(detail) || detail == detailKey)
                detail = _localizationService.Get("UI.Repair.Certificates.Errors.Unknown");

            return string.Format(
                _localizationService.Get("UI.Repair.Certificates.CertutilError"),
                detail,
                classified.HexCode,
                classified.TechnicalName);
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
            string? scriptPath = null;
            string? outPath = null;
            try
            {
                // Kandydaci: vanilla bieżącego trybu + zainstalowane mody full.
                var settings = _userSettingsService.LoadUserSettings();
                var mode = DeterminePlatform();
                string? steamVanillaDir = null;
                if (!string.Equals(mode, "epic", StringComparison.OrdinalIgnoreCase))
                {
                    steamVanillaDir = AmongUsPathDiscovery.IsValidInstallDirectory(settings.VanillaInstallPath)
                        ? settings.VanillaInstallPath
                        : GameLocator.TryFindSteamPath();
                }

                var candidates = FirewallToolPlan.BuildCandidates(
                    mode,
                    steamVanillaDir,
                    PathSettings.ModsInstallPath,
                    Mods.Where(m => !string.IsNullOrEmpty(m.InstallPath) && m.ModType == "full")
                        .Select(m => (m.Name, m.InstallPath)),
                    _localizationService.Get("UI.Repair.Firewall.VanillaEntry"),
                    File.Exists);

                if (candidates.Count == 0)
                {
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Info.Title"),
                        _localizationService.Get("UI.Repair.Firewall.NoModsInstalled"));
                    return;
                }

                // Pokaż dialog wyboru (jawny wybór + potwierdzenie zostają).
                var dialog = new FirewallModSelectionDialog(candidates);
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

                var selected = dialog.SelectedMod;
                if (selected == null)
                {
                    return; // Anulowano
                }

                string targetExePath = selected.ExePath;
                if (!FirewallRuleService.IsValidProgramPath(targetExePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[FixFirewall] Nieprawidłowa ścieżka programu: {targetExePath}");
                    await ShowErrorDialogAsync(
                        _localizationService.Get("UI.Repair.Firewall.InvalidPath"),
                        _localizationService.Get("Dialogs.Error.Title"));
                    return;
                }

                // Steam: exe musi istnieć. Epic: predykcja dopuszczona (IsPredicted).
                if (!selected.IsPredicted && !File.Exists(targetExePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[FixFirewall] Nie znaleziono pliku gry: {targetExePath}");
                    await ShowErrorDialogAsync(
                        _localizationService.Get("UI.Repair.Firewall.ExeNotFound"),
                        _localizationService.Get("Dialogs.Error.Title"));
                    return;
                }

                // Potwierdzenie
                var confirmResult = await ShowConfirmDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Firewall.ConfirmMessage"), selected.Name),
                    _localizationService.Get("Dialogs.Confirm.Title"));

                if (!confirmResult)
                    return;

                // Vanilla → "Among Us - Vanilla" (spójne z checkiem firewall.rules w Core).
                string ruleBaseName = FirewallRuleService.BuildRuleBaseName(selected.IsVanilla ? "Vanilla" : selected.Name);

                // Ownership pre-check bez elewacji — nie usuwamy cudzych reguł.
                var inboundOutput = await FirewallRuleService.TryShowRuleVerboseAsync(ruleBaseName, "Inbound");
                var outboundOutput = await FirewallRuleService.TryShowRuleVerboseAsync(ruleBaseName, "Outbound");
                var decision = FirewallToolPlan.Decide(inboundOutput, outboundOutput, targetExePath);
                if (decision == FirewallToolDecision.Conflict)
                {
                    System.Diagnostics.Debug.WriteLine($"[FixFirewall] Konflikt nazwy reguły: {ruleBaseName}");
                    await ShowFirewallNameConflictAsync();
                    return;
                }

                // Dane dynamiczne (nazwa, ścieżka) wyłącznie w walidowanym skrypcie;
                // w linii cmd tylko nasze stałe ścieżki tymczasowe. Jedna elewacja.
                var script = FirewallRuleService.BuildNetshScript(
                    new FirewallRuleRequest(ruleBaseName, targetExePath),
                    deleteOwnFirst: decision == FirewallToolDecision.Replace);
                var tempBaseName = Path.Combine(Path.GetTempPath(), "susmodder-firewall-" + Guid.NewGuid().ToString("N"));
                scriptPath = tempBaseName + ".txt";
                outPath = tempBaseName + ".out";
                // Acceptance 2026-11-16 (Win11 26300 PL): netsh -f przyjmuje skrypt wyłącznie jako UTF-8 bez BOM
                // (UTF-16 LE BOM = "command not found", OEM = zniekształcone polskie znaki).
                File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

                System.Diagnostics.Debug.WriteLine($"[FixFirewall] Wykonanie skryptu dla: {targetExePath} (decision={decision})");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netsh -f \"{scriptPath}\" > \"{outPath}\" 2>&1",
                    UseShellExecute = true,
                    Verb = "runas", // Wymaga uprawnień administratora
                    CreateNoWindow = true
                };

                var process = Process.Start(processInfo);
                if (process == null)
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("UI.Repair.Firewall.ProcessError"),
                        _localizationService.Get("Dialogs.Error.Title"));
                    return;
                }

                using (process)
                {
                    await Task.Run(() => process.WaitForExit());
                }

                // Wynik uczciwie z odczytu reguł, nie z kodu wyjścia cmd.
                var outcome = FirewallRuleService.ParseScriptOutput(
                    FirewallToolPlan.ReadOutputFile(outPath), targetExePath);
                var messageKey = FirewallToolPlan.OutcomeMessageKey(outcome);
                if (messageKey == "UI.Repair.Firewall.NameConflict")
                {
                    await ShowFirewallNameConflictAsync();
                    return;
                }

                var titleKey = messageKey == "UI.Repair.Firewall.Success"
                    ? "Dialogs.Success.Title"
                    : "Dialogs.Info.Title";
                await ShowMessageAsync(
                    _localizationService.Get(titleKey),
                    _localizationService.Get(messageKey));
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
                System.Diagnostics.Debug.WriteLine($"[FixFirewall] Błąd: {ex}");
                await ShowErrorDialogAsync(
                    _localizationService.Get("UI.Repair.Firewall.Error"),
                    _localizationService.Get("Dialogs.Error.Title"));
            }
            finally
            {
                foreach (var tempFile in new[] { scriptPath, outPath })
                {
                    if (tempFile == null)
                        continue;
                    try
                    {
                        if (File.Exists(tempFile))
                            File.Delete(tempFile);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FixFirewall] Nie udało się usunąć pliku tymczasowego: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Konflikt nazwy pozwolenia: instrukcja 1-2-3 + jeden przycisk akcji (istniejący wzorzec
        /// ShowConfirmDialogAsync z przyciskami, jak „Otwórz Steam" przy wersji beta).
        /// </summary>
        private async Task ShowFirewallNameConflictAsync()
        {
            var openFirewall = await ShowConfirmDialogAsync(
                _localizationService.Get("UI.Repair.Firewall.NameConflict"),
                _localizationService.Get("Dialogs.Info.Title"),
                _localizationService.Get("UI.Repair.Firewall.NameConflictOpenFirewall"),
                _localizationService.Get("UI.Buttons.Close"));

            if (!openFirewall)
                return;

            try
            {
                Process.Start(new ProcessStartInfo("wf.msc") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FixFirewall] Nie udało się otworzyć Zapory Windows: {ex.Message}");
                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Info.Title"),
                    _localizationService.Get("UI.Repair.Firewall.NameConflictOpenError"));
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

        private void OpenVirusTotalReport()
        {
            if (SelectedMod == null)
                return;

            var permalink = SelectedMod.VtPermalink;
            if (string.IsNullOrWhiteSpace(permalink))
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("SecurityScan.NoReportAvailable"),
                        _localizationService.Get("Dialogs.Error.Title"));
                });
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = permalink,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VT] Nie udało się otworzyć raportu: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync(
                        string.Format(_localizationService.Get("SecurityScan.OpenReportError"), ex.Message),
                        _localizationService.Get("Dialogs.Error.Title"));
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

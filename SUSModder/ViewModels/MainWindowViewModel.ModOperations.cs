using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
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
        private ModManagerUserCallbacks CreateModManagerCallbacks() => new()
        {
            ConfirmAsync = _userInteractionService.ShowConfirmAsync,
            ShowErrorAsync = async (message, title) =>
            {
                var mapped = MapDownloadVerificationMessage(message);
                await _userInteractionService.ShowErrorAsync(mapped ?? message, title);
            },
            ShowInfoAsync = _userInteractionService.ShowInfoAsync,
            RunSteamQrDownloadAsync = _userInteractionService.RunSteamQrDownloadAsync
        };

        #region Install

        private async void Install()
        {
            if (_isInitializing)
                return;

            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            await InstallModItemAsync(SelectedMod, showPostInstallFlow: true);
        }

        /// <summary>
        /// Instalacja pojedynczego moda (używane także przez kolejkę bulk).
        /// </summary>
        internal async Task<bool> InstallModItemAsync(ModItem currentSelectedMod, bool showPostInstallFlow = true)
        {
            if (currentSelectedMod.IsInstalling)
                return false;

            // Zamknij pozostawione wcześniej panele z poprzedniej akcji, aby
            // użytkownik nie widział dwóch nakładających się modalnych paneli.
            if (IsPostInstallSuccessVisible || IsPostInstallFailureVisible || IsLaunchDiagnosticsVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsPostInstallSuccessVisible = false;
                    PostInstallSuccessViewModel = null;
                    IsPostInstallFailureVisible = false;
                    PostInstallFailureViewModel = null;
                    IsLaunchDiagnosticsVisible = false;
                });
            }

            bool success = false;
            ModConfiguration? modConfig = null;
            ModInstallResult? installResult = null;

            lock (_installationLock)
            {
                _activeInstallationsCount++;
                System.Diagnostics.Debug.WriteLine($"[Install] Rozpoczęto instalację {currentSelectedMod.Name}. Aktywnych instalacji: {_activeInstallationsCount}");
            }
            SyncIsAnyModInstalling();

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.IsInstalling = true;
                    currentSelectedMod.ShowProgress = true;
                });

                var configService = new ConfigService();
                var allConfigs = configService.LoadConfig();
                modConfig = allConfigs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                if (modConfig == null)
                {
                    await _userInteractionService.ShowErrorAsync(
                        _localizationService.Get("ModOperations.ConfigNotFound"),
                        _localizationService.Get("MainWindow.ErrorTitle"));
                    return false;
                }

                string platform = DeterminePlatform();

                // ── VirusTotal security gate (czyta z DB, nie woła API) ──
                if (!currentSelectedMod.IsVanilla && currentSelectedMod.IsVtRisky)
                {
                    string warningTitle = _localizationService.Get("SecurityScan.InstallWarningTitle");
                    string warningMessage = string.Format(
                        _localizationService.Get("SecurityScan.WarningIntro"),
                        currentSelectedMod.Name, modConfig.ModVersion);
                    warningMessage += "\n\n";
                    warningMessage += string.Format(
                        _localizationService.Get("SecurityScan.WarningStatus"),
                        currentSelectedMod.VtScanStatus ?? "unknown");
                    if (!string.IsNullOrWhiteSpace(currentSelectedMod.VtAiReviewSummary))
                    {
                        warningMessage += "\n" + string.Format(
                            _localizationService.Get("SecurityScan.WarningAiReview"),
                            currentSelectedMod.VtAiReviewSummary);
                    }
                    if (!string.IsNullOrWhiteSpace(currentSelectedMod.VtPermalink))
                    {
                        warningMessage += "\n\n" + string.Format(
                            _localizationService.Get("SecurityScan.WarningPermalink"),
                            currentSelectedMod.VtPermalink);
                    }

                    bool proceed = await ShowConfirmDialogAsync(warningMessage, warningTitle);
                    if (!proceed)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Install] Użytkownik anulował instalację moda {currentSelectedMod.Name} z powodu ostrzeżenia VT.");
                        return false;
                    }
                }
                // ── Koniec security gate ──

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    installResult = await InstallEpicModAsync(currentSelectedMod, modConfig);
                    success = installResult.Success;
                }
                else
                {
                    installResult = await InstallSteamModAsync(currentSelectedMod, modConfig, allConfigs);
                    success = installResult.Success;
                }

                if (success)
                {
                    await PersistInstalledVersionStateAsync(
                        currentSelectedMod.Id,
                        modConfig.ModVersion,
                        disableAutoUpdatePrompt: false,
                        pinnedInstallVersion: null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Install] Exception: {ex.Message}");
                installResult = ModInstallResult.Failed(ex.Message);
                success = false;
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.ShowProgress = false;
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;
                    currentSelectedMod.DownloadSpeed = null;
                    currentSelectedMod.IsInstalling = false;
                });

                lock (_installationLock)
                {
                    _activeInstallationsCount--;
                    System.Diagnostics.Debug.WriteLine($"[Install] Zakończono instalację {currentSelectedMod.Name}. Aktywnych instalacji: {_activeInstallationsCount}");
                }
                SyncIsAnyModInstalling();

                await ShowPendingDllDialogsIfNeeded();
                await RefreshStatusBarAsync();
            }

            if (success && showPostInstallFlow && modConfig != null)
                await ShowPostInstallFlowAsync(currentSelectedMod, modConfig);
            else if (!success)
                await ShowPostInstallFailureFlowAsync(currentSelectedMod, installResult);

            return success;
        }

        /// <summary>
        /// Instalacja moda z wyborem wersji - pokazuje dialog wyboru wersji przed instalacją
        /// </summary>
        private async Task InstallWithVersionSelection()
        {
            if (_isInitializing)
                return;

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
                    await _userInteractionService.ShowErrorAsync(
                        _localizationService.Get("ModOperations.ConfigNotFound"),
                        _localizationService.Get("MainWindow.ErrorTitle"));
                    return;
                }

                // Pobierz konfigurację aplikacji
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                // Utwórz ViewModel dialogu
                var versionSelectionVM = new VersionSelectionViewModel(modConfig, configuration, _localizationService);

                var selectedVersion = await ShowVersionSelectionModalAsync(versionSelectionVM);

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
                await _userInteractionService.ShowErrorAsync(
                    _localizationService.GetFormatted("ModOperations.InstallError", ex.Message),
                    _localizationService.Get("MainWindow.ErrorTitle"));
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
            SyncIsAnyModInstalling();

            bool success = false;
            ModInstallResult? installResult = null;

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

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    installResult = await InstallEpicModAsync(modItem, tempModConfig);
                    success = installResult.Success;
                }
                else
                {
                    installResult = await InstallSteamModAsync(modItem, tempModConfig, allConfigs);
                    success = installResult.Success;
                }

                if (success)
                {
                    bool installedOlderThanLatest = !string.Equals(
                        selectedVersion.ModVersion,
                        modConfig.ModVersion,
                        StringComparison.OrdinalIgnoreCase);

                    await PersistInstalledVersionStateAsync(
                        modItem.Id,
                        selectedVersion.ModVersion,
                        disableAutoUpdatePrompt: installedOlderThanLatest,
                        pinnedInstallVersion: installedOlderThanLatest ? selectedVersion.ModVersion : null);

                    await RefreshModsListAsync(checkUpdates: false);
                    SelectedMod = Mods.FirstOrDefault(m => m.Id == modItem.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InstallSpecificVersion] Exception: {ex.Message}");
                installResult = ModInstallResult.Failed(ex.Message);
                success = false;
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
                SyncIsAnyModInstalling();

                // Jeśli to była ostatnia instalacja, pokaż wszystkie oczekujące dialogi DLL
                await ShowPendingDllDialogsIfNeeded();

                // Odśwież statystyki status bara
                await RefreshStatusBarAsync();
            }

            if (success)
                await ShowPostInstallFlowAsync(modItem, modConfig);
            else
                await ShowPostInstallFailureFlowAsync(modItem, installResult);
        }

        private async Task<ModInstallResult> InstallEpicModAsync(ModItem currentSelectedMod, ModConfiguration modConfig)
        {
            var diagnosticsOutput = new BufferingDiagnosticsOutput(new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Install Epic] {message}");
            }));

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

            // Subskrybuj prędkość pobierania z legendary
            epicManager.SpeedChanged += (speed) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.DownloadSpeed = speed;
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
                var epicResult = await epicManager.ModifyEpicAsync(modConfig, null, null);
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync returned {epicResult}");

                return epicResult
                    ? ModInstallResult.Succeeded(diagnosticsOutput.Lines)
                    : ModInstallResult.Failed(
                        MapDownloadVerificationMessage(epicManager.LastFailureCode)
                            ?? _localizationService.Get("Dialogs.Error.InstallFailed"),
                        diagnosticsOutput.Lines);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync threw exception: {ex.Message}");
                diagnosticsOutput.Write($"[Install Epic] Exception: {ex.Message}");
                return ModInstallResult.Failed(
                    MapDownloadVerificationMessage(ex.Message)
                        ?? MapDownloadVerificationMessage(epicManager.LastFailureCode)
                        ?? ex.Message,
                    diagnosticsOutput.Lines);
            }
        }

        private string? MapDownloadVerificationMessage(string? codeOrMessage)
        {
            if (string.IsNullOrWhiteSpace(codeOrMessage))
                return null;

            return codeOrMessage switch
            {
                DownloadVerificationCodes.HashMismatch =>
                    _localizationService.Get("Security.DownloadHashMismatch"),
                DownloadVerificationCodes.HashMissing =>
                    _localizationService.Get("Security.DownloadHashMissing"),
                DownloadVerificationCodes.ToolHashMismatch =>
                    _localizationService.Get("Security.ToolHashMismatch"),
                DownloadVerificationCodes.ToolDownloadFailed =>
                    _localizationService.Get("Security.ToolDownloadFailed"),
                DownloadVerificationCodes.ArtifactVerificationFailed =>
                    _localizationService.Get("Security.ArtifactVerificationFailed"),
                _ => null
            };
        }

        private async Task<ModInstallResult> InstallSteamModAsync(
            ModItem currentSelectedMod,
            ModConfiguration modConfig,
            System.Collections.Generic.List<ModConfiguration> allConfigs)
        {
            var progressReporter = new UIProgressReporter((percentage, message) =>
            {
                currentSelectedMod.InstallProgress = percentage;
                currentSelectedMod.InstallStatusMessage = message;
            });

            var logCollector = new BufferingDiagnosticsOutput(new UIDiagnosticsOutput(message =>
            {
                System.Diagnostics.Debug.WriteLine($"[Install Steam] {message}");
            }));

            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            var configuration = configBuilder.Build();

            var modManager = new ModManager(configuration);

            try
            {
                var callbacks = CreateModManagerCallbacks();

                var result = await modManager.ModifyAsync(
                    modConfig,
                    allConfigs,
                    progressReporter,
                    logCollector,
                    callbacks,
                    "steam",
                    onSpeedUpdate: (speed) =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            currentSelectedMod.DownloadSpeed = speed;
                        });
                    });

                if (!result.Success)
                {
                    return ModInstallResult.Failed(
                        result.ErrorMessage ?? _localizationService.Get("Dialogs.Error.InstallFailed"),
                        logCollector.Lines);
                }

                var installedConfig = allConfigs.FirstOrDefault(c => c.Id == modConfig.Id);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.InstallPath = installedConfig?.InstallPath ?? modConfig.InstallPath;
                });

                RefreshModsSortingKeepSelection(currentSelectedMod);
                return ModInstallResult.Succeeded(logCollector.Lines);
            }
            catch (Exception ex)
            {
                logCollector.Write($"[Install Steam] Exception: {ex.Message}");
                return ModInstallResult.Failed(ex.Message, logCollector.Lines);
            }
        }

        private async Task<ModVersionHistory?> ShowVersionSelectionModalAsync(VersionSelectionViewModel versionSelectionVM)
        {
            var completion = new TaskCompletionSource<ModVersionHistory?>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<ModVersionHistory?>? onVersionSelected = null;
            EventHandler? onCancelled = null;

            onVersionSelected = (_, version) => completion.TrySetResult(version);
            onCancelled = (_, _) => completion.TrySetResult(null);

            versionSelectionVM.VersionSelected += onVersionSelected;
            versionSelectionVM.Cancelled += onCancelled;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsInfoPanelVisible = false;
                IsAdditionalActionsVisible = false;
                IsDllModificationsVisible = false;
                IsSUStatsConfigVisible = false;
                IsAppSettingsVisible = false;
                IsRecommendedDiscordsVisible = false;
                IsRepairOptionsVisible = false;
                IsDllInstallDialogVisible = false;
                IsDllSelectionModalVisible = false;

                VersionSelectionModalViewModel = versionSelectionVM;
                IsVersionSelectionModalVisible = true;
            });

            var selectedVersion = await completion.Task;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsVersionSelectionModalVisible = false;
                VersionSelectionModalViewModel = null;
                this.RaisePropertyChanged(nameof(IsModPanelVisible));
            });

            versionSelectionVM.VersionSelected -= onVersionSelected;
            versionSelectionVM.Cancelled -= onCancelled;
            versionSelectionVM.Dispose();

            return selectedVersion;
        }

        private void ShowDllSelectionFromSelectedMod()
        {
            if (SelectedMod == null || string.IsNullOrEmpty(SelectedMod.InstallPath))
            {
                return;
            }

            string platform = DeterminePlatform().ToLower();
            ShowDllSelectionWindow(SelectedMod, platform);
        }

        private void ShowDllSelectionWindow(ModItem mod, string platform)
        {
            lock (_installationLock)
            {
                if (_activeInstallationsCount > 0 || IsDllSelectionModalVisible)
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
            
            // Załaduj świeżą konfigurację z pliku, aby mieć aktualne InstallPath
            var allConfigs = ConfigManager.LoadConfig();
            var targetModConfig = allConfigs.FirstOrDefault(c => c.Id == mod.Id);
            
            if (targetModConfig == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DLL Dialog] ⚠️ Nie znaleziono konfiguracji dla moda {mod.Name} (ID: {mod.Id})");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[DLL Dialog] Załadowano konfigurację: InstallPath = {targetModConfig.InstallPath}");

            // Uwaga: nie wołamy CloseDllSelectionModal() tutaj, ponieważ
            // modal nie jest widoczny w momencie wywołania — ShowDllSelectionWindowInternal
            // jest zawsze wołane gdy IsDllSelectionModalVisible == false
            // (z queue lub z ShowDllSelectionWindow z guardem). Wywołanie
            // CloseDllSelectionModal() powodowało flashowanie UI (najpierw false, potem true)
            // i w niektórych przypadkach dialog zamykał się natychmiast.

            var dllSelectionVm = new DllModSelectionViewModel(
                _dllModificationService,
                targetModConfig, // Użyj świeżo załadowanej konfiguracji zamiast konwersji z ModItem
                platform,
                _configuration, // Przekaż konfigurację dla CompatibilityService
                _diagnosticsOutput // Przekaż diagnostykę dla CompatibilityService
            );
            dllSelectionVm.CloseRequested += OnDllSelectionCloseRequested;
            DllSelectionModalViewModel = dllSelectionVm;

            IsPaneOpen = false;
            IsInfoPanelVisible = false;
            IsAdditionalActionsVisible = false;
            IsDllModificationsVisible = false;
            IsSUStatsConfigVisible = false;
            IsAppSettingsVisible = false;
            IsRecommendedDiscordsVisible = false;
            IsRepairOptionsVisible = false;
            IsDllInstallDialogVisible = false;
            IsDllSelectionModalVisible = true;

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
            System.Diagnostics.Debug.WriteLine($"DEBUG {platform} Path: {targetModConfig.InstallPath}");
        }

        /// <summary>
        /// Sprawdza czy dialog poinstalacyjny ma być pominięty (flag DontShowPostInstallDialog).
        /// </summary>
        private async Task<bool> IsPostInstallDialogSuppressedAsync(ModItem modItem)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modItem.InstallPath))
                    return false;

                var installMap = await InstallationMapManager.LoadInstallationMapAsync(modItem.InstallPath);
                return installMap?.FullMod?.DontShowPostInstallDialog ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Współdzielony flow poinstalacyjny: odświeża listę, pokazuje modal sukcesu i opcjonalnie toast.
        /// Toast pokazywany TYLKO gdy dialog jest pominięty (DontShowAgain).
        /// </summary>
        private async Task ShowPostInstallFlowAsync(ModItem modItem, ModConfiguration? modConfig)
        {
            await RefreshModsListAsync(checkUpdates: false);
            SelectedMod = Mods.FirstOrDefault(m => m.Id == modItem.Id);
            if (SelectedMod != null)
                IsModContentVisible = true;

            string platform = DeterminePlatform();
            bool supportsDll = !string.IsNullOrEmpty(modConfig?.DllInstallPath);
            bool dialogSuppressed = await IsPostInstallDialogSuppressedAsync(modItem);

            if (dialogSuppressed)
            {
                // Toast tylko gdy dialog pominięty — bo modal sam w sobie jest powiadomieniem
                ToastService.ShowSuccess(
                    _localizationService.GetFormatted("Toast.ModInstalled", modItem.Name),
                    _localizationService.GetFormatted("Toast.ModInstalledDesc", modItem.ModVersion));
                return;
            }

            // Pokaż modal inline (jak DLL selection, a nie osobne okienko)
            bool isPinnedVersion = SelectedMod?.IsPinnedVersionInstall == true;
            var vm = new PostInstallSuccessViewModel(
                modItem.Name,
                modItem.Id,
                modItem.ModVersion,
                supportsDll,
                _localizationService,
                defaultAutoUpdateEnabled: !isPinnedVersion,
                showAutoUpdateCheckbox: !isPinnedVersion);
            vm.CloseRequested += OnPostInstallSuccessCloseRequested;
            vm.ChangelogRequested += OnPostInstallChangelogRequested;
            IsLaunchDiagnosticsVisible = false;
            IsPostInstallFailureVisible = false;
            PostInstallFailureViewModel = null;
            PostInstallSuccessViewModel = vm;
            IsPostInstallSuccessVisible = true;
        }

        private async Task ShowPostInstallFailureFlowAsync(ModItem modItem, ModInstallResult? installResult)
        {
            await RefreshModsListAsync(checkUpdates: false);
            SelectedMod = Mods.FirstOrDefault(m => m.Id == modItem.Id) ?? modItem;
            if (SelectedMod != null)
                IsModContentVisible = true;

            var errorMessage = installResult?.ErrorMessage
                ?? _localizationService.Get("Dialogs.Error.InstallFailed");
            var logText = installResult?.GetLogText() ?? string.Empty;

            var vm = new PostInstallFailureViewModel(
                modItem.Name,
                errorMessage,
                logText,
                _localizationService);
            vm.CloseRequested += OnPostInstallFailureCloseRequested;
            vm.AiSupportRequested += OnPostInstallFailureAiSupportRequested;
            IsLaunchDiagnosticsVisible = false;
            IsPostInstallSuccessVisible = false;
            PostInstallSuccessViewModel = null;
            PostInstallFailureViewModel = vm;
            IsPostInstallFailureVisible = true;
        }

        private void OnPostInstallFailureAiSupportRequested(object? sender, EventArgs e)
        {
            if (sender is not PostInstallFailureViewModel vm)
                return;

            vm.AiSupportRequested -= OnPostInstallFailureAiSupportRequested;
            vm.CloseRequested -= OnPostInstallFailureCloseRequested;

            IsPostInstallFailureVisible = false;
            PostInstallFailureViewModel = null;
            ShowAiSupportForInstallFailure(vm.ModName, vm.Message, vm.LogText);
        }

        private void OnPostInstallFailureCloseRequested(object? sender, EventArgs e)
        {
            if (sender is PostInstallFailureViewModel vm)
            {
                vm.AiSupportRequested -= OnPostInstallFailureAiSupportRequested;
                vm.CloseRequested -= OnPostInstallFailureCloseRequested;
                IsPostInstallFailureVisible = false;
                PostInstallFailureViewModel = null;

                if (SelectedMod != null)
                    IsModContentVisible = true;
            }
        }

        private void OnPostInstallChangelogRequested(object? sender, EventArgs e)
        {
            if (sender is PostInstallSuccessViewModel vm)
            {
                // Otw�rz modal changeloga dla zainstalowanego moda
                _ = OpenModChangelogAsync();
            }
        }

        private void OnPostInstallSuccessCloseRequested(object? sender, EventArgs e)
        {
            if (sender is PostInstallSuccessViewModel vm)
            {
                vm.CloseRequested -= OnPostInstallSuccessCloseRequested;
                vm.ChangelogRequested -= OnPostInstallChangelogRequested;

                // Zapisz flag� "Nie pokazuj wi�cej" jeśli zaznaczona
                if (vm.DontShowAgain && SelectedMod != null && !string.IsNullOrWhiteSpace(SelectedMod.InstallPath))
                {
                    _ = SaveDontShowPostInstallDialogAsync(SelectedMod.InstallPath);
                }

                if (vm.IsAutoUpdateCheckboxVisible && SelectedMod != null)
                {
                    _ = ToggleAutoUpdateAsync(SelectedMod, vm.AutoUpdateEnabled);
                }

                // Ukryj modal
                IsPostInstallSuccessVisible = false;
                PostInstallSuccessViewModel = null;

                // Wykonaj wybraną akcję
                if (vm.Result == PostInstallAction.Launch)
                {
                    _ = LaunchAsync();
                }
                else if (vm.Result == PostInstallAction.AddDll)
                {
                    string platform = DeterminePlatform();
                    ShowDllSelectionWindowInternal(SelectedMod!, platform);
                }
                else if (SelectedMod != null)
                {
                    IsModContentVisible = true;
                }
            }
        }

        /// <summary>
        /// Zapisuje flagę DontShowPostInstallDialog w installation-map.json.
        /// </summary>
        private async Task SaveDontShowPostInstallDialogAsync(string installPath)
        {
            try
            {
                var installMap = await InstallationMapManager.LoadInstallationMapAsync(installPath);
                if (installMap?.FullMod != null)
                {
                    installMap.FullMod.DontShowPostInstallDialog = true;
                    installMap.FullMod.LastUpdated = DateTime.Now;
                    await InstallationMapManager.SaveInstallationMapAsync(installPath, installMap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostInstall] Błąd podczas zapisywania flagi: {ex.Message}");
            }
        }

        private async Task PersistInstalledVersionStateAsync(
            int modId,
            string? installedVersion,
            bool disableAutoUpdatePrompt,
            string? pinnedInstallVersion)
        {
            try
            {
                var configService = new ConfigService();
                var configs = configService.LoadConfig();
                var targetConfig = configs.FirstOrDefault(c => c.Id == modId);

                if (targetConfig == null || string.IsNullOrWhiteSpace(targetConfig.InstallPath))
                {
                    return;
                }

                var installMap = await InstallationMapManager.LoadInstallationMapAsync(targetConfig.InstallPath);
                if (installMap?.FullMod == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(installedVersion))
                {
                    installMap.FullMod.ModVersion = installedVersion;
                }

                installMap.FullMod.DisableAutoUpdatePrompt = disableAutoUpdatePrompt;
                installMap.FullMod.PinnedInstallVersion = disableAutoUpdatePrompt ? pinnedInstallVersion : null;
                installMap.FullMod.AutoUpdateEnabled = !disableAutoUpdatePrompt;
                installMap.FullMod.LastUpdated = DateTime.Now;

                await InstallationMapManager.SaveInstallationMapAsync(targetConfig.InstallPath, installMap);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Install] Nie udało się zapisać stanu wersji moda: {ex.Message}");
            }
        }

        private void OnDllSelectionCloseRequested(object? sender, EventArgs e)
        {
            CloseDllSelectionModal();
            ShowNextQueuedDllSelectionIfNeeded();
        }

        private void CloseDllSelectionModal()
        {
            if (DllSelectionModalViewModel != null)
            {
                DllSelectionModalViewModel.CloseRequested -= OnDllSelectionCloseRequested;
                DllSelectionModalViewModel = null;
            }

            IsDllSelectionModalVisible = false;
            RestoreModDetailPanelAfterToolModal();
        }

        private void ShowNextQueuedDllSelectionIfNeeded()
        {
            (ModItem mod, string platform)? nextDialog = null;

            lock (_installationLock)
            {
                if (_activeInstallationsCount > 0 || _pendingDllDialogs.Count == 0 || IsDllSelectionModalVisible)
                {
                    return;
                }

                nextDialog = _pendingDllDialogs[0];
                _pendingDllDialogs.RemoveAt(0);
            }

            ShowDllSelectionWindowInternal(nextDialog.Value.mod, nextDialog.Value.platform);
        }

        private async Task ShowPendingDllDialogsIfNeeded()
        {
            await Dispatcher.UIThread.InvokeAsync(ShowNextQueuedDllSelectionIfNeeded);
        }

        #endregion

        #region Update

        private async void Update()
        {
            if (_isInitializing)
                return;

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
                    currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.NoUpdatesAvailable");
                    currentSelectedMod.InstallProgress = 100;
                    await Task.Delay(1500);
                    showNoUpdateMessage = true;

                    // Powiadomienie toast
                    ToastService.ShowInfo(
                        _localizationService.Get("Toast.AllModsUpToDate"));
                    return;
                }

                // 2. Rozpocznij aktualizację bez potwierdzenia (ręczna akcja użytkownika)
                currentSelectedMod.InstallProgress = 20;

                // 3. Aktualizuj konfigurację w pliku
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.UpdatingConfiguration");
                bool configUpdated = await configService.UpdateSingleModConfigAsync(updatedModConfig);

                if (!configUpdated)
                {
                    await ShowErrorDialogAsync(
                        _localizationService.Get("ModOperations.UpdateConfigFailed"),
                        _localizationService.Get("ModOperations.UpdateConfigError"));
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
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.UpdateComplete");

                // Odśwież sortowanie zachowując zaznaczenie
                RefreshModsSortingKeepSelection(currentSelectedMod);

                await Task.Delay(1500);
                updateSuccessful = true;
                successMessage = _localizationService.GetFormatted("ModOperations.ModUpdatedSuccess", currentSelectedMod.Name, updatedModConfig.ModVersion);

                // Powiadomienie toast
                ToastService.ShowSuccess(
                    _localizationService.GetFormatted("Toast.ModUpdated", currentSelectedMod.Name, updatedModConfig.ModVersion));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Exception: {ex.Message}");
                await ShowErrorDialogAsync(
                    _localizationService.GetFormatted("ModOperations.UpdateError", ex.Message),
                    _localizationService.Get("ModOperations.UpdateConfigError"));
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

                // Natychmiast wyzeruj licznik dostępnych aktualizacji – mod został zaktualizowany
                System.Diagnostics.Debug.WriteLine("[FAB-DEBUG] UpdateMod finally: setting AvailableUpdatesCount = 0");
                AvailableUpdatesCount = 0;
                
                // Odśwież natychmiastowo status dostępnych aktualizacji w status barze (force = true, pomija rate-limit)
                await CheckForModUpdatesForStatusBarAsync(force: true);
            }

            // Pokaż komunikaty po zakończeniu finally (poza blokiem try-finally)
            if (showNoUpdateMessage)
            {
                await ShowMessageAsync(
                    _localizationService.Get("ModOperations.InfoTitle"),
                    _localizationService.GetFormatted("ModOperations.AlreadyLatestVersion", currentSelectedMod.Name));
            }
            else if (updateSuccessful && successMessage != null)
            {
                await ShowMessageAsync(_localizationService.Get("ModOperations.SuccessTitle"), successMessage);
            }
        }

        private async Task ReinstallModAsync(ModItem currentSelectedMod, ConfigService configService, ModConfiguration updatedModConfig)
        {
            // UNINSTALL
            currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.UninstallingOldVersion");

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
            currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.InstallingNewVersion");

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
                    currentSelectedMod.InstallStatusMessage = _localizationService.GetFormatted("ModOperations.InstallingProgress", message);
                });

                // Diagnostics output
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Update-Install] {message}");
                });

                // Silent user interaction
                var silentUserInteraction = new InstallationSilentUserInteraction();

                // Sprawdź platformę
                var userSettings = _userSettingsService.LoadUserSettings();
                string platform = userSettings.Mode;

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
                        "steam"
                    );

                    if (!installResult.Success)
                        throw new InvalidOperationException(installResult.ErrorMessage ?? "Update failed");
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
            if (_isInitializing)
                return;

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
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.StartingUninstall");

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
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.DeletingFiles");

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
                        _localizationService.GetFormatted("ModOperations.IncompleteUninstall", currentSelectedMod.Name, currentSelectedMod.InstallPath),
                        _localizationService.Get("ModOperations.IncompleteUninstallTitle")
                    );

                    // Mimo niepełnego usunięcia, kontynuuj proces odinstalowania
                    System.Diagnostics.Debug.WriteLine($"OSTRZEŻENIE: Niepełne usunięcie katalogu: {currentSelectedMod.InstallPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Pomyślnie usunięto katalog: {currentSelectedMod.InstallPath}");
                }

                currentSelectedMod.InstallProgress = 75;
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.UpdatingConfig");

                // Aktualizuj konfigurację
                var configService = new ConfigService();
                var configs = configService.LoadConfig();
                var modConfig = configs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                if (modConfig != null)
                {
                    try
                    {
                        var remoteConfigs = await configService.LoadConfigFromApiAsync();
                        var remoteModConfig = remoteConfigs?.FirstOrDefault(c => c.Id == modConfig.Id);
                        if (remoteModConfig != null)
                        {
                            modConfig.ModVersion = remoteModConfig.ModVersion;
                            modConfig.AmongVersion = remoteModConfig.AmongVersion;
                            modConfig.Description = remoteModConfig.Description;
                            modConfig.GitHubRepoOrLink = remoteModConfig.GitHubRepoOrLink;
                            modConfig.EpicGitHubRepoOrLink = remoteModConfig.EpicGitHubRepoOrLink;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Uninstall] Nie udało się odświeżyć meta moda z API: {ex.Message}");
                    }

                    modConfig.InstallPath = string.Empty;
                    ConfigManager.SaveConfig(configs);
                }

                // Aktualizuj UI
                currentSelectedMod.InstallPath = string.Empty;
                currentSelectedMod.InstallProgress = 100;
                currentSelectedMod.InstallStatusMessage = _localizationService.Get("ModOperations.UninstallComplete");

                // Odśwież sortowanie bez utraty zaznaczenia
                RefreshModsSortingKeepSelection(currentSelectedMod);

                System.Diagnostics.Debug.WriteLine($"[Uninstall] SUCCESS: Odinstalowanie moda '{currentSelectedMod.Name}' zakończone pomyślnie");

                // Powiadomienie toast
                ToastService.ShowInfo(
                    _localizationService.GetFormatted("Toast.ModDeleted", currentSelectedMod.Name));
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

        /// <summary>
        /// Przełącza auto-aktualizację dla wybranego moda i zapisuje stan.
        /// </summary>
        public async Task ToggleAutoUpdateAsync(ModItem modItem, bool enabled)
        {
            try
            {
                if (modItem == null || string.IsNullOrWhiteSpace(modItem.InstallPath))
                    return;

                modItem.AutoUpdateEnabled = enabled;

                var installMap = await InstallationMapManager.LoadInstallationMapAsync(modItem.InstallPath);
                if (installMap?.FullMod != null)
                {
                    installMap.FullMod.AutoUpdateEnabled = enabled;
                    installMap.FullMod.LastUpdated = DateTime.Now;
                    await InstallationMapManager.SaveInstallationMapAsync(modItem.InstallPath, installMap);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoUpdate] Błąd podczas zapisywania ustawienia: {ex.Message}");
            }
        }

        #endregion
    }
}

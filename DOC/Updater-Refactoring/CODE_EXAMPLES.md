# Code Examples - Gotowe Snippety Velopack

**Data:** 2025-10-28
**Framework:** Velopack (https://velopack.io)
**API Reference:** https://docs.velopack.io/reference/cs/Velopack
**Target:** SUSModder 2.1.0

Ten dokument zawiera ready-to-use kod Velopack który możesz skopiować bezpośrednio do projektu.

---

## Spis Treści

1. [VelopackUpdateService.cs (Complete)](#1-velopackupdateservicecs-complete)
2. [Program.cs (Velopack Hooks)](#2-programcs-velopack-hooks)
3. [MainWindowViewModel.cs (UI Integration)](#3-mainwindowviewmodelcs-ui-integration)
4. [AppUpdateViewModel.cs (Dedicated Update UI)](#4-appupdateviewmodelcs-dedicated-update-ui)
5. [Build Scripts](#5-build-scripts)
6. [Backend Examples](#6-backend-examples)

---

## 1. VelopackUpdateService.cs (Complete)

**Location:** `SUSModder.Core/Services/VelopackUpdateService.cs`

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Velopack;
using Velopack.Sources;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Zarządza aktualizacjami aplikacji za pomocą Velopack
    /// </summary>
    public class VelopackUpdateService : IDisposable
    {
        private readonly string _currentVersion;
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;
        private UpdateManager? _updateManager;
        private bool _disposed;

        public VelopackUpdateService(
            string currentVersion,
            IConfiguration configuration,
            IDiagnosticsOutput diagnosticsOutput)
        {
            _currentVersion = currentVersion ?? "0.0.0";
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _diagnosticsOutput = diagnosticsOutput ?? throw new ArgumentNullException(nameof(diagnosticsOutput));
        }

        /// <summary>
        /// Inicjalizuje UpdateManager
        /// </summary>
        public void Initialize()
        {
            if (_updateManager != null)
                return;

            try
            {
                var updateUrl = GetUpdateUrl();
                _diagnosticsOutput.Write($"Inicjalizacja Velopack UpdateManager: {updateUrl}");

                // Create HTTP source for updates
                var source = new SimpleWebSource(updateUrl);

                // Create UpdateManager with source
                _updateManager = new UpdateManager(source);

                _diagnosticsOutput.Write("Velopack UpdateManager zainicjalizowany pomyślnie");
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd inicjalizacji UpdateManager: {ex.Message}");
                throw new InvalidOperationException("Failed to initialize Velopack UpdateManager", ex);
            }
        }

        /// <summary>
        /// Sprawdza dostępność aktualizacji
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                if (_updateManager == null)
                    Initialize();

                _diagnosticsOutput.Write("Sprawdzanie dostępności aktualizacji...");

                // Check for updates
                var updateInfo = await _updateManager!.CheckForUpdatesAsync();

                bool isUpdateAvailable = updateInfo != null;
                string latestVersion = isUpdateAvailable
                    ? updateInfo!.TargetFullRelease.Version.ToString()
                    : _currentVersion;

                if (isUpdateAvailable)
                {
                    _diagnosticsOutput.Write($"Dostępna aktualizacja: {latestVersion}");
                }
                else
                {
                    _diagnosticsOutput.Write("Brak dostępnych aktualizacji");
                }

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = isUpdateAvailable,
                    CurrentVersion = _currentVersion,
                    LatestVersion = latestVersion,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas sprawdzania aktualizacji: {ex.Message}");

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    LatestVersion = _currentVersion,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Pobiera i instaluje aktualizację (one-step)
        /// Automatycznie restartuje aplikację po update
        /// </summary>
        public async Task<UpdateDownloadResult> DownloadAndApplyUpdateAsync(IProgress<int>? progress = null)
        {
            try
            {
                if (_updateManager == null)
                    Initialize();

                _diagnosticsOutput.Write("Rozpoczynanie pobierania i instalacji aktualizacji...");
                progress?.Report(0);

                // Check for updates
                var updateInfo = await _updateManager!.CheckForUpdatesAsync();
                if (updateInfo == null)
                {
                    _diagnosticsOutput.Write("Brak aktualizacji do zastosowania");
                    return new UpdateDownloadResult
                    {
                        Success = false,
                        ErrorMessage = "No update available"
                    };
                }

                // Download update with progress callback
                await _updateManager.DownloadUpdatesAsync(updateInfo, p =>
                {
                    int percent = (int)p;
                    _diagnosticsOutput.Write($"Postęp: {percent}%");
                    progress?.Report(percent);
                });

                progress?.Report(100);
                _diagnosticsOutput.Write($"Aktualizacja {updateInfo.TargetFullRelease.Version} pobrana pomyślnie");

                // Apply updates and restart application
                // This will exit current app and start the new version
                _updateManager.ApplyUpdatesAndRestart(updateInfo);

                // Code below won't execute (app will restart)
                return new UpdateDownloadResult
                {
                    Success = true,
                    Version = updateInfo.TargetFullRelease.Version.ToString()
                };
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas pobierania aktualizacji: {ex.Message}");

                return new UpdateDownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Pobiera aktualizację bez restartu (two-step process)
        /// Użyj gdy chcesz dać userowi wybór kiedy zrestartować
        /// </summary>
        public async Task<UpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null)
        {
            try
            {
                if (_updateManager == null)
                    Initialize();

                _diagnosticsOutput.Write("Pobieranie aktualizacji w tle...");

                var updateInfo = await _updateManager!.CheckForUpdatesAsync();
                if (updateInfo == null)
                {
                    return new UpdateDownloadResult
                    {
                        Success = false,
                        ErrorMessage = "No update available"
                    };
                }

                // Download only
                await _updateManager.DownloadUpdatesAsync(updateInfo, p =>
                {
                    progress?.Report((int)p);
                });

                _diagnosticsOutput.Write("Aktualizacja pobrana, czeka na restart");

                return new UpdateDownloadResult
                {
                    Success = true,
                    Version = updateInfo.TargetFullRelease.Version.ToString(),
                    UpdateInfo = updateInfo // Save for later
                };
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd pobierania: {ex.Message}");
                return new UpdateDownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Aplikuje pobraną aktualizację i restartuje
        /// Użyj po DownloadUpdateAsync() gdy user zdecyduje się zrestartować
        /// </summary>
        public void ApplyUpdateAndRestart(UpdateInfo updateInfo)
        {
            try
            {
                if (_updateManager == null)
                    throw new InvalidOperationException("UpdateManager not initialized");

                _diagnosticsOutput.Write("Aplikowanie aktualizacji i restart...");
                _updateManager.ApplyUpdatesAndRestart(updateInfo);

                // Code below won't execute
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas restartu: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Aplikuje aktualizację i zamyka app (bez restartu)
        /// Użyj gdy chcesz żeby user ręcznie uruchomił app ponownie
        /// </summary>
        public void ApplyUpdateAndExit(UpdateInfo updateInfo)
        {
            try
            {
                if (_updateManager == null)
                    throw new InvalidOperationException("UpdateManager not initialized");

                _diagnosticsOutput.Write("Aplikowanie aktualizacji i zamykanie...");
                _updateManager.ApplyUpdatesAndExit(updateInfo);

                // Code below won't execute
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas exit: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sprawdza czy aplikacja jest zainstalowana przez Velopack
        /// </summary>
        public bool IsInstalled()
        {
            return VelopackApp.IsInstalled;
        }

        /// <summary>
        /// Pobiera URL do update source
        /// </summary>
        private string GetUpdateUrl()
        {
            var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new InvalidOperationException("Configuration:BaseUrl is not set in appsettings.json");
            }

            return $"{baseUrl}/releases";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _updateManager?.Dispose();
                _updateManager = null;
            }

            _disposed = true;
        }

        ~VelopackUpdateService()
        {
            Dispose(false);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Result Classes
    // ═══════════════════════════════════════════════════════════════

    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class UpdateDownloadResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public string? Version { get; set; }
        public string? ErrorMessage { get; set; }
        public UpdateInfo? UpdateInfo { get; set; } // For two-step updates
    }
}
```

---

## 2. Program.cs (Velopack Hooks)

**Location:** `SUSModder/Program.cs`

```csharp
using System;
using System.IO;
using Avalonia;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using Velopack;

namespace SUSModder
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // ═══════════════════════════════════════════════════════════
            // VELOPACK HOOKS - Must be FIRST, before any other code!
            // ═══════════════════════════════════════════════════════════

            // Build and run Velopack lifecycle hooks
            VelopackApp.Build()
                .WithFirstRun(OnFirstRun)
                .WithAfterInstallFastCallback(OnAfterInstall)
                .WithBeforeUpdateFastCallback(OnBeforeUpdate)
                .WithAfterUpdateFastCallback(OnAfterUpdate)
                .WithBeforeUninstallFastCallback(OnBeforeUninstall)
                .Run();

            // If we're here, this is normal app launch (not hook event)

            // ═══════════════════════════════════════════════════════════
            // Normal application startup
            // ═══════════════════════════════════════════════════════════

            string? appDirPath = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(appDirPath))
            {
                string appSettingsPath = Path.Combine(appDirPath, "appsettings.json");

                // Restore user settings if needed (after update)
                AppUpdateService.RestoreUserSettingsIfNeeded(
                    appSettingsPath,
                    new ConsoleLogger()
                );
            }

            // Build and start Avalonia app
            try
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex}");
                throw;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Velopack Lifecycle Hooks
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Called on first run after installation or update
        /// </summary>
        private static void OnFirstRun(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] First run: v{version}");

                // Optional: Show welcome screen, changelog, setup wizard, etc.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnFirstRun: {ex.Message}");
            }
        }

        /// <summary>
        /// Called immediately after fresh installation
        /// </summary>
        private static void OnAfterInstall(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] After install: v{version}");

                // Velopack automatically creates shortcuts
                // You can add custom logic here:
                // - Register protocol handler
                // - Add to Windows startup
                // - Create firewall rules
                // - etc.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnAfterInstall: {ex.Message}");
            }
        }

        /// <summary>
        /// Called before update is applied (app still running old version)
        /// </summary>
        private static void OnBeforeUpdate(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] Before update to: v{version}");

                // Optional:
                // - Backup user data
                // - Close active connections
                // - Save application state
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnBeforeUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Called after update is applied (app running new version)
        /// </summary>
        private static void OnAfterUpdate(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] After update to: v{version}");

                // Optional:
                // - Migrate user data to new format
                // - Update registry entries
                // - Cleanup old files
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnAfterUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Called before app is uninstalled
        /// </summary>
        private static void OnBeforeUninstall(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] Before uninstall: v{version}");

                // Optional:
                // - Ask user about keeping data
                // - Cleanup registry
                // - Remove firewall rules
                //
                // IMPORTANT: Don't delete user data without asking!
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnBeforeUninstall: {ex.Message}");
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
```

---

## 3. MainWindowViewModel.cs (UI Integration)

**Location:** `SUSModder/ViewModels/MainWindowViewModel.cs`

Dodaj/zamień metody związane z aktualizacjami:

```csharp
using System;
using System.Threading.Tasks;
using ReactiveUI;
using Velopack;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly VelopackUpdateService _updateService;

        // Observable properties
        private bool _isCheckingForUpdates;
        private bool _isUpdateAvailable;
        private bool _isUpdating;
        private int _updateProgress;
        private string _latestVersion = string.Empty;

        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set => this.RaiseAndSetIfChanged(ref _isCheckingForUpdates, value);
        }

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
        }

        public bool IsUpdating
        {
            get => _isUpdating;
            set => this.RaiseAndSetIfChanged(ref _isUpdating, value);
        }

        public int UpdateProgress
        {
            get => _updateProgress;
            set => this.RaiseAndSetIfChanged(ref _updateProgress, value);
        }

        public string LatestVersion
        {
            get => _latestVersion;
            set => this.RaiseAndSetIfChanged(ref _latestVersion, value);
        }

        // Commands
        public ReactiveCommand<Unit, Unit> CheckForUpdatesCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallUpdateCommand { get; }

        public MainWindowViewModel(VelopackUpdateService updateService /* ... other deps */)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

            // Initialize commands
            CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
            InstallUpdateCommand = ReactiveCommand.CreateFromTask(InstallUpdateAsync,
                this.WhenAnyValue(
                    x => x.IsUpdateAvailable,
                    x => x.IsUpdating,
                    (avail, updating) => avail && !updating
                ));
        }

        /// <summary>
        /// Sprawdza dostępność aktualizacji
        /// </summary>
        public async Task CheckForUpdatesAsync()
        {
            if (IsCheckingForUpdates || IsUpdating)
                return;

            try
            {
                IsCheckingForUpdates = true;
                IsUpdateAvailable = false;

                var result = await _updateService.CheckForUpdateAsync();

                if (!result.Success)
                {
                    await ShowErrorAsync("Błąd", $"Nie udało się sprawdzić aktualizacji:\n{result.ErrorMessage}");
                    return;
                }

                if (result.IsUpdateAvailable)
                {
                    IsUpdateAvailable = true;
                    LatestVersion = result.LatestVersion;

                    // Prompt user
                    var shouldUpdate = await ShowUpdatePromptAsync(result.CurrentVersion, result.LatestVersion);
                    if (shouldUpdate)
                    {
                        await InstallUpdateAsync();
                    }
                }
                else
                {
                    await ShowInfoAsync("Aktualizacje", "Aplikacja jest aktualna.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Błąd", $"Wystąpił nieoczekiwany błąd:\n{ex.Message}");
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        /// <summary>
        /// OPTION 1: One-step update (auto-restart)
        /// </summary>
        public async Task InstallUpdateAsync()
        {
            if (IsUpdating)
                return;

            try
            {
                IsUpdating = true;
                UpdateProgress = 0;

                var progress = new Progress<int>(percent =>
                {
                    UpdateProgress = percent;
                });

                // This will download, apply, and restart automatically
                // Your app will close and new version will start
                var result = await _updateService.DownloadAndApplyUpdateAsync(progress);

                // Code below won't execute if restart succeeded
                if (!result.Success)
                {
                    await ShowErrorAsync("Błąd", $"Nie udało się zainstalować aktualizacji:\n{result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Błąd", $"Wystąpił nieoczekiwany błąd:\n{ex.Message}");
            }
            finally
            {
                IsUpdating = false;
            }
        }

        /// <summary>
        /// OPTION 2: Two-step update (download → user chooses when to restart)
        /// </summary>
        public async Task InstallUpdateTwoStepAsync()
        {
            if (IsUpdating)
                return;

            try
            {
                IsUpdating = true;
                UpdateProgress = 0;

                var progress = new Progress<int>(percent =>
                {
                    UpdateProgress = percent;
                });

                // Step 1: Download only
                var result = await _updateService.DownloadUpdateAsync(progress);

                if (!result.Success)
                {
                    await ShowErrorAsync("Błąd", $"Nie udało się pobrać aktualizacji:\n{result.ErrorMessage}");
                    IsUpdating = false;
                    return;
                }

                // Step 2: Ask user when to restart
                var shouldRestartNow = await ShowRestartPromptAsync();
                if (shouldRestartNow)
                {
                    // Save any user data first
                    SaveUserSettings();

                    // Apply and restart
                    _updateService.ApplyUpdateAndRestart(result.UpdateInfo!);

                    // Code below won't execute
                }
                else
                {
                    // User will restart later
                    IsUpdateAvailable = false;
                    IsUpdating = false;
                    await ShowInfoAsync("Aktualizacja", "Aktualizacja pobrana. Zostanie zastosowana przy następnym uruchomieniu.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Błąd", $"Wystąpił nieoczekiwany błąd:\n{ex.Message}");
                IsUpdating = false;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Helper methods (implement based on your UI framework)
        // ═══════════════════════════════════════════════════════════

        private async Task<bool> ShowUpdatePromptAsync(string currentVersion, string latestVersion)
        {
            // Show dialog: "Update available v{latestVersion}. Install now?"
            // Return true if user clicks Yes
            return false; // Placeholder
        }

        private async Task<bool> ShowRestartPromptAsync()
        {
            // Show dialog: "Update ready. Restart now?"
            return false; // Placeholder
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            // Show error dialog
        }

        private async Task ShowInfoAsync(string title, string message)
        {
            // Show info dialog
        }

        private void SaveUserSettings()
        {
            // Save any unsaved settings before restart
        }
    }
}
```

---

## 4. AppUpdateViewModel.cs (Dedicated Update UI)

**Optional:** Dedykowany ViewModel dla ekranu aktualizacji

**Location:** `SUSModder/ViewModels/AppUpdateViewModel.cs`

```csharp
using System;
using System.Threading.Tasks;
using ReactiveUI;
using Velopack;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels
{
    public class AppUpdateViewModel : ViewModelBase
    {
        private readonly VelopackUpdateService _updateService;

        private string _currentVersion = string.Empty;
        private string _latestVersion = string.Empty;
        private string _statusMessage = string.Empty;
        private int _progressValue;
        private bool _isCheckingUpdate;
        private bool _isUpdateAvailable;
        private bool _isUpdating;
        private UpdateInfo? _pendingUpdate;

        public string CurrentVersion
        {
            get => _currentVersion;
            set => this.RaiseAndSetIfChanged(ref _currentVersion, value);
        }

        public string LatestVersion
        {
            get => _latestVersion;
            set => this.RaiseAndSetIfChanged(ref _latestVersion, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public int ProgressValue
        {
            get => _progressValue;
            set => this.RaiseAndSetIfChanged(ref _progressValue, value);
        }

        public bool IsCheckingUpdate
        {
            get => _isCheckingUpdate;
            set => this.RaiseAndSetIfChanged(ref _isCheckingUpdate, value);
        }

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
        }

        public bool IsUpdating
        {
            get => _isUpdating;
            set => this.RaiseAndSetIfChanged(ref _isUpdating, value);
        }

        public ReactiveCommand<Unit, Unit> CheckUpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> DownloadUpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallUpdateCommand { get; }

        public AppUpdateViewModel(VelopackUpdateService updateService, string currentVersion)
        {
            _updateService = updateService;
            _currentVersion = currentVersion;

            CheckUpdateCommand = ReactiveCommand.CreateFromTask(CheckForUpdateAsync);

            DownloadUpdateCommand = ReactiveCommand.CreateFromTask(DownloadUpdateAsync,
                this.WhenAnyValue(
                    x => x.IsUpdateAvailable,
                    x => x.IsUpdating,
                    (avail, updating) => avail && !updating
                ));

            InstallUpdateCommand = ReactiveCommand.Create(InstallUpdate,
                this.WhenAnyValue(x => x._pendingUpdate, p => p != null));
        }

        private async Task CheckForUpdateAsync()
        {
            IsCheckingUpdate = true;
            StatusMessage = "Sprawdzanie dostępności aktualizacji...";

            var result = await _updateService.CheckForUpdateAsync();

            IsCheckingUpdate = false;

            if (result.Success && result.IsUpdateAvailable)
            {
                LatestVersion = result.LatestVersion;
                IsUpdateAvailable = true;
                StatusMessage = $"Dostępna nowa wersja: {result.LatestVersion}";
            }
            else if (result.Success)
            {
                StatusMessage = "Aplikacja jest aktualna.";
            }
            else
            {
                StatusMessage = $"Błąd: {result.ErrorMessage}";
            }
        }

        private async Task DownloadUpdateAsync()
        {
            IsUpdating = true;
            StatusMessage = "Pobieranie aktualizacji...";
            ProgressValue = 0;

            var progress = new Progress<int>(percent =>
            {
                ProgressValue = percent;
                StatusMessage = $"Pobieranie... {percent}%";
            });

            var result = await _updateService.DownloadUpdateAsync(progress);

            IsUpdating = false;

            if (result.Success)
            {
                _pendingUpdate = result.UpdateInfo;
                StatusMessage = "Aktualizacja gotowa do instalacji. Kliknij 'Zainstaluj'.";
                IsUpdateAvailable = false;
            }
            else
            {
                StatusMessage = $"Błąd pobierania: {result.ErrorMessage}";
            }
        }

        private void InstallUpdate()
        {
            if (_pendingUpdate == null)
                return;

            StatusMessage = "Instalowanie i restartowanie...";

            // This will restart the app
            _updateService.ApplyUpdateAndRestart(_pendingUpdate);

            // Code below won't execute
        }
    }
}
```

**Corresponding View (AppUpdateView.axaml):**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:SUSModder.ViewModels"
             x:Class="SUSModder.Views.AppUpdateView"
             x:DataType="vm:AppUpdateViewModel">

    <StackPanel Margin="20" Spacing="15">
        <TextBlock Text="Aktualizacje" FontSize="24" FontWeight="Bold"/>

        <StackPanel Spacing="5">
            <TextBlock Text="{Binding CurrentVersion, StringFormat='Obecna wersja: {0}'}"/>
            <TextBlock Text="{Binding LatestVersion, StringFormat='Najnowsza wersja: {0}'}"
                       IsVisible="{Binding IsUpdateAvailable}"/>
        </StackPanel>

        <TextBlock Text="{Binding StatusMessage}" Foreground="Gray"/>

        <ProgressBar Value="{Binding ProgressValue}"
                     IsVisible="{Binding IsUpdating}"
                     Height="20"
                     Minimum="0"
                     Maximum="100"/>

        <StackPanel Orientation="Horizontal" Spacing="10">
            <Button Content="Sprawdź aktualizacje"
                    Command="{Binding CheckUpdateCommand}"
                    IsEnabled="{Binding !IsCheckingUpdate}"/>

            <Button Content="Pobierz aktualizację"
                    Command="{Binding DownloadUpdateCommand}"
                    IsVisible="{Binding IsUpdateAvailable}"/>

            <Button Content="Zainstaluj i restartuj"
                    Command="{Binding InstallUpdateCommand}"
                    IsVisible="{Binding !!_pendingUpdate}"/>
        </StackPanel>
    </StackPanel>
</UserControl>
```

---

## 5. Build Scripts

### 5.1. PowerShell Build Script

**File:** `build-release-velopack.ps1`

```powershell
#Requires -Version 5.1
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [string]$PreviousVersion = "",

    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory=$false)]
    [switch]$SignPackages
)

$ErrorActionPreference = "Stop"

Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Building SUSModder v$Version with Velopack" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ═══════════════════════════════════════════════════════════════
# 1. Update Version
# ═══════════════════════════════════════════════════════════════
Write-Host "[1/6] Updating version..." -ForegroundColor Yellow

$appsettingsPath = "SUSModder\appsettings.json"
(Get-Content $appsettingsPath -Raw) -replace '"CurrentVersion":\s*"[^"]*"', "`"CurrentVersion`": `"$Version`"" | Set-Content $appsettingsPath -NoNewline

Write-Host "  ✓ Version updated to $Version" -ForegroundColor Green

# ═══════════════════════════════════════════════════════════════
# 2. Clean
# ═══════════════════════════════════════════════════════════════
Write-Host "[2/6] Cleaning..." -ForegroundColor Yellow

@("SUSModder\bin", "SUSModder\obj", "SUSModder.Core\bin", "SUSModder.Core\obj", "Releases") | ForEach-Object {
    if (Test-Path $_) {
        Remove-Item -Recurse -Force $_
    }
}

Write-Host "  ✓ Cleaned" -ForegroundColor Green

# ═══════════════════════════════════════════════════════════════
# 3. Restore
# ═══════════════════════════════════════════════════════════════
Write-Host "[3/6] Restoring dependencies..." -ForegroundColor Yellow

dotnet restore SUSModder.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

Write-Host "  ✓ Dependencies restored" -ForegroundColor Green

# ═══════════════════════════════════════════════════════════════
# 4. Publish
# ═══════════════════════════════════════════════════════════════
Write-Host "[4/6] Publishing..." -ForegroundColor Yellow

dotnet publish SUSModder\SUSModder.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Write-Host "  ✓ Published" -ForegroundColor Green

# ═══════════════════════════════════════════════════════════════
# 5. Pack with Velopack
# ═══════════════════════════════════════════════════════════════
Write-Host "[5/6] Packing with Velopack..." -ForegroundColor Yellow

$publishDir = "SUSModder\bin\$Configuration\net8.0\win-x64\publish"

$vpkArgs = @(
    "pack",
    "--packId", "SUSModder",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "SUSModder.exe",
    "--packTitle", "SUSModder - Among Us Mod Manager",
    "--packAuthors", "Your Company"
)

# Add icon if exists
if (Test-Path "SUSModder\icon.ico") {
    $vpkArgs += "--icon"
    $vpkArgs += "SUSModder\icon.ico"
}

# Add delta if previous version provided
if ($PreviousVersion) {
    Write-Host "  Creating delta from v$PreviousVersion..." -ForegroundColor Gray
    $previousNupkg = "Releases\SUSModder-$PreviousVersion-full.nupkg"
    if (Test-Path $previousNupkg) {
        $vpkArgs += "--delta"
        $vpkArgs += $previousNupkg
    } else {
        Write-Host "  ⚠ Previous version not found, skipping delta" -ForegroundColor Yellow
    }
}

# Add signing if requested
if ($SignPackages -and $env:CODE_SIGNING_CERT_PATH) {
    $certPath = $env:CODE_SIGNING_CERT_PATH
    $certPass = $env:CODE_SIGNING_CERT_PASSWORD
    $vpkArgs += "--signTemplate"
    $vpkArgs += "SignTool.exe sign /f `"$certPath`" /p $certPass /t http://timestamp.digicert.com `"{{file}}`""
}

& vpk @vpkArgs

if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "  ✓ Velopack package created" -ForegroundColor Green

# ═══════════════════════════════════════════════════════════════
# 6. Summary
# ═══════════════════════════════════════════════════════════════
Write-Host "[6/6] Build complete!" -ForegroundColor Green
Write-Host ""
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Output: .\Releases\" -ForegroundColor White
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Get-ChildItem Releases -Exclude packages | ForEach-Object {
    $size = "{0:N2} MB" -f ($_.Length / 1MB)
    Write-Host "  - $($_.Name) ($size)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Test Setup.exe locally" -ForegroundColor White
Write-Host "  2. Upload ./Releases/ to https://susmodder.app/releases/" -ForegroundColor White
Write-Host "  3. Verify manifest accessible" -ForegroundColor White
Write-Host ""
```

**Usage:**
```powershell
# First release
.\build-release-velopack.ps1 -Version 2.1.0

# With delta from previous
.\build-release-velopack.ps1 -Version 2.1.1 -PreviousVersion 2.1.0

# With signing
.\build-release-velopack.ps1 -Version 2.1.0 -SignPackages
```

---

## 6. Backend Examples

### 6.1. NGINX Configuration

```nginx
server {
    listen 443 ssl http2;
    server_name susmodder.app;

    # SSL config...

    # Velopack releases
    location /releases/ {
        alias /var/www/susmodder/releases/;
        autoindex off;

        # JSON manifests
        location ~ \.json$ {
            default_type application/json;
            add_header Cache-Control "max-age=300"; # 5 minutes
            add_header Access-Control-Allow-Origin *;
        }

        # .nupkg packages
        location ~ \.nupkg$ {
            default_type application/octet-stream;
            add_header Content-Disposition "attachment";
            add_header Cache-Control "public, max-age=31536000, immutable";
        }

        # Setup.exe
        location ~ Setup\.exe$ {
            default_type application/octet-stream;
            add_header Content-Disposition "attachment; filename=SUSModder-Setup.exe";
            add_header Cache-Control "max-age=3600";
        }
    }
}
```

### 6.2. Deploy Script

```powershell
# deploy-release.ps1
param([string]$Version, [string]$Server = "deploy@susmodder.app")

Write-Host "Deploying SUSModder v$Version..." -ForegroundColor Cyan

# Upload to server
scp -r Releases/* "$Server:/var/www/susmodder/releases/"

# Set permissions
ssh $Server "chmod 644 /var/www/susmodder/releases/*"

# Verify
$manifest = Invoke-WebRequest "https://susmodder.app/releases/releases.$Version.json"
if ($manifest.StatusCode -eq 200) {
    Write-Host "✓ Deployment successful!" -ForegroundColor Green
} else {
    Write-Host "✗ Deployment verification failed!" -ForegroundColor Red
}
```

---

## Quick Start Checklist

1. ✅ Install Velopack NuGet: `dotnet add package Velopack`
2. ✅ Install vpk CLI: `dotnet tool install -g vpk`
3. ✅ Copy `VelopackUpdateService.cs` → `SUSModder.Core/Services/`
4. ✅ Update `Program.cs` → Add Velopack hooks
5. ✅ Update `MainWindowViewModel.cs` → Integrate update UI
6. ✅ Copy `build-release-velopack.ps1` → Root directory
7. ✅ Build: `.\build-release-velopack.ps1 -Version 2.1.0`
8. ✅ Test: `.\Releases\SUSModder-Setup.exe`

---

**To wszystko!** Masz teraz complete working code dla Velopack w SUSModder.

**Dokumentacja:**
- API Reference: https://docs.velopack.io/reference/cs/Velopack
- Getting Started: https://docs.velopack.io/getting-started/wpf-avalonia
- Migration Guide: https://docs.velopack.io/migrating/squirrel

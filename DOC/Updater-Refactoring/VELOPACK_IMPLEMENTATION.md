# Velopack - Szczegółowy Plan Implementacji

**Data:** 2025-10-28
**Target version:** SUSModder 2.1.0
**Estimated effort:** 4-6 dni roboczych
**Framework:** Velopack (następca Squirrel.Windows, napisany w Rust)

---

## Spis Treści

1. [Czym Jest Velopack](#1-czym-jest-velopack)
2. [Prerequisites & Setup](#2-prerequisites--setup)
3. [Zmiany w Kodzie - Krok Po Kroku](#3-zmiany-w-kodzie---krok-po-kroku)
4. [Backend Requirements](#4-backend-requirements)
5. [Build & Release Process](#5-build--release-process)
6. [Testing Checklist](#6-testing-checklist)
7. [Deployment Strategy](#7-deployment-strategy)
8. [Troubleshooting](#8-troubleshooting)

---

## 1. Czym Jest Velopack

### 1.1. Overview

**Velopack** to next-generation installer i auto-update framework dla desktop aplikacji.

**Historia:**
```
Squirrel.Windows (2013-2019)
  ↓ deprecated
Clowd.Squirrel (2019-2024)
  ↓ renamed & rewritten
Velopack (2024+) ← Aktualny
```

**Key Features:**
- ✅ **Native Performance** - przepisany w Rust (szybszy od C# Squirrel)
- ✅ **Stała Ścieżka Exe** - `{root}\current\YourApp.exe` (nie zmienia się przy update)
  - Rozwiązuje problemy: firewall rules, AV issues, GPU preferences, tray icon pinning
- ✅ **Delta Updates** - tylko różnice między wersjami (~80-90% mniejsze pliki)
- ✅ **Cross-Platform** - Windows, macOS, Linux
- ✅ **Auto-Migration** - automatycznie wykrywa i migruje z Squirrel.Windows
- ✅ **Zero Config** - działa out-of-the-box

### 1.2. Architecture Comparison

**Old (Squirrel.Windows):**
```
app-1.0.0/
  └─ SUSModder.exe
app-1.0.1/       ← Nowa ścieżka przy każdym update!
  └─ SUSModder.exe
Update.exe       ← Squirrel updater (C#)
```

**New (Velopack):**
```
current/
  └─ SUSModder.exe  ← Zawsze ta sama ścieżka!
packages/            ← Cached update files
Update.exe           ← Velopack updater (Rust, szybszy)
```

**Kluczowa różnica:** Firewall, antivirus, shortcuts zawsze widzą tę samą ścieżkę.

### 1.3. Dlaczego Velopack > Squirrel.Windows

| Aspekt | Squirrel.Windows | Velopack |
|--------|------------------|----------|
| **Maintenance** | Deprecated (2019) | Aktywny (2025) |
| **Performance** | C# | Rust (native) |
| **File path** | app-1.0.x (zmienne) | current/ (stałe) |
| **AV issues** | Wysokie | Niższe (stała ścieżka) |
| **Cross-platform** | Windows only | Win/Mac/Linux |
| **Migration** | Manual | Auto z Squirrel |
| **Speed** | Baseline | 2-3x szybsze |

---

## 2. Prerequisites & Setup

### 2.1. NuGet Package

Dodaj do `SUSModder/SUSModder.csproj`:

```xml
<ItemGroup>
  <!-- Velopack core -->
  <PackageReference Include="Velopack" Version="0.0.1335" />
</ItemGroup>
```

**Instalacja:**
```bash
cd SUSModder
dotnet add package Velopack
```

**Latest version:** https://www.nuget.org/packages/Velopack

### 2.2. Velopack CLI Tool (vpk)

Instalacja globalnie:

```bash
dotnet tool install -g vpk
```

Sprawdź:
```bash
vpk --version
# Output: 0.0.1335 (lub nowsza)
```

**Alternative:** Download standalone binary:
```bash
# Windows
Invoke-WebRequest https://github.com/velopack/velopack/releases/latest/download/vpk-windows-x64.exe -OutFile vpk.exe

# Dodaj do PATH lub używaj ./vpk.exe
```

### 2.3. Project Configuration

**Upewnij się że masz:**

1. **Single-file publish** (już masz w SUSModder.csproj):
   ```xml
   <PublishSingleFile>true</PublishSingleFile>
   <SelfContained>true</SelfContained>
   ```

2. **Application icon** (recommended):
   ```xml
   <ApplicationIcon>icon.ico</ApplicationIcon>
   ```

3. **Assembly metadata** (w SUSModder.csproj lub AssemblyInfo.cs):
   ```xml
   <AssemblyTitle>SUSModder</AssemblyTitle>
   <Company>Your Company</Company>
   <Product>SUSModder - Among Us Mod Manager</Product>
   <Description>Menedżer modów dla Among Us</Description>
   ```

---

## 3. Zmiany w Kodzie - Krok Po Kroku

### Krok 1: Nowy Serwis - VelopackUpdateService.cs

Utwórz `SUSModder.Core/Services/VelopackUpdateService.cs`:

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
        /// Inicjalizuje UpdateManager. Wywołaj raz na początku.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_updateManager != null)
                return; // Already initialized

            try
            {
                var updateUrl = GetUpdateUrl();
                _diagnosticsOutput.Write($"Inicjalizacja Velopack UpdateManager: {updateUrl}");

                // Create update source (HTTP/HTTPS)
                var source = new SimpleWebSource(updateUrl);

                // Create update manager
                _updateManager = new UpdateManager(source);

                _diagnosticsOutput.Write("Velopack UpdateManager zainicjalizowany pomyślnie");
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd inicjalizacji UpdateManager: {ex.Message}");
                throw new InvalidOperationException("Failed to initialize Velopack UpdateManager", ex);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Sprawdza dostępność aktualizacji
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                if (_updateManager == null)
                    await InitializeAsync();

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
                _diagnosticsOutput.Write($"Stack trace: {ex.StackTrace}");

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
        /// Pobiera i instaluje aktualizację
        /// </summary>
        public async Task<UpdateDownloadResult> DownloadAndApplyUpdateAsync(IProgress<int>? progress = null)
        {
            try
            {
                if (_updateManager == null)
                    await InitializeAsync();

                _diagnosticsOutput.Write("Rozpoczynanie pobierania i instalacji aktualizacji...");
                progress?.Report(0);

                // Check for updates first
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

                // Download update (with progress)
                await _updateManager.DownloadUpdatesAsync(updateInfo, p =>
                {
                    int percent = (int)p;
                    _diagnosticsOutput.Write($"Postęp aktualizacji: {percent}%");
                    progress?.Report(percent);
                });

                progress?.Report(100);
                _diagnosticsOutput.Write($"Aktualizacja {updateInfo.TargetFullRelease.Version} pobrana pomyślnie");

                // Apply update and prepare for restart
                _updateManager.ApplyUpdatesAndRestart(updateInfo);

                // If we reach here, restart didn't happen (shouldn't occur normally)
                return new UpdateDownloadResult
                {
                    Success = true,
                    Version = updateInfo.TargetFullRelease.Version.ToString()
                };
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas pobierania aktualizacji: {ex.Message}");
                _diagnosticsOutput.Write($"Stack trace: {ex.StackTrace}");

                return new UpdateDownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Pobiera aktualizację bez restartu (optional - dla background download)
        /// </summary>
        public async Task<UpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null)
        {
            try
            {
                if (_updateManager == null)
                    await InitializeAsync();

                var updateInfo = await _updateManager!.CheckForUpdatesAsync();
                if (updateInfo == null)
                {
                    return new UpdateDownloadResult
                    {
                        Success = false,
                        ErrorMessage = "No update available"
                    };
                }

                await _updateManager.DownloadUpdatesAsync(updateInfo, p =>
                {
                    progress?.Report((int)p);
                });

                return new UpdateDownloadResult
                {
                    Success = true,
                    Version = updateInfo.TargetFullRelease.Version.ToString(),
                    UpdateInfo = updateInfo // Store for later apply
                };
            }
            catch (Exception ex)
            {
                return new UpdateDownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Stosuje pobraną aktualizację i restartuje (dla two-step update)
        /// </summary>
        public void ApplyUpdateAndRestart(object updateInfo)
        {
            try
            {
                if (_updateManager == null)
                    throw new InvalidOperationException("UpdateManager not initialized");

                _diagnosticsOutput.Write("Aplikowanie aktualizacji i restart...");
                _updateManager.ApplyUpdatesAndRestart((UpdateInfo)updateInfo);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas restartu: {ex.Message}");
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
    // Result Classes (same as before)
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
        public object? UpdateInfo { get; set; } // For two-step updates
    }
}
```

### Krok 2: Modyfikacja Program.cs - Velopack Hooks

Edytuj `SUSModder/Program.cs`:

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
            // VELOPACK HOOKS - Must be FIRST, before any other code
            // ═══════════════════════════════════════════════════════════

            // Build Velopack app and run hooks
            VelopackApp.Build()
                .WithFirstRun(OnFirstRun)
                .WithAfterInstallFastCallback(OnAfterInstall)
                .WithBeforeUpdateFastCallback(OnBeforeUpdate)
                .WithAfterUpdateFastCallback(OnAfterUpdate)
                .WithBeforeUninstallFastCallback(OnBeforeUninstall)
                .Run();

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

        /// <summary>
        /// Called on first run after installation or update
        /// </summary>
        private static void OnFirstRun(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] First run: v{version}");
                // Optional: Show welcome screen, changelog, etc.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnFirstRun: {ex.Message}");
            }
        }

        /// <summary>
        /// Called immediately after installation
        /// </summary>
        private static void OnAfterInstall(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] After install: v{version}");

                // Create shortcuts
                // Velopack automatically creates shortcuts, but you can customize:
                // - Desktop shortcut
                // - Start menu shortcut
                // - Optionally: register protocol handler, add to startup, etc.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnAfterInstall: {ex.Message}");
            }
        }

        /// <summary>
        /// Called before update is applied
        /// </summary>
        private static void OnBeforeUpdate(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] Before update to: v{version}");

                // Optional: Backup user data, close connections, etc.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnBeforeUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Called after update is applied
        /// </summary>
        private static void OnAfterUpdate(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] After update to: v{version}");

                // Optional: Migrate user data, update registry, etc.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Velopack] Error in OnAfterUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Called before uninstallation
        /// </summary>
        private static void OnBeforeUninstall(SemanticVersion version)
        {
            try
            {
                Console.WriteLine($"[Velopack] Before uninstall: v{version}");

                // Optional: Ask about user data, cleanup registry, etc.
                // Note: Don't delete user data without asking!
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

### Krok 3: Integracja w MainWindowViewModel

(Ten kod jest podobny do poprzedniego - tylko zmiana nazwy serwisu)

```csharp
// Replace SquirrelUpdateService → VelopackUpdateService
private readonly VelopackUpdateService _velopackUpdateService;

// Rest of the code remains similar (CheckForUpdatesAsync, InstallUpdateAsync, etc.)
```

---

## 4. Backend Requirements

### 4.1. Struktura Katalogów

```
https://susmodder.app/releases/
├─ releases.{version}.json      (Release manifest)
├─ SUSModder-2.0.1-full.nupkg   (Full package v2.0.1)
├─ SUSModder-2.1.0-full.nupkg   (Full package v2.1.0)
├─ SUSModder-2.1.0-delta.nupkg  (Delta from 2.0.1 → 2.1.0)
└─ Setup.exe                     (Installer)
```

**Różnica vs Squirrel:** Nie ma pojedynczego `RELEASES` file - każda wersja ma własny manifest JSON.

### 4.2. Release Manifest Format

**Example: `releases.2.1.0.json`**

```json
{
  "Version": "2.1.0",
  "Packages": [
    {
      "FileName": "SUSModder-2.1.0-full.nupkg",
      "SHA256": "abc123...",
      "Size": 54525952,
      "Type": "Full"
    },
    {
      "FileName": "SUSModder-2.1.0-delta.nupkg",
      "SHA256": "def456...",
      "Size": 5242880,
      "Type": "Delta",
      "BasedOn": "2.0.1"
    }
  ],
  "ReleaseNotes": "https://susmodder.app/changelog#2.1.0"
}
```

**Generowane automatycznie przez `vpk pack`**

---

## 5. Build & Release Process

### 5.1. Publish Application

```bash
dotnet publish SUSModder/SUSModder.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `SUSModder/bin/Release/net8.0/win-x64/publish/`

### 5.2. Pack with Velopack (vpk)

**Basic:**
```bash
vpk pack \
    --packId SUSModder \
    --packVersion 2.1.0 \
    --packDir SUSModder/bin/Release/net8.0/win-x64/publish \
    --mainExe SUSModder.exe
```

**Advanced (with options):**
```bash
vpk pack \
    --packId SUSModder \
    --packVersion 2.1.0 \
    --packDir SUSModder/bin/Release/net8.0/win-x64/publish \
    --mainExe SUSModder.exe \
    --packTitle "SUSModder - Among Us Mod Manager" \
    --packAuthors "Your Company" \
    --icon SUSModder/icon.ico \
    --releaseNotes "https://susmodder.app/changelog#2.1.0" \
    --signTemplate SignTool.exe sign /f cert.pfx /p {{password}} "{{file}}"
```

**Output:**
```
Releases/
├─ SUSModder-2.1.0-full.nupkg
├─ SUSModder-Setup.exe
└─ releases.2.1.0.json
```

### 5.3. Delta Packages

Dla delta updates, podaj poprzednią wersję:

```bash
vpk pack \
    --packId SUSModder \
    --packVersion 2.1.0 \
    --packDir ./publish \
    --mainExe SUSModder.exe \
    --delta SUSModder-2.0.1-full.nupkg
```

Output dodatkowy: `SUSModder-2.1.0-delta.nupkg`

**Size comparison:**
```
Full:  52 MB (SUSModder-2.1.0-full.nupkg)
Delta:  6 MB (SUSModder-2.1.0-delta.nupkg)

Savings: 88% bandwidth reduction
```

### 5.4. Automated Build Script (PowerShell)

**File:** `build-release-velopack.ps1`

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [string]$PreviousVersion = "",

    [Parameter(Mandatory=$false)]
    [switch]$SignPackages
)

$ErrorActionPreference = "Stop"

Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Building SUSModder v$Version (Velopack)" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════" -ForegroundColor Cyan

# 1. Update version
Write-Host "[1/5] Updating version..." -ForegroundColor Yellow
(Get-Content SUSModder/appsettings.json -Raw) -replace '"CurrentVersion":\s*"[^"]*"', "`"CurrentVersion`": `"$Version`"" | Set-Content SUSModder/appsettings.json -NoNewline

# 2. Clean
Write-Host "[2/5] Cleaning..." -ForegroundColor Yellow
Remove-Item -Recurse -Force SUSModder/bin, SUSModder/obj, Releases -ErrorAction SilentlyContinue

# 3. Publish
Write-Host "[3/5] Publishing..." -ForegroundColor Yellow
dotnet publish SUSModder/SUSModder.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# 4. Pack with Velopack
Write-Host "[4/5] Packing with Velopack..." -ForegroundColor Yellow

$packArgs = @(
    "pack",
    "--packId", "SUSModder",
    "--packVersion", $Version,
    "--packDir", "SUSModder/bin/Release/net8.0/win-x64/publish",
    "--mainExe", "SUSModder.exe",
    "--packTitle", "SUSModder - Among Us Mod Manager",
    "--icon", "SUSModder/icon.ico"
)

# Add delta if previous version provided
if ($PreviousVersion) {
    Write-Host "  Creating delta from v$PreviousVersion..." -ForegroundColor Gray
    $packArgs += "--delta"
    $packArgs += "Releases/SUSModder-$PreviousVersion-full.nupkg"
}

# Add signing if requested
if ($SignPackages -and $env:CODE_SIGNING_CERT_PATH) {
    $certPath = $env:CODE_SIGNING_CERT_PATH
    $certPass = $env:CODE_SIGNING_CERT_PASSWORD
    $packArgs += "--signTemplate"
    $packArgs += "SignTool.exe sign /f $certPath /p $certPass /t http://timestamp.digicert.com `"{{file}}`""
}

& vpk @packArgs

if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

# 5. Summary
Write-Host "[5/5] Build complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Output: .\Releases\" -ForegroundColor White
Get-ChildItem Releases | ForEach-Object {
    $size = "{0:N2} MB" -f ($_.Length / 1MB)
    Write-Host "  - $($_.Name) ($size)" -ForegroundColor Gray
}
```

**Usage:**
```powershell
# First release
.\build-release-velopack.ps1 -Version 2.1.0

# With delta from previous version
.\build-release-velopack.ps1 -Version 2.1.1 -PreviousVersion 2.1.0

# With signing
.\build-release-velopack.ps1 -Version 2.1.0 -SignPackages
```

---

## 6. Testing Checklist

### 6.1. Local Testing

**Test 1: Fresh Install**
```powershell
.\Releases\SUSModder-Setup.exe

# Should:
# ✓ Install to %LocalAppData%\SUSModder\
# ✓ Create current\SUSModder.exe (stała ścieżka)
# ✓ Create shortcuts
# ✓ Launch app
```

**Test 2: Update Flow**
```powershell
# 1. Install v2.0.1
.\Releases-old\SUSModder-Setup.exe

# 2. Check for updates (should detect v2.1.0)
# 3. Download delta (6MB, not full 52MB)
# 4. Apply update
# 5. Verify exe path unchanged: %LocalAppData%\SUSModder\current\SUSModder.exe
```

**Test 3: Settings Preservation**
```
1. Install v2.0.1
2. Configure settings (ModsInstallPath, Theme)
3. Update to v2.1.0
4. Verify settings preserved
```

---

## 7. Deployment Strategy

### 7.1. Upload to Server

```bash
# Upload releases
scp Releases/* server:/var/www/susmodder/releases/

# Set permissions
ssh server "chmod 644 /var/www/susmodder/releases/*"
```

### 7.2. Verify Deployment

```bash
# Check manifest accessible
curl https://susmodder.app/releases/releases.2.1.0.json

# Check package download
curl -I https://susmodder.app/releases/SUSModder-2.1.0-full.nupkg
```

---

## 8. Troubleshooting

### Issue 1: "Package not found"

**Symptom:** App can't find update packages

**Fix:**
```bash
# Verify manifest structure
curl https://susmodder.app/releases/releases.latest.json

# Check Velopack source configuration
var source = new SimpleWebSource("https://susmodder.app/releases");
```

### Issue 2: "Delta update failed, falling back to full"

**Cause:** Delta corruption or base version mismatch

**Expected behavior:** Velopack automatically falls back to full package

---

## Summary

### Key Differences from Squirrel

| Aspect | Squirrel.Windows | Velopack |
|--------|------------------|----------|
| **CLI tool** | `squirrel releasify` | `vpk pack` |
| **Manifest** | Single RELEASES file | Per-version JSON |
| **Path** | app-{version}/ | current/ (stała) |
| **Speed** | C# | Rust (2-3x faster) |
| **Migration** | Manual | Auto from Squirrel |

### Timeline: 4-6 Days

- Day 1-2: Install vpk, implement VelopackUpdateService
- Day 3-4: Integrate UI, testing
- Day 5: Backend setup, deployment
- Day 6: End-to-end verification

---

**Next:** [MIGRATION_PLAN.md](./MIGRATION_PLAN.md) - Migracja istniejących użytkowników

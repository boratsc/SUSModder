# Auto-rejestracja w Windows Registry

## Problem
Aplikacja zainstalowana z ZIP (portable) nie pojawia się w "Dodaj/usuń programy".

## Rozwiązanie: Auto-rejestracja przy pierwszym uruchomieniu

### Kod C# - RegistryInstaller.cs

```csharp
using Microsoft.Win32;
using System;
using System.IO;
using System.Security.Principal;

namespace SUSModder.Core.Utilities
{
    public static class RegistryInstaller
    {
        private const string UNINSTALL_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SUSModder";

        /// <summary>
        /// Sprawdza czy aplikacja jest zarejestrowana w Windows Registry
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UNINSTALL_KEY);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rejestruje aplikację w Windows "Dodaj/usuń programy"
        /// </summary>
        /// <param name="appVersion">Wersja aplikacji (np. "2.2.0")</param>
        /// <returns>True jeśli sukces</returns>
        public static bool RegisterApplication(string appVersion)
        {
            try
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SUSModder.exe");
                var installLocation = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var uninstallScriptPath = Path.Combine(installLocation, "uninstall.ps1");

                // Utwórz klucz rejestru
                using var key = Registry.CurrentUser.CreateSubKey(UNINSTALL_KEY);

                if (key == null)
                    return false;

                // Podstawowe informacje
                key.SetValue("DisplayName", "SUSModder");
                key.SetValue("DisplayVersion", appVersion);
                key.SetValue("Publisher", "SUSModder Team");
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("InstallLocation", installLocation);

                // Polecenie deinstalacji - uruchom uninstall.ps1
                if (File.Exists(uninstallScriptPath))
                {
                    var uninstallCommand = $"powershell.exe -ExecutionPolicy Bypass -File \"{uninstallScriptPath}\"";
                    key.SetValue("UninstallString", uninstallCommand);
                }
                else
                {
                    // Fallback - usuń katalog ręcznie
                    key.SetValue("UninstallString", $"cmd.exe /c rmdir /s /q \"{installLocation}\"");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                }

                // Szacunkowy rozmiar instalacji (w KB)
                var estimatedSize = CalculateInstallSize(installLocation);
                key.SetValue("EstimatedSize", estimatedSize, RegistryValueKind.DWord);

                // Metadane
                key.SetValue("URLInfoAbout", "https://susmodder.app");
                key.SetValue("HelpLink", "https://susmodder.app/help");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register application: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Usuwa wpis aplikacji z rejestru
        /// </summary>
        public static bool UnregisterApplication()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UNINSTALL_KEY, throwOnMissingSubKey: false);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to unregister application: {ex.Message}");
                return false;
            }
        }

        private static int CalculateInstallSize(string directory)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directory);
                long totalBytes = 0;

                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }

                // Konwersja na KB
                return (int)(totalBytes / 1024);
            }
            catch
            {
                return 100000; // ~100 MB fallback
            }
        }
    }
}
```

### Integracja w MainWindowViewModel.Initialization.cs

```csharp
// Dodaj na końcu InitializeApplicationAsync()

// Sprawdź czy aplikacja jest zarejestrowana w Windows Registry
if (!RegistryInstaller.IsRegistered())
{
    _diagnosticsOutput?.Write("[Registry] Application not registered in Windows");

    // Pokaż dialog pytając użytkownika
    var shouldRegister = await Dispatcher.UIThread.InvokeAsync(async () =>
    {
        var dialog = new ConfirmDialog(
            "Rejestracja w systemie Windows",
            "SUSModder nie jest zarejestrowany w systemie Windows.\n\n" +
            "Czy chcesz zarejestrować aplikację w \"Dodaj/usuń programy\"?\n\n" +
            "Umożliwi to łatwą deinstalację przez panel Windows."
        );
        dialog.OkButtonText = "Zarejestruj";
        dialog.CancelButtonText = "Pomiń";

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow != null)
            await dialog.ShowDialog(mainWindow);

        return dialog.Result;
    });

    if (shouldRegister)
    {
        var success = RegistryInstaller.RegisterApplication(AppVersion);

        if (success)
        {
            _diagnosticsOutput?.Write("[Registry] Application registered successfully");
            await ShowMessageAsync("Sukces", "Aplikacja została zarejestrowana w systemie Windows.");
        }
        else
        {
            _diagnosticsOutput?.Write("[Registry] Failed to register application");
            await ShowErrorDialogAsync("Nie udało się zarejestrować aplikacji.", "Błąd rejestracji");
        }
    }
    else
    {
        _diagnosticsOutput?.Write("[Registry] User skipped registration");
    }
}
else
{
    _diagnosticsOutput?.Write("[Registry] Application already registered");
}
```

### Aktualizacja uninstall.ps1

Dodaj usuwanie wpisu z rejestru:

```powershell
# Na początku skryptu, po zamknięciu procesu (linia 27)

# Usuń wpis z rejestru Windows
Write-Host "[5.5/6] Usuwanie wpisu z rejestru Windows..." -ForegroundColor Yellow
try {
    $registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SUSModder"
    if (Test-Path $registryPath) {
        Remove-Item -Path $registryPath -Recurse -Force
        Write-Host "  ✅ Wpis usunięty z rejestru" -ForegroundColor Green
    } else {
        Write-Host "  ℹ️ Brak wpisu w rejestrze" -ForegroundColor Gray
    }
} catch {
    Write-Host "  ⚠️ Nie udało się usunąć wpisu z rejestru: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""
```

## Zalety tego rozwiązania

1. ✅ **Zero zmian po stronie użytkownika** - auto-pytanie przy pierwszym uruchomieniu
2. ✅ **Nie wymaga uprawnień administratora** - używa HKCU (Current User)
3. ✅ **Kompatybilne z legacy ZIP users** - migują automatycznie
4. ✅ **Respektuje wybór użytkownika** - można pominąć rejestrację (portable mode)
5. ✅ **Łatwa deinstalacja** - przez panel Windows lub uninstall.ps1

## Alternatywne rozwiązania

### Opcja B: Tylko push do Setup.exe

Na stronie i w aplikacji dodaj komunikat:

```
⚠️ Jeśli zainstalowałeś SUSModder z pliku ZIP:
   - Pobierz i uruchom SUSModder-release-Setup.exe
   - To zapewni poprawną rejestrację w systemie Windows
```

### Opcja C: Migration tool

Stwórz oddzielny mini-installer który:
1. Wykrywa istniejącą instalację portable
2. Migruje do Velopack structure
3. Rejestruje w Windows
4. Usuwa stare pliki

## Rekomendacja

**Używaj Opcja A** (auto-rejestracja) - najlepsze UX:
- Pytaj użytkownika tylko raz
- Zapamiętaj wybór (nie pytaj ponownie jeśli odmówił)
- Dodaj opcję w Settings aby zarejestrować później

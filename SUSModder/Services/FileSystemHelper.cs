using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SUSModder.Services
{
    /// <summary>
    /// Helper do zaawansowanych operacji na systemie plików (usuwanie z retry, elevated permissions)
    /// </summary>
    public class FileSystemHelper
    {
        /// <summary>
        /// Bezpieczne usuwanie katalogu z wieloma strategiami fallback
        /// </summary>
        public async Task<bool> SafeDeleteDirectoryAsync(string directoryPath, string modName = "", Func<string, string, Task<bool>>? confirmElevatedCallback = null)
        {
            if (!Directory.Exists(directoryPath))
            {
                System.Diagnostics.Debug.WriteLine($"Katalog nie istnieje: {directoryPath}");
                return true;
            }

            try
            {
                // Pierwsza próba - standardowe usunięcie
                System.Diagnostics.Debug.WriteLine($"Próba standardowego usunięcia katalogu: {directoryPath}");
                Directory.Delete(directoryPath, true);

                // Sprawdź czy katalog został usunięty
                await Task.Delay(500); // Krótka pauza na operacje systemowe

                if (!Directory.Exists(directoryPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Katalog został pomyślnie usunięty: {directoryPath}");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"Katalog nadal istnieje po standardowym usunięciu: {directoryPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas standardowego usuwania: {ex.Message}");
            }

            // Druga próba - force delete
            bool forceDeleteSuccess = await ForceDeleteDirectoryAsync(directoryPath);
            if (forceDeleteSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"Katalog został usunięty przez force delete: {directoryPath}");
                return true;
            }

            // Trzecia próba - z podniesieniem uprawnień
            if (confirmElevatedCallback != null)
            {
                bool elevatedSuccess = await TryDeleteWithElevatedPermissionsAsync(directoryPath, modName, confirmElevatedCallback);
                if (elevatedSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"Katalog został usunięty z podwyższonymi uprawnieniami: {directoryPath}");
                    return true;
                }
            }

            // Jeśli wszystko zawiodło
            System.Diagnostics.Debug.WriteLine($"BŁĄD: Nie udało się usunąć katalogu: {directoryPath}");
            return false;
        }

        private async Task<bool> ForceDeleteDirectoryAsync(string directoryPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Rozpoczynanie force delete dla: {directoryPath}");

                // Usuń atrybut tylko do odczytu ze wszystkich plików i folderów
                await Task.Run(() => RemoveReadOnlyAttributes(directoryPath));

                // Zamknij wszystkie procesy które mogą blokować pliki
                await Task.Run(() => KillProcessesUsingDirectory(directoryPath));

                // Poczekaj chwilę na zwolnienie zasobów
                await Task.Delay(1000);

                // Spróbuj usunąć ponownie
                Directory.Delete(directoryPath, true);

                // Sprawdź rezultat
                await Task.Delay(500);
                return !Directory.Exists(directoryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas force delete: {ex.Message}");
                return false;
            }
        }

        private void RemoveReadOnlyAttributes(string directoryPath)
        {
            try
            {
                // Usuń atrybut ReadOnly z katalogu głównego
                var dirInfo = new DirectoryInfo(directoryPath);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }

                // Usuń atrybut ReadOnly ze wszystkich plików
                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Nie udało się usunąć atrybutu ReadOnly z pliku {file}: {ex.Message}");
                    }
                }

                // Usuń atrybut ReadOnly ze wszystkich podkatalogów
                foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var subDirInfo = new DirectoryInfo(dir);
                        if (subDirInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            subDirInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Nie udało się usunąć atrybutu ReadOnly z katalogu {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas usuwania atrybutów ReadOnly: {ex.Message}");
            }
        }

        private void KillProcessesUsingDirectory(string directoryPath)
        {
            try
            {
                // Lista procesów które mogą blokować pliki Among Us
                string[] processesToKill = { "Among Us", "AmongUs", "Among_Us" };

                foreach (string processName in processesToKill)
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Sprawdź czy proces używa plików z naszego katalogu
                            if (process.MainModule?.FileName?.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                System.Diagnostics.Debug.WriteLine($"Zamykanie procesu {processName} (PID: {process.Id})");
                                process.Kill();
                                process.WaitForExit(5000); // Czekaj maksymalnie 5 sekund
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Nie udało się zamknąć procesu {processName}: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas zamykania procesów: {ex.Message}");
            }
        }

        private async Task<bool> TryDeleteWithElevatedPermissionsAsync(string directoryPath, string modName, Func<string, string, Task<bool>> confirmCallback)
        {
            try
            {
                // Pokaż dialog użytkownikowi
                bool userConfirmed = await confirmCallback(
                    $"Nie udało się usunąć katalogu moda '{modName}' standardowymi metodami.\n\n" +
                    $"Katalog: {directoryPath}\n\n" +
                    "Czy chcesz spróbować usunąć go z podwyższonymi uprawnieniami?\n" +
                    "(Może pojawić się okno UAC)",
                    "Wymagane podwyższone uprawnienia"
                );

                if (!userConfirmed)
                {
                    return false;
                }

                // Tylko na Windows
                if (!OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Debug.WriteLine("Usuwanie z podwyższonymi uprawnieniami jest dostępne tylko na Windows.");
                    return false;
                }

                return await DeleteWithElevatedPermissionsWindows(directoryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas próby usunięcia z podwyższonymi uprawnieniami: {ex.Message}");
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task<bool> DeleteWithElevatedPermissionsWindows(string directoryPath)
        {
            try
            {
                // Użyj PowerShell z uprawnieniami administratora
                string script = $@"
            try {{
                if (Test-Path '{directoryPath}') {{
                    Remove-Item -Path '{directoryPath}' -Recurse -Force -ErrorAction Stop
                    Write-Output 'SUCCESS: Directory deleted'
                }} else {{
                    Write-Output 'SUCCESS: Directory does not exist'
                }}
            }} catch {{
                Write-Output ""ERROR: $($_.Exception.Message)""
            }}
        ";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{script}\"",
                    UseShellExecute = true,
                    Verb = "runas", // Wymusza uruchomienie jako administrator
                    CreateNoWindow = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Nie udało się uruchomić PowerShell z uprawnieniami administratora");
                        return false;
                    }

                    // Czekaj na zakończenie procesu
                    await Task.Run(() => process.WaitForExit());

                    // Sprawdź czy katalog został usunięty
                    await Task.Delay(1000);
                    bool success = !Directory.Exists(directoryPath);

                    System.Diagnostics.Debug.WriteLine($"PowerShell z uprawnieniami administratora - sukces: {success}");
                    return success;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas usuwania z PowerShell: {ex.Message}");

                // Jeśli PowerShell zawiódł, spróbuj cmd
                return await DeleteWithCmdElevated(directoryPath);
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task<bool> DeleteWithCmdElevated(string directoryPath)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir /s /q \"{directoryPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null) return false;

                    await Task.Run(() => process.WaitForExit());
                    await Task.Delay(1000);

                    return !Directory.Exists(directoryPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas usuwania z CMD: {ex.Message}");
                return false;
            }
        }
    }
}

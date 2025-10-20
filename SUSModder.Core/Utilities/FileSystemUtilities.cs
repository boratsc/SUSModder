using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Narzędzia do bezpiecznych operacji na systemie plików,
    /// w tym zaawansowane usuwanie katalogów z obsługą uprawnień i blokad plików.
    /// </summary>
    public static class FileSystemUtilities
    {
        /// <summary>
        /// Bezpiecznie usuwa katalog z wielopoziomową strategią:
        /// 1. Standardowe usunięcie
        /// 2. Force delete z usunięciem atrybutów ReadOnly i zamknięciem procesów
        /// 3. Usunięcie z podwyższonymi uprawnieniami (wymaga potwierdzenia użytkownika)
        /// </summary>
        /// <param name="directoryPath">Ścieżka do katalogu do usunięcia</param>
        /// <param name="modName">Nazwa moda (dla komunikatów diagnostycznych)</param>
        /// <param name="diagnostics">Output do logowania diagnostyki</param>
        /// <param name="confirmElevationFunc">Funkcja do potwierdzenia podniesienia uprawnień (async)</param>
        /// <returns>True jeśli katalog został usunięty, false w przeciwnym razie</returns>
        public static async Task<bool> SafeDeleteDirectoryAsync(
            string directoryPath,
            string modName = "",
            IDiagnosticsOutput? diagnostics = null,
            Func<string, string, Task<bool>>? confirmElevationFunc = null)
        {
            if (!Directory.Exists(directoryPath))
            {
                diagnostics?.Write($"Katalog nie istnieje: {directoryPath}");
                return true;
            }

            try
            {
                // Pierwsza próba - standardowe usunięcie
                diagnostics?.Write($"Próba standardowego usunięcia katalogu: {directoryPath}");
                Directory.Delete(directoryPath, true);

                // Sprawdź czy katalog został usunięty
                await Task.Delay(500); // Krótka pauza na operacje systemowe

                if (!Directory.Exists(directoryPath))
                {
                    diagnostics?.Write($"Katalog został pomyślnie usunięty: {directoryPath}");
                    return true;
                }

                diagnostics?.Write($"Katalog nadal istnieje po standardowym usunięciu: {directoryPath}");
            }
            catch (Exception ex)
            {
                diagnostics?.Write($"Błąd podczas standardowego usuwania: {ex.Message}");
            }

            // Druga próba - force delete
            bool forceDeleteSuccess = await ForceDeleteDirectoryAsync(directoryPath, diagnostics);
            if (forceDeleteSuccess)
            {
                diagnostics?.Write($"Katalog został usunięty przez force delete: {directoryPath}");
                return true;
            }

            // Trzecia próba - z podniesieniem uprawnień
            if (OperatingSystem.IsWindows() && confirmElevationFunc != null)
            {
                bool elevatedSuccess = await TryDeleteWithElevatedPermissionsAsync(
                    directoryPath,
                    modName,
                    diagnostics,
                    confirmElevationFunc);

                if (elevatedSuccess)
                {
                    diagnostics?.Write($"Katalog został usunięty z podwyższonymi uprawnieniami: {directoryPath}");
                    return true;
                }
            }

            // Jeśli wszystko zawiodło
            diagnostics?.Write($"BŁĄD: Nie udało się usunąć katalogu: {directoryPath}");
            return false;
        }

        /// <summary>
        /// Wymusza usunięcie katalogu poprzez:
        /// 1. Usunięcie atrybutów ReadOnly ze wszystkich plików i folderów
        /// 2. Zamknięcie procesów które mogą blokować pliki
        /// 3. Ponowną próbę usunięcia
        /// </summary>
        private static async Task<bool> ForceDeleteDirectoryAsync(
            string directoryPath,
            IDiagnosticsOutput? diagnostics = null)
        {
            try
            {
                diagnostics?.Write($"Rozpoczynanie force delete dla: {directoryPath}");

                // Usuń atrybut tylko do odczytu ze wszystkich plików i folderów
                await Task.Run(() => RemoveReadOnlyAttributes(directoryPath, diagnostics));

                // Zamknij wszystkie procesy które mogą blokować pliki
                await Task.Run(() => KillProcessesUsingDirectory(directoryPath, diagnostics));

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
                diagnostics?.Write($"Błąd podczas force delete: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Usuwa atrybut ReadOnly ze wszystkich plików i podkatalogów w podanym katalogu
        /// </summary>
        private static void RemoveReadOnlyAttributes(string directoryPath, IDiagnosticsOutput? diagnostics = null)
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
                        diagnostics?.Write($"Nie udało się usunąć atrybutu ReadOnly z pliku {file}: {ex.Message}");
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
                        diagnostics?.Write($"Nie udało się usunąć atrybutu ReadOnly z katalogu {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics?.Write($"Błąd podczas usuwania atrybutów ReadOnly: {ex.Message}");
            }
        }

        /// <summary>
        /// Zamyka procesy Among Us które mogą blokować pliki w podanym katalogu
        /// </summary>
        private static void KillProcessesUsingDirectory(string directoryPath, IDiagnosticsOutput? diagnostics = null)
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
                                diagnostics?.Write($"Zamykanie procesu {processName} (PID: {process.Id})");
                                process.Kill();
                                process.WaitForExit(5000); // Czekaj maksymalnie 5 sekund
                            }
                        }
                        catch (Exception ex)
                        {
                            diagnostics?.Write($"Nie udało się zamknąć procesu {processName}: {ex.Message}");
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
                diagnostics?.Write($"Błąd podczas zamykania procesów: {ex.Message}");
            }
        }

        /// <summary>
        /// Próbuje usunąć katalog z podwyższonymi uprawnieniami (administrator)
        /// Wymaga potwierdzenia użytkownika poprzez confirmFunc
        /// </summary>
        private static async Task<bool> TryDeleteWithElevatedPermissionsAsync(
            string directoryPath,
            string modName,
            IDiagnosticsOutput? diagnostics,
            Func<string, string, Task<bool>> confirmFunc)
        {
            try
            {
                // Pokaż dialog użytkownikowi
                string message = $"Nie udało się usunąć katalogu moda '{modName}' standardowymi metodami.\n\n" +
                    $"Katalog: {directoryPath}\n\n" +
                    "Czy chcesz spróbować usunąć go z podwyższonymi uprawnieniami?\n" +
                    "(Może pojawić się okno UAC)";

                bool userConfirmed = await confirmFunc(message, "Wymagane podwyższone uprawnienia");

                if (!userConfirmed)
                {
                    return false;
                }

                // Tylko na Windows
                if (!OperatingSystem.IsWindows())
                {
                    diagnostics?.Write("Usuwanie z podwyższonymi uprawnieniami jest dostępne tylko na Windows.");
                    return false;
                }

                return await DeleteWithElevatedPermissionsWindows(directoryPath, diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics?.Write($"Błąd podczas próby usunięcia z podwyższonymi uprawnieniami: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Usuwa katalog z podwyższonymi uprawnieniami na Windows poprzez PowerShell lub CMD
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static async Task<bool> DeleteWithElevatedPermissionsWindows(
            string directoryPath,
            IDiagnosticsOutput? diagnostics)
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
                        diagnostics?.Write("Nie udało się uruchomić PowerShell z uprawnieniami administratora");
                        return false;
                    }

                    // Czekaj na zakończenie procesu
                    await Task.Run(() => process.WaitForExit());

                    // Sprawdź czy katalog został usunięty
                    await Task.Delay(1000);
                    bool success = !Directory.Exists(directoryPath);

                    diagnostics?.Write($"PowerShell z uprawnieniami administratora - sukces: {success}");
                    return success;
                }
            }
            catch (Exception ex)
            {
                diagnostics?.Write($"Błąd podczas usuwania z PowerShell: {ex.Message}");

                // Jeśli PowerShell zawiódł, spróbuj cmd
                return await DeleteWithCmdElevated(directoryPath, diagnostics);
            }
        }

        /// <summary>
        /// Fallback: usuwa katalog przez CMD z podwyższonymi uprawnieniami
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static async Task<bool> DeleteWithCmdElevated(string directoryPath, IDiagnosticsOutput? diagnostics)
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
                diagnostics?.Write($"Błąd podczas usuwania z CMD: {ex.Message}");
                return false;
            }
        }
    }
}

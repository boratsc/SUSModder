using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    public class DllModificationService
    {
        private readonly ConfigService _configService;
        // Statyczny HttpClient współdzielony przez wszystkie instancje (best practice)
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly IDiagnosticsOutput _diagnosticsOutput;



        public DllModificationService(ConfigService configService, IDiagnosticsOutput diagnosticsOutput)
        {
            _configService = configService;
            _diagnosticsOutput = diagnosticsOutput;
        }

        /// <summary>
        /// Pobiera listę ID modów DLL zainstalowanych w danym modzie FULL
        /// </summary>
        /// <param name="targetMod">Mod docelowy (FULL)</param>
        /// <returns>Lista ID zainstalowanych DLL</returns>
        public async Task<List<int>> GetInstalledDllIdsAsync(ModConfiguration targetMod)
        {
            if (targetMod == null || string.IsNullOrEmpty(targetMod.InstallPath))
                return new List<int>();

            try
            {
                var installationMap = await InstallationMapManager.LoadInstallationMapAsync(targetMod.InstallPath);
                
                if (installationMap == null || installationMap.InstalledDlls == null)
                    return new List<int>();

                return installationMap.InstalledDlls
                    .Where(dll => dll.ModId > 0)
                    .Select(dll => dll.ModId)
                    .ToList();
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error loading installed DLL IDs: {ex.Message}");
                return new List<int>();
            }
        }

        public List<ModConfiguration> GetDllMods()
        {
            try
            {
                var configs = _configService.LoadConfig();
                return configs
                    .Where(m => m.ModType.Equals("dll", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.ModName)
                    .ToList();
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error loading DLL mods: {ex.Message}");
                return new List<ModConfiguration>();
            }
        }

        public List<ModConfiguration> GetAvailableFullMods()
        {
            try
            {
                var configs = _configService.LoadConfig();
                return configs
                    .Where(m => m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrEmpty(m.InstallPath))  // Usuń warunek o GitHubRepoOrLink
                    .OrderBy(m => m.ModName)
                    .ToList();
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error loading available full mods: {ex.Message}");
                return new List<ModConfiguration>();
            }
        }
        public List<ModConfiguration> GetModsWithDllInstalled(ModConfiguration dllMod, string platform)
        {
            try
            {
                var configs = _configService.LoadConfig();
                var installedFullMods = configs
                    .Where(m => m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrEmpty(m.InstallPath))
                    .Where(m => IsDllInstalledInMod(dllMod, m, platform))
                    .OrderBy(m => m.ModName)
                    .ToList();

                return installedFullMods;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error loading mods with DLL installed: {ex.Message}");
                return new List<ModConfiguration>();
            }
        }

        public List<ModConfiguration> GetModsWithoutDllInstalled(ModConfiguration dllMod, string platform)
        {
            try
            {
                var configs = _configService.LoadConfig();
                var availableFullMods = configs
                    .Where(m => m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrEmpty(m.InstallPath))
                    .Where(m => !IsDllInstalledInMod(dllMod, m, platform))
                    .OrderBy(m => m.ModName)
                    .ToList();

                return availableFullMods;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error loading mods without DLL installed: {ex.Message}");
                return new List<ModConfiguration>();
            }
        }
        public async Task<string?> InstallDllToModAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            try
            {
                _diagnosticsOutput.Write($"Starting DLL installation: {dllMod.ModName} to {targetMod.ModName}");

                // Sprawdź czy targetMod ma InstallPath
                if (string.IsNullOrEmpty(targetMod.InstallPath))
                {
                    _diagnosticsOutput.Write("Target mod has no install path");
                    return null;
                }

                // Wybierz odpowiedni link do pobrania
                string downloadUrl = await GetDllDownloadUrlAsync(dllMod, platform);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _diagnosticsOutput.Write("No download URL available for this platform");
                    return null;
                }

                // Wyciągnij nazwę pliku z oryginalnego linku (nie CDN URL)
                string fileName = GetDllFileName(dllMod, platform);
                if (string.IsNullOrEmpty(fileName))
                {
                    _diagnosticsOutput.Write("Could not determine DLL file name from source URL");
                    return null;
                }
                _diagnosticsOutput.Write($"DLL file name: {fileName}");

                // Ścieżka docelowa - uwzględnij strukturę Epic (podkatalog AmongUs)
                string actualModPath = PathSettings.GetActualModPath(targetMod.InstallPath);
                _diagnosticsOutput.Write($"Base install path: {targetMod.InstallPath}");
                _diagnosticsOutput.Write($"Actual mod path (with Epic AmongUs check): {actualModPath}");
                if (!ModPackInstaller.TryResolveSafeDllDirectory(actualModPath, dllMod.DllInstallPath, out var targetDirectory) ||
                    !ModPackInstaller.TryResolveSafeDllPath(targetDirectory, fileName, out var targetPath))
                {
                    _diagnosticsOutput.Write("Unsafe DLL target path blocked");
                    return null;
                }

                string? targetDirectoryNullable = Path.GetDirectoryName(targetPath);

                // Sprawdź czy GetDirectoryName zwróciło null
                if (string.IsNullOrEmpty(targetDirectoryNullable))
                {
                    _diagnosticsOutput.Write("Could not determine target directory");
                    return null;
                }

                _diagnosticsOutput.Write($"Target path: {targetPath}");

                // Sprawdź czy plik już istnieje
                if (File.Exists(targetPath))
                {
                    _diagnosticsOutput.Write($"File {fileName} already exists, will be overwritten");
                }

                // Utwórz katalog jeśli nie istnieje
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    _diagnosticsOutput.Write($"Created directory: {targetDirectory}");
                }

                // Pobierz i zapisz plik (safe replace: temp → atomic overwrite)
                _diagnosticsOutput.Write($"Downloading from: {downloadUrl}");
                var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsByteArrayAsync();

                // Zapisz do pliku tymczasowego (w tym samym katalogu co docelowy, żeby
                // rename był atomowy – na tym samym wolumenie)
                string tempPath = targetPath + ".tmp";
                string? backupPath = null;
                try
                {
                    await File.WriteAllBytesAsync(tempPath, content);

                    // Jeśli plik docelowy istnieje, zrób backup przed nadpisaniem
                    if (File.Exists(targetPath))
                    {
                        backupPath = targetPath + ".bak";
                        File.Move(targetPath, backupPath, overwrite: true);
                    }

                    // Atomowe zastąpienie: rename temp → target
                    File.Move(tempPath, targetPath, overwrite: true);
                    _diagnosticsOutput.Write($"DLL installation completed successfully");

                    // Jeśli backup istniał i wszystko się udało, usuń go
                    if (backupPath != null && File.Exists(backupPath))
                    {
                        try { File.Delete(backupPath); }
                        catch { /* backup może zostać */ }
                    }
                }
                catch (Exception writeEx)
                {
                    _diagnosticsOutput.Write($"[ERROR] DLL write failed, preserving old DLL: {writeEx.Message}");

                    // Przywróć backup jeśli operacja się nie udała
                    if (backupPath != null && File.Exists(backupPath) && !File.Exists(targetPath))
                    {
                        try { File.Move(backupPath, targetPath); }
                        catch { /* najlepsza próba przywrócenia */ }
                    }

                    // Posprzątaj plik tymczasowy
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch { /* nic nie robimy */ }

                    return null;
                }
                finally
                {
                    // Posprzątaj plik tymczasowy (na wypadek gdyby został)
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch { /* nic nie robimy */ }
                }

                // === NOWY KOD: Zaktualizuj Installation Map ===
                try
                {
                    var installationMap = await InstallationMapManager.LoadInstallationMapAsync(targetMod.InstallPath);

                    if (installationMap != null)
                    {
                        // Sprawdź czy DLL już istnieje w mapie
                        var existingDll = installationMap.InstalledDlls
                            .FirstOrDefault(d => d.ModId == dllMod.Id);

                        if (existingDll != null)
                        {
                            // Aktualizuj istniejący wpis
                            existingDll.ModVersion = dllMod.ModVersion ?? "unknown";
                            existingDll.LastUpdated = DateTime.Now;
                            existingDll.InstalledFrom = downloadUrl;
                            _diagnosticsOutput.Write($"[InstallationMap] Zaktualizowano DLL {dllMod.ModName} w mapie");
                        }
                        else
                        {
                            // Dodaj nowy wpis - zapisz względną ścieżkę z uwzględnieniem podkatalogu AmongUs dla Epic
                            string relativePrefix = actualModPath != targetMod.InstallPath ? "AmongUs\\" : "";
                            var relativePath = Path.Combine(relativePrefix + (dllMod.DllInstallPath ?? "BepInEx\\plugins"), fileName);
                            installationMap.InstalledDlls.Add(new DllModInstallation
                            {
                                ModId = dllMod.Id,
                                ModName = dllMod.ModName,
                                ModVersion = dllMod.ModVersion ?? "unknown",
                                InstallPath = relativePath,
                                InstalledFrom = downloadUrl,
                                InstalledAt = DateTime.Now,
                                LastUpdated = DateTime.Now
                            });
                            _diagnosticsOutput.Write($"[InstallationMap] Dodano DLL {dllMod.ModName} do mapy");
                        }

                        await InstallationMapManager.SaveInstallationMapAsync(targetMod.InstallPath, installationMap);
                        _diagnosticsOutput.Write($"[InstallationMap] Zapisano mapę instalacji dla {targetMod.ModName}");
                    }
                    else
                    {
                        _diagnosticsOutput.Write($"[WARNING] Brak Installation Map dla {targetMod.ModName} - pominięto aktualizację");
                    }
                }
                catch (Exception ex)
                {
                    _diagnosticsOutput.Write($"[WARNING] Nie udało się zaktualizować Installation Map: {ex.Message}");
                    // Nie przerywamy operacji jeśli aktualizacja mapy się nie powiodła
                }
                // === KONIEC NOWEGO KODU ===

                return targetPath;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error installing DLL: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UninstallDllFromModAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            await Task.CompletedTask;
            try
            {
                _diagnosticsOutput.Write($"Starting DLL uninstallation: {dllMod.ModName} from {targetMod.ModName}");

                // Sprawdź czy targetMod ma InstallPath
                if (string.IsNullOrEmpty(targetMod.InstallPath))
                {
                    _diagnosticsOutput.Write("Target mod has no install path");
                    return false;
                }

                // Wyciągnij nazwę pliku z oryginalnego linku (nie CDN URL)
                string fileName = GetDllFileName(dllMod, platform);
                if (string.IsNullOrEmpty(fileName))
                {
                    _diagnosticsOutput.Write("Cannot determine file name for removal");
                    return false;
                }
                _diagnosticsOutput.Write($"DLL file name to remove: {fileName}");

                // Ścieżka do pliku - uwzględnij strukturę Epic (podkatalog AmongUs)
                string actualModPath = PathSettings.GetActualModPath(targetMod.InstallPath);
                if (!ModPackInstaller.TryResolveSafeDllDirectory(actualModPath, dllMod.DllInstallPath, out var targetDirectory) ||
                    !ModPackInstaller.TryResolveSafeDllPath(targetDirectory, fileName, out var filePath))
                {
                    _diagnosticsOutput.Write("Unsafe DLL target path blocked");
                    return false;
                }
                _diagnosticsOutput.Write($"File path to remove: {filePath}");

                if (!File.Exists(filePath))
                {
                    _diagnosticsOutput.Write($"File {fileName} not found in {targetMod.ModName}");
                    return false;
                }

                // Usuń plik
                File.Delete(filePath);
                _diagnosticsOutput.Write($"DLL uninstallation completed successfully");

                // === NOWY KOD: Zaktualizuj Installation Map ===
                try
                {
                    var installationMap = await InstallationMapManager.LoadInstallationMapAsync(targetMod.InstallPath);

                    if (installationMap != null)
                    {
                        // Usuń DLL z mapy
                        var dllToRemove = installationMap.InstalledDlls
                            .FirstOrDefault(d => d.ModId == dllMod.Id);

                        if (dllToRemove != null)
                        {
                            installationMap.InstalledDlls.Remove(dllToRemove);
                            await InstallationMapManager.SaveInstallationMapAsync(targetMod.InstallPath, installationMap);
                            _diagnosticsOutput.Write($"[InstallationMap] Usunięto DLL {dllMod.ModName} z mapy");
                        }
                        else
                        {
                            _diagnosticsOutput.Write($"[InstallationMap] DLL {dllMod.ModName} nie był w mapie");
                        }
                    }
                    else
                    {
                        _diagnosticsOutput.Write($"[WARNING] Brak Installation Map dla {targetMod.ModName} - pominięto aktualizację");
                    }
                }
                catch (Exception ex)
                {
                    _diagnosticsOutput.Write($"[WARNING] Nie udało się zaktualizować Installation Map: {ex.Message}");
                    // Nie przerywamy operacji jeśli aktualizacja mapy się nie powiodła
                }
                // === KONIEC NOWEGO KODU ===

                return true;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error uninstalling DLL: {ex.Message}");
                return false;
            }
        }

        public bool IsDllInstalledInMod(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            try
            {
                // Sprawdź czy targetMod ma InstallPath
                if (string.IsNullOrEmpty(targetMod.InstallPath))
                {
                    return false;
                }

                string fileName = GetDllFileName(dllMod, platform);
                if (string.IsNullOrEmpty(fileName)) return false;
                
                // Uwzględnij strukturę Epic (podkatalog AmongUs)
                string actualModPath = PathSettings.GetActualModPath(targetMod.InstallPath);
                if (!ModPackInstaller.TryResolveSafeDllDirectory(actualModPath, dllMod.DllInstallPath, out var targetDirectory) ||
                    !ModPackInstaller.TryResolveSafeDllPath(targetDirectory, fileName, out var filePath))
                {
                    _diagnosticsOutput.Write("Unsafe DLL target path blocked while checking installation status");
                    return false;
                }

                return File.Exists(filePath);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error checking DLL installation status: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetDllDownloadUrlAsync(ModConfiguration dllMod, string platform)
        {
            return await ModDownloadUrlBuilder.ResolveAsync(dllMod, platform);
        }

        private string GetDllFileName(ModConfiguration dllMod, string platform)
        {
            return ModDownloadUrlBuilder.GetDllFileName(dllMod, platform);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

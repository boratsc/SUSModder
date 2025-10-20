using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;

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
        public async Task<bool> InstallDllToModAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            try
            {
                _diagnosticsOutput.Write($"Starting DLL installation: {dllMod.ModName} to {targetMod.ModName}");

                // Sprawdź czy targetMod ma InstallPath
                if (string.IsNullOrEmpty(targetMod.InstallPath))
                {
                    _diagnosticsOutput.Write("Target mod has no install path");
                    return false;
                }

                // Wybierz odpowiedni link do pobrania
                string downloadUrl = GetDllDownloadUrl(dllMod, platform);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _diagnosticsOutput.Write("No download URL available for this platform");
                    return false;
                }

                // Wyciągnij nazwę pliku z URL
                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                _diagnosticsOutput.Write($"DLL file name: {fileName}");

                // Ścieżka docelowa - teraz targetMod.InstallPath jest sprawdzone
                string targetPath = Path.Combine(targetMod.InstallPath, dllMod.DllInstallPath ?? "BepInEx\\plugins", fileName);
                string? targetDirectoryNullable = Path.GetDirectoryName(targetPath);

                // Sprawdź czy GetDirectoryName zwróciło null
                if (string.IsNullOrEmpty(targetDirectoryNullable))
                {
                    _diagnosticsOutput.Write("Could not determine target directory");
                    return false;
                }

                string targetDirectory = targetDirectoryNullable;

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

                // Pobierz i zapisz plik
                _diagnosticsOutput.Write($"Downloading from: {downloadUrl}");
                var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(targetPath, content);

                _diagnosticsOutput.Write($"DLL installation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error installing DLL: {ex.Message}");
                return false;
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

                // Wybierz odpowiedni link
                string downloadUrl = GetDllDownloadUrl(dllMod, platform);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _diagnosticsOutput.Write("Cannot determine file name for removal");
                    return false;
                }

                // Wyciągnij nazwę pliku z URL
                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                _diagnosticsOutput.Write($"DLL file name to remove: {fileName}");

                // Ścieżka do pliku - teraz targetMod.InstallPath jest sprawdzone
                string filePath = Path.Combine(targetMod.InstallPath, dllMod.DllInstallPath ?? "BepInEx\\plugins", fileName);
                _diagnosticsOutput.Write($"File path to remove: {filePath}");

                if (!File.Exists(filePath))
                {
                    _diagnosticsOutput.Write($"File {fileName} not found in {targetMod.ModName}");
                    return false;
                }

                // Usuń plik
                File.Delete(filePath);
                _diagnosticsOutput.Write($"DLL uninstallation completed successfully");
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

                string downloadUrl = GetDllDownloadUrl(dllMod, platform);
                if (string.IsNullOrEmpty(downloadUrl)) return false;

                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                string filePath = Path.Combine(targetMod.InstallPath, dllMod.DllInstallPath ?? "BepInEx\\plugins", fileName);

                return File.Exists(filePath);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Error checking DLL installation status: {ex.Message}");
                return false;
            }
        }

        private string GetDllDownloadUrl(ModConfiguration dllMod, string platform)
        {
            // Jeśli Epic i ma EpicGitHubRepoOrLink
            if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(dllMod.EpicGitHubRepoOrLink))
            {
                return dllMod.EpicGitHubRepoOrLink;
            }

            // W przeciwnym razie użyj standardowego linku
            return dllMod.GitHubRepoOrLink ?? string.Empty;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

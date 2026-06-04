using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;

namespace SUSModder.Core.GameIntegration
{
    public static class GameLocator
    {
        /// <summary>
        /// Wyszukuje ścieżkę do Among Us w bibliotekach Steam, rejestrze i typowych lokalizacjach dysków.
        /// </summary>
        public static string? TryFindSteamPath() => AmongUsPathDiscovery.TryFindInstallDirectory();

        /// <summary>
        /// Sprawdza, czy katalog zawiera plik Among Us.exe.
        /// </summary>
        public static bool IsValidAmongUsInstallDirectory(string? path) =>
            AmongUsPathDiscovery.IsValidInstallDirectory(path);

        /// <summary>
        /// Rejestruje lub aktualizuje wpis Vanilla na podstawie wskazanej ścieżki katalogu gry.
        /// </summary>
        public static ModConfiguration? TryRegisterSteamVanillaPath(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            string installDirectory)
        {
            if (!AmongUsPathDiscovery.IsValidInstallDirectory(installDirectory))
                return null;

            var userSettingsService = new UserSettingsService();
            var userMode = userSettingsService.LoadUserSettings().Mode;
            if (string.IsNullOrWhiteSpace(userMode))
                userMode = "steam";

            var normalizedPath = Path.GetFullPath(installDirectory.TrimEnd('\\', '/'));
            var existing = modConfigs.FirstOrDefault(x =>
                x.ModName == "AmongUs" &&
                x.Id == 0 &&
                !string.IsNullOrEmpty(x.InstallPath));

            if (existing != null)
            {
                existing.InstallPath = normalizedPath;
                existing.AmongVersion = GetGameVersion(normalizedPath);
                existing.LastUpdated = DateTime.Now;
                ConfigManager.SaveConfig(modConfigs);
                userSettingsService.UpdateUserSetting(settings => settings.VanillaInstallPath = normalizedPath);
                return existing;
            }

            return SaveVanillaMod(modConfigs, configuration, userSettingsService, userMode, normalizedPath);
        }

        /// <summary>
        /// Sprawdza i konfiguruje Vanilla mod (podstawowa gra Among Us).
        /// 
        /// Dla Steam: automatycznie szuka ścieżki (Steam, rejestr, manifesty, dyski).
        ///            Jeśli nie znajdzie — setup zostaje odłożony do pierwszego uruchomienia gry.
        /// 
        /// Dla Epic: NIE szuka ścieżki - gra jest zarządzana przez legendary.exe.
        ///           Sprawdza przez "legendary list-games" czy użytkownik posiada Among Us na koncie.
        ///           InstallPath ustawiony na katalog modów vanilla (analogicznie do innych modów Epic).
        ///           Jeśli legendary niedostępny lub użytkownik nie jest zalogowany - setup jest odkładany
        ///           (auth nastąpi przy pierwszym "Uruchom", tak jak dla każdego moda Epic).
        /// </summary>
        public static async Task<ModConfiguration?> CheckAndSetupVanillaModAsync(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            IUserInteraction? userInteraction = null)
        {
            var existingConfig = modConfigs.FirstOrDefault(x => x.ModName == "AmongUs" &&
                                                                x.Id == 0 &&
                                                                !string.IsNullOrEmpty(x.InstallPath));

            if (existingConfig != null)
            {
                Console.WriteLine("Among Us już zainstalowano z wersją Vanilla.");
                return null; // już istnieje
            }

            // Pobierz platformę z UserSettings (wybraną przez użytkownika przy pierwszym uruchomieniu)
            var userSettingsService = new UserSettingsService();
            var userMode = userSettingsService.LoadUserSettings().Mode;

            if (string.IsNullOrEmpty(userMode))
            {
                Console.WriteLine("Platforma nie została wybrana przez użytkownika.");
                return null;
            }

            if (string.Equals(userMode, "epic", StringComparison.OrdinalIgnoreCase))
            {
                return await SetupEpicVanillaAsync(modConfigs, configuration, userSettingsService, userMode);
            }
            else
            {
                return SetupSteamVanilla(modConfigs, configuration, userSettingsService, userMode);
            }
        }

        /// <summary>
        /// Konfiguruje Vanilla mod dla Steam wyłącznie przez auto-wykrywanie.
        /// </summary>
        private static ModConfiguration? SetupSteamVanilla(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            UserSettingsService userSettingsService,
            string userMode)
        {
            string? foundPath = TryFindSteamPath();

            if (foundPath == null)
            {
                Console.WriteLine("[Steam Vanilla] Nie wykryto Among Us automatycznie — monit po wystartowaniu aplikacji.");
                return null;
            }

            Console.WriteLine($"[Steam Vanilla] Wykryto Among Us: {foundPath}");
            return SaveVanillaMod(modConfigs, configuration, userSettingsService, userMode, foundPath);
        }

        /// <summary>
        /// Konfiguruje Vanilla mod dla Epic Games.
        /// Nie szuka ścieżki instalacji - gra zarządzana jest przez legendary.exe.
        /// Sprawdza czy użytkownik posiada Among Us na koncie Epic przez "legendary list-games".
        /// InstallPath jest budowany analogicznie do innych modów Epic.
        /// </summary>
        private static async Task<ModConfiguration?> SetupEpicVanillaAsync(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            UserSettingsService userSettingsService,
            string userMode)
        {
            Console.WriteLine("[Epic Vanilla] Sprawdzanie posiadania Among Us na koncie Epic Games...");

            // Sprawdź czy legendary.exe jest dostępny
            string legendaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legendary.exe");
            if (!File.Exists(legendaryPath))
            {
                // legendary nie jest jeszcze pobrany - setup vanilla zostanie wykonany po pobraniu legendary
                // (auth flow w App.axaml.cs pobierze legendary przed tym krokiem, ale bądźmy defensywni)
                Console.WriteLine("[Epic Vanilla] legendary.exe nie istnieje - odkładam setup vanilla na później.");
                return null;
            }

            // Użyj EpicVersionManager do sprawdzenia posiadania gry
            // Tworzymy minimalny output handler tylko do logowania
            var diagnosticsOutput = new ConsoleDiagnosticsOutput();
            // EpicVersionManager wymaga IEpicUserInteraction - przekazujemy null-safe adapter
            var nullInteraction = new NullEpicUserInteraction();
            var epicManager = new EpicVersionManager(diagnosticsOutput, nullInteraction);

            bool isOwned = await epicManager.CheckIsGameOwnedAsync();

            if (!isOwned)
            {
                // Użytkownik nie jest zalogowany lub nie posiada gry.
                // Auth nastąpi przy pierwszym "Uruchom" - nie blokuj setup.
                Console.WriteLine("[Epic Vanilla] Among Us nie znaleziony na koncie Epic lub użytkownik niezalogowany. Vanilla mod zostanie skonfigurowany po zalogowaniu.");
                return null;
            }

            // Zbuduj ścieżkę InstallPath analogicznie do innych modów Epic:
            // {ModsInstallPath}\Among Us - Vanilla\AmongUs
            string installPath = Path.Combine(PathSettings.ModsInstallPath, "Among Us - Vanilla", "AmongUs");
            Console.WriteLine($"[Epic Vanilla] Among Us znaleziony na koncie Epic. InstallPath: {installPath}");

            return SaveVanillaMod(modConfigs, configuration, userSettingsService, userMode, installPath);
        }

        /// <summary>
        /// Tworzy i zapisuje konfigurację Vanilla modu do config.json.
        /// </summary>
        private static ModConfiguration SaveVanillaMod(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            UserSettingsService userSettingsService,
            string userMode,
            string installPath)
        {
            var vanillaMod = new ModConfiguration
            {
                ModName = "AmongUs",
                PngFileName = "Vanilla.png",
                InstallPath = installPath,
                GitHubRepoOrLink = string.Empty,
                EpicGitHubRepoOrLink = string.Empty,
                ModType = "Vanilla",
                DllInstallPath = null,
                ModVersion = "Vanilla",
                LastUpdated = DateTime.Now,
                AmongVersion = GetGameVersion(installPath),
                Description = $"Platform: {userMode}"
            };

            // Dodaj do listy i zapisz
            modConfigs.Add(vanillaMod);
            ConfigManager.SaveConfig(modConfigs);

            // Zapisz ścieżkę Vanilla do user-settings (fallback po update)
            userSettingsService.UpdateUserSetting(settings => settings.VanillaInstallPath = installPath);

            // Mode jest zapisywany w user-settings (wywoływane przez caller)
            // appsettings.json pozostaje read-only – zapis został usunięty (SQLite migration)

            Console.WriteLine($"Among Us Vanilla ({userMode}) został dodany do listy modów. InstallPath: {installPath}");

            return vanillaMod;
        }

        private static string GetGameVersion(string path)
        {
            try
            {
                // 1) Preferuj wersję runtime wyciągniętą z globalgamemanagers
                // (dokładniejsza dla Among Us niż FileVersion z EXE).
                var dataVersion = TryGetVersionFromGlobalGameManagers(path);
                if (!string.IsNullOrWhiteSpace(dataVersion))
                    return dataVersion;

                // 2) Fallback: wersja pliku EXE
                var exePath = Path.Combine(path, "Among Us.exe");
                if (File.Exists(exePath))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(versionInfo.FileVersion))
                        return versionInfo.FileVersion;
                }

                return "Nieznana";
            }
            catch
            {
                Console.WriteLine($"Nie udało się odczytać wersji gry.");
                return "Nieznana";
            }
        }

        private static string? TryGetVersionFromGlobalGameManagers(string path)
        {
            try
            {
                var managersPath = Path.Combine(path, "Among Us_Data", "globalgamemanagers");
                if (!File.Exists(managersPath))
                    return null;

                var bytes = File.ReadAllBytes(managersPath);
                var asciiChunk = new StringBuilder();
                var regex = new Regex(@"\b(\d{4}\.\d{1,2}\.\d{1,2}f?)\b", RegexOptions.Compiled);

                foreach (var b in bytes)
                {
                    if (b >= 32 && b <= 126)
                    {
                        asciiChunk.Append((char)b);
                        continue;
                    }

                    var version = ExtractVersionFromChunk(asciiChunk.ToString(), regex);
                    if (!string.IsNullOrWhiteSpace(version))
                        return NormalizeUnityStyleVersion(version);

                    asciiChunk.Clear();
                }

                var trailing = ExtractVersionFromChunk(asciiChunk.ToString(), regex);
                return string.IsNullOrWhiteSpace(trailing) ? null : NormalizeUnityStyleVersion(trailing);
            }
            catch
            {
                return null;
            }
        }

        private static string? ExtractVersionFromChunk(string chunk, Regex regex)
        {
            if (string.IsNullOrWhiteSpace(chunk) || chunk.Length < 4)
                return null;

            foreach (Match match in regex.Matches(chunk))
            {
                var version = match.Groups[1].Value;
                if (version.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                    continue;

                return version;
            }

            return null;
        }

        private static string NormalizeUnityStyleVersion(string version)
        {
            return version == "2025.4.20" ? "2025.5.20" : version;
        }

        /// <summary>
        /// Minimalny adapter IDiagnosticsOutput przekazujący logi do Console.
        /// Używany wewnętrznie przez GameLocator przy wywołaniu EpicVersionManager.
        /// </summary>
        private class ConsoleDiagnosticsOutput : IDiagnosticsOutput
        {
            public void Write(string message) => Console.WriteLine(message);
        }

        /// <summary>
        /// Null-safe adapter IEpicUserInteraction używany przy sprawdzaniu posiadania gry
        /// (nie wymaga interakcji z użytkownikiem).
        /// </summary>
        private class NullEpicUserInteraction : IEpicUserInteraction
        {
            public bool Confirm(string message) => false;
            public void ShowError(string message) => Console.WriteLine($"[EpicError] {message}");
            public string? Prompt(string message, string title = "") => null;
            public Task<string?> ShowEpicAuthDialogAsync(string browserUrl) => Task.FromResult<string?>(null);
        }
    }
}

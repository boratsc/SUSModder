using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Repositories;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Configuration
{
    public class ModConfiguration : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _isSelected;
        private bool _isInstalled;
        private string? _compatibilityEmoji;
        private string? _compatibilityDescription;
        private string? _compatibilityWarning;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        /// <summary>
        /// Pole dynamiczne wskazujące czy mod DLL jest zainstalowany w docelowym modzie FULL
        /// (nie serializowane do JSON)
        /// </summary>
        [JsonIgnore]
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled != value)
                {
                    _isInstalled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
                }
            }
        }

        /// <summary>
        /// Emoji kompatybilności (dla UI)
        /// </summary>
        [JsonIgnore]
        public string? CompatibilityEmoji
        {
            get => _compatibilityEmoji;
            set
            {
                if (_compatibilityEmoji != value)
                {
                    _compatibilityEmoji = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompatibilityEmoji)));
                }
            }
        }

        /// <summary>
        /// Opis kompatybilności (dla UI)
        /// </summary>
        [JsonIgnore]
        public string? CompatibilityDescription
        {
            get => _compatibilityDescription;
            set
            {
                if (_compatibilityDescription != value)
                {
                    _compatibilityDescription = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompatibilityDescription)));
                }
            }
        }

        /// <summary>
        /// Ostrzeżenie o kompatybilności (dla UI)
        /// </summary>
        [JsonIgnore]
        public string? CompatibilityWarning
        {
            get => _compatibilityWarning;
            set
            {
                if (_compatibilityWarning != value)
                {
                    _compatibilityWarning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompatibilityWarning)));
                }
            }
        }

        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("ModName")]
        public string ModName { get; set; } = string.Empty;

        [JsonPropertyName("PngFileName")]
        public string PngFileName { get; set; } = string.Empty;

        [JsonPropertyName("InstallPath")]
        public string? InstallPath { get; set; }

        [JsonPropertyName("GitHubRepoOrLink")]
        public string GitHubRepoOrLink { get; set; } = string.Empty;

        [JsonPropertyName("EpicGitHubRepoOrLink")]
        public string? EpicGitHubRepoOrLink { get; set; }

        [JsonPropertyName("ModType")]
        public string ModType { get; set; } = string.Empty;

        [JsonPropertyName("DllInstallPath")]
        public string? DllInstallPath { get; set; }

        [JsonPropertyName("ModVersion")]
        public string ModVersion { get; set; } = string.Empty;

        [JsonPropertyName("LastUpdated")]
        public DateTime? LastUpdated { get; set; }

        [JsonPropertyName("AmongVersion")]
        public string AmongVersion { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("HasRoles")]
        public bool? HasRoles { get; set; }
    }

    public static class ConfigManager
    {
        private static readonly string exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        private static readonly string configFilePath = Path.Combine(exeDir, "config.json");
        private static readonly string appSettingsFilePath = Path.Combine(exeDir, "appsettings.json");

        public static List<ModConfiguration> LoadConfig()
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
            var configRepo = new ConfigRepository(exeDir);

            System.Diagnostics.Debug.WriteLine($"Looking for config in: {exeDir}");

            var localConfigs = configRepo.LoadConfig();
            System.Diagnostics.Debug.WriteLine($"Local configs count: {localConfigs.Count}");

            if (localConfigs.Count > 0)
            {
                var vanillaUpdated = EnsureVanillaConfigPresent(localConfigs);
                if (vanillaUpdated)
                {
                    configRepo.SaveConfig(localConfigs);
                    System.Diagnostics.Debug.WriteLine("[ConfigManager] Vanilla config restored/updated in local config.");
                }
                else
                {
                    PersistVanillaPathIfNeeded(localConfigs);
                }

                System.Diagnostics.Debug.WriteLine("Using local config");
                return localConfigs;
            }

            System.Diagnostics.Debug.WriteLine("Local config not found, fetching from API...");

            // Spróbuj wczytać config z poprzedniej wersji (Velopack)
            List<ModConfiguration>? previousConfigs = null;
            var previousConfigPath = PreviousVersionLocator.TryGetPreviousConfigPath();
            if (!string.IsNullOrWhiteSpace(previousConfigPath))
            {
                previousConfigs = LoadConfigFromFile(previousConfigPath);
                if (previousConfigs != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigManager] Loaded previous config: {previousConfigPath} (count: {previousConfigs.Count})");
                }
            }

            try
            {
                // Użyj Task.Run aby uniknąć deadlocka z kontekstem synchronizacji
                var apiConfigs = Task.Run(async () => await FetchConfigFromApiAsync()).GetAwaiter().GetResult();
                System.Diagnostics.Debug.WriteLine($"API returned {apiConfigs.Count} configs");

                if (apiConfigs.Count > 0)
                {
                    if (previousConfigs != null && previousConfigs.Count > 0)
                    {
                        MergeInstallDataFromPrevious(apiConfigs, previousConfigs);
                        EnsureVanillaConfigPresent(apiConfigs, previousConfigs);
                    }

                    System.Diagnostics.Debug.WriteLine("Saving API config locally...");
                    configRepo.SaveConfig(apiConfigs);
                    System.Diagnostics.Debug.WriteLine("Config saved successfully");

                    PersistVanillaPathIfNeeded(apiConfigs);
                    return apiConfigs;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API fetch failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            if (previousConfigs != null && previousConfigs.Count > 0)
            {
                EnsureVanillaConfigPresent(previousConfigs);
                configRepo.SaveConfig(previousConfigs);
                PersistVanillaPathIfNeeded(previousConfigs);
                return previousConfigs;
            }

            return new List<ModConfiguration>();
        }

        private static async Task<List<ModConfiguration>> FetchConfigFromApiAsync()
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    // Ustaw timeout na 15 sekund
                    httpClient.Timeout = TimeSpan.FromSeconds(15);

                    // Pobierz URL z appsettings.json
                    string configApiUrl = GetUpdateServerUrl();
                    System.Diagnostics.Debug.WriteLine($"Fetching config from: {configApiUrl}");

                    // Dodaj token autoryzacji tak jak w UpdateConfigMenuItem_Click
                    string downloadToken = SecretProvider.GetDownloadToken();
                    httpClient.DefaultRequestHeaders.Add("Authorization", downloadToken);

                    var response = await httpClient.GetStringAsync(configApiUrl);
                    System.Diagnostics.Debug.WriteLine($"API response received, length: {response.Length}");
                    
                    var configs = JsonSerializer.Deserialize<List<ModConfiguration>>(response) ?? new List<ModConfiguration>();
                    System.Diagnostics.Debug.WriteLine($"Deserialized {configs.Count} configurations");
                    
                    return configs;
                }
                catch (TaskCanceledException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"API request timeout: {ex.Message}");
                    return new List<ModConfiguration>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error fetching config from API: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    return new List<ModConfiguration>();
                }
            }
        }

        private static string GetUpdateServerUrl()
        {
            try
            {
                if (File.Exists(appSettingsFilePath))
                {
                    var json = File.ReadAllText(appSettingsFilePath);
                    var jsonObj = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json);

                    if (jsonObj != null &&
                        jsonObj.ContainsKey("Configuration") &&
                        jsonObj["Configuration"].ContainsKey("UpdateServerUrl"))
                    {
                        return jsonObj["Configuration"]["UpdateServerUrl"].ToString() ?? "https://susmodder.boracik.pl/api/susmodder-config";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading UpdateServerUrl from appsettings.json: {ex.Message}");
            }

            // Fallback do domyślnego URL-a
            return "https://susmodder.boracik.pl/api/susmodder-config";
        }

        public static void SaveConfig(List<ModConfiguration> configs)
        {
            var dir = Path.GetDirectoryName(configFilePath) ?? string.Empty;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, json);
        }

        private static List<ModConfiguration>? LoadConfigFromFile(string? filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<ModConfiguration>>(json) ?? new List<ModConfiguration>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigManager] Failed to load config from {filePath}: {ex.Message}");
                return null;
            }
        }

        private static bool IsVanillaConfig(ModConfiguration config)
        {
            if (config == null) return false;
            return config.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase) ||
                   config.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetVanillaInstallPath(IEnumerable<ModConfiguration> configs, out string? installPath, out ModConfiguration? vanillaConfig)
        {
            vanillaConfig = configs.FirstOrDefault(IsVanillaConfig);
            if (vanillaConfig != null && !string.IsNullOrWhiteSpace(vanillaConfig.InstallPath))
            {
                installPath = vanillaConfig.InstallPath;
                return true;
            }

            installPath = null;
            return false;
        }

        private static bool EnsureVanillaConfigPresent(List<ModConfiguration> configs, List<ModConfiguration>? previousConfigs = null)
        {
            if (TryGetVanillaInstallPath(configs, out var existingPath, out _))
            {
                PersistVanillaPathIfNeeded(existingPath);
                return false;
            }

            // 1) Spróbuj z poprzedniego configu (Velopack)
            ModConfiguration? sourceVanilla = null;
            if (previousConfigs != null)
            {
                sourceVanilla = previousConfigs.FirstOrDefault(IsVanillaConfig);
            }
            else
            {
                var prevPath = PreviousVersionLocator.TryGetPreviousConfigPath();
                var prevConfigs = LoadConfigFromFile(prevPath);
                sourceVanilla = prevConfigs?.FirstOrDefault(IsVanillaConfig);
            }

            if (sourceVanilla != null && !string.IsNullOrWhiteSpace(sourceVanilla.InstallPath))
            {
                var normalized = NormalizeAmongUsPath(sourceVanilla.InstallPath);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    sourceVanilla.InstallPath = normalized;
                    var updated = EnsureVanillaFromSource(configs, sourceVanilla);
                    System.Diagnostics.Debug.WriteLine($"[ConfigManager] Restored Vanilla path from previous config: {normalized}");
                    PersistVanillaPathIfNeeded(normalized);
                    return updated;
                }
            }

            // 2) Fallback do user-settings
            var userSettingsService = new UserSettingsService();
            var settings = userSettingsService.LoadUserSettings();
            var userPath = NormalizeAmongUsPath(settings.VanillaInstallPath);
            if (!string.IsNullOrWhiteSpace(userPath))
            {
                var vanillaFromUser = CreateVanillaConfig(userPath);
                var updated = EnsureVanillaFromSource(configs, vanillaFromUser);
                System.Diagnostics.Debug.WriteLine($"[ConfigManager] Restored Vanilla path from user-settings: {userPath}");
                return updated;
            }

            return false;
        }

        private static void PersistVanillaPathIfNeeded(List<ModConfiguration> configs)
        {
            if (TryGetVanillaInstallPath(configs, out var installPath, out _))
            {
                PersistVanillaPathIfNeeded(installPath);
            }
        }

        private static void PersistVanillaPathIfNeeded(string? installPath)
        {
            var normalized = NormalizeAmongUsPath(installPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            var userSettingsService = new UserSettingsService();
            userSettingsService.UpdateIfEmpty(
                settings => settings.VanillaInstallPath,
                (settings, value) => settings.VanillaInstallPath = value,
                normalized
            );
        }

        private static string? NormalizeAmongUsPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Jeśli podano bezpośrednio plik EXE
            if (File.Exists(path))
            {
                var fileName = Path.GetFileName(path);
                if (string.Equals(fileName, "Among Us.exe", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(path);

                return null;
            }

            var exePath = Path.Combine(path, "Among Us.exe");
            return File.Exists(exePath) ? path : null;
        }

        private static ModConfiguration CreateVanillaConfig(string installPath)
        {
            return new ModConfiguration
            {
                Id = 0,
                ModName = "AmongUs",
                PngFileName = "Vanilla.png",
                InstallPath = installPath,
                GitHubRepoOrLink = string.Empty,
                EpicGitHubRepoOrLink = string.Empty,
                ModType = "Vanilla",
                DllInstallPath = null,
                ModVersion = string.Empty,
                LastUpdated = DateTime.Now,
                AmongVersion = GetGameVersionSafe(installPath),
                Description = "Platform: unknown"
            };
        }

        private static string GetGameVersionSafe(string installPath)
        {
            try
            {
                var exePath = Path.Combine(installPath, "Among Us.exe");
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return versionInfo.FileVersion ?? "Nieznana";
            }
            catch
            {
                return "Nieznana";
            }
        }

        private static bool EnsureVanillaFromSource(List<ModConfiguration> configs, ModConfiguration source)
        {
            var existing = configs.FirstOrDefault(IsVanillaConfig);
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(existing.InstallPath))
                    return false;

                existing.InstallPath = source.InstallPath;
                if (string.IsNullOrWhiteSpace(existing.AmongVersion))
                    existing.AmongVersion = source.AmongVersion;
                if (string.IsNullOrWhiteSpace(existing.ModVersion))
                    existing.ModVersion = source.ModVersion;
                if (!existing.LastUpdated.HasValue)
                    existing.LastUpdated = source.LastUpdated;

                return true;
            }

            // Brak wpisu Vanilla - dodaj nowy
            configs.Add(new ModConfiguration
            {
                Id = source.Id,
                ModName = source.ModName,
                PngFileName = source.PngFileName,
                InstallPath = source.InstallPath,
                GitHubRepoOrLink = source.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = source.EpicGitHubRepoOrLink,
                ModType = source.ModType,
                DllInstallPath = source.DllInstallPath,
                ModVersion = source.ModVersion,
                LastUpdated = source.LastUpdated,
                AmongVersion = source.AmongVersion,
                Description = source.Description,
                HasRoles = source.HasRoles
            });

            return true;
        }

        private static void MergeInstallDataFromPrevious(List<ModConfiguration> target, List<ModConfiguration> previous)
        {
            foreach (var targetMod in target)
            {
                var prev = previous.FirstOrDefault(p =>
                    p.ModName.Equals(targetMod.ModName, StringComparison.OrdinalIgnoreCase));

                if (prev == null)
                    continue;

                if (string.IsNullOrWhiteSpace(targetMod.InstallPath) && !string.IsNullOrWhiteSpace(prev.InstallPath))
                    targetMod.InstallPath = prev.InstallPath;

                if (string.IsNullOrWhiteSpace(targetMod.ModVersion) && !string.IsNullOrWhiteSpace(prev.ModVersion))
                    targetMod.ModVersion = prev.ModVersion;

                if (string.IsNullOrWhiteSpace(targetMod.AmongVersion) && !string.IsNullOrWhiteSpace(prev.AmongVersion))
                    targetMod.AmongVersion = prev.AmongVersion;

                if (!targetMod.LastUpdated.HasValue && prev.LastUpdated.HasValue)
                    targetMod.LastUpdated = prev.LastUpdated;
            }
        }

        public static void SaveConfigurationSetting(string key, string value)
        {
            var json = File.ReadAllText(appSettingsFilePath);
            var jsonObj = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json);
            if (jsonObj != null && jsonObj.ContainsKey("Configuration"))
            {
                var configuration = jsonObj["Configuration"];
                if (configuration.ContainsKey(key))
                {
                    configuration[key] = value;
                }
                else
                {
                    configuration.Add(key, value);
                }

                var updatedJson = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(appSettingsFilePath, updatedJson);
            }
        }
        public static void SaveThemeSetting(string theme)
        {
            try
            {
                // Użyj appSettingsFilePath zamiast GetConfigPath()
                if (File.Exists(appSettingsFilePath))
                {
                    var json = File.ReadAllText(appSettingsFilePath);
                    var config = JsonSerializer.Deserialize<JsonElement>(json);

                    var configDict = new Dictionary<string, object>();

                    // Skopiuj istniejące ustawienia
                    foreach (var property in config.EnumerateObject())
                    {
                        if (property.Name == "Configuration")
                        {
                            var configSection = new Dictionary<string, object>();
                            foreach (var configProp in property.Value.EnumerateObject())
                            {
                                if (configProp.Name == "Theme")
                                {
                                    configSection[configProp.Name] = theme;
                                }
                                else
                                {
                                    configSection[configProp.Name] = configProp.Value.ToString();
                                }
                            }
                            configDict[property.Name] = configSection;
                        }
                        else
                        {
                            try
                            {
                                var deserializedValue = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                                if (deserializedValue != null)
                                {
                                    configDict[property.Name] = deserializedValue;
                                }
                                else
                                {
                                    // Użyj raw text jako fallback
                                    configDict[property.Name] = property.Value.GetRawText();
                                }
                            }
                            catch (JsonException)
                            {
                                // Jeśli deserializacja się nie powiedzie, użyj raw text
                                configDict[property.Name] = property.Value.GetRawText();
                            }
                        }
                    }

                    var updatedJson = JsonSerializer.Serialize(configDict, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(appSettingsFilePath, updatedJson);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd zapisywania motywu: {ex.Message}");
            }
        }

        public static string GetThemeSetting()
        {
            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                var configuration = configBuilder.Build();
                return configuration["Configuration:Theme"] ?? "dark";
            }
            catch
            {
                return "dark"; // domyślny motyw
            }
        }

        public static void SaveLanguageSetting(string language)
        {
            try
            {
                if (File.Exists(appSettingsFilePath))
                {
                    var json = File.ReadAllText(appSettingsFilePath);
                    var config = JsonSerializer.Deserialize<JsonElement>(json);

                    var configDict = new Dictionary<string, object>();

                    // Skopiuj istniejące ustawienia
                    foreach (var property in config.EnumerateObject())
                    {
                        if (property.Name == "Configuration")
                        {
                            var configSection = new Dictionary<string, object>();
                            bool languageFound = false;

                            foreach (var configProp in property.Value.EnumerateObject())
                            {
                                if (configProp.Name == "Language")
                                {
                                    configSection[configProp.Name] = language;
                                    languageFound = true;
                                }
                                else
                                {
                                    configSection[configProp.Name] = configProp.Value.ToString();
                                }
                            }

                            // Jeśli parametr Language nie istniał, dodaj go
                            if (!languageFound)
                            {
                                configSection["Language"] = language;
                            }

                            configDict[property.Name] = configSection;
                        }
                        else
                        {
                            try
                            {
                                var deserializedValue = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                                if (deserializedValue != null)
                                {
                                    configDict[property.Name] = deserializedValue;
                                }
                                else
                                {
                                    configDict[property.Name] = property.Value.GetRawText();
                                }
                            }
                            catch (JsonException)
                            {
                                configDict[property.Name] = property.Value.GetRawText();
                            }
                        }
                    }

                    var updatedJson = JsonSerializer.Serialize(configDict, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(appSettingsFilePath, updatedJson);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd zapisywania języka: {ex.Message}");
            }
        }

        public static string GetLanguageSetting()
        {
            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                var configuration = configBuilder.Build();
                var language = configuration["Configuration:Language"];

                // Zwróć pusty string jeśli parametr nie istnieje lub jest pusty
                // To spowoduje pokazanie dialogu wyboru języka
                return language ?? string.Empty;
            }
            catch
            {
                // W przypadku błędu zwróć pusty string, aby pokazać dialog
                return string.Empty;
            }
        }

        /// <summary>
        /// Sprawdza czy w appsettings.json istnieje klucz TelemetryEnabled.
        /// Jeśli nie istnieje, dodaje go z wartością true.
        /// </summary>
        public static void EnsureTelemetryEnabledExists()
        {
            try
            {
                if (!File.Exists(appSettingsFilePath))
                    return;

                var json = File.ReadAllText(appSettingsFilePath);
                var config = JsonSerializer.Deserialize<JsonElement>(json);

                var configDict = new Dictionary<string, object>();
                bool needsUpdate = false;

                // Skopiuj istniejące ustawienia
                foreach (var property in config.EnumerateObject())
                {
                    if (property.Name == "Configuration")
                    {
                        var configSection = new Dictionary<string, object>();
                        bool telemetryFound = false;

                        foreach (var configProp in property.Value.EnumerateObject())
                        {
                            if (configProp.Name == "TelemetryEnabled")
                            {
                                telemetryFound = true;
                            }
                            
                            // Zachowaj typ danych (bool, string, int, etc.)
                            configSection[configProp.Name] = configProp.Value.ValueKind switch
                            {
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Number => configProp.Value.GetInt32(),
                                _ => configProp.Value.ToString()
                            };
                        }

                        // Jeśli parametr TelemetryEnabled nie istniał, dodaj go
                        if (!telemetryFound)
                        {
                            configSection["TelemetryEnabled"] = true;
                            needsUpdate = true;
                        }

                        configDict[property.Name] = configSection;
                    }
                    else
                    {
                        try
                        {
                            var deserializedValue = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                            if (deserializedValue != null)
                            {
                                configDict[property.Name] = deserializedValue;
                            }
                            else
                            {
                                configDict[property.Name] = property.Value.GetRawText();
                            }
                        }
                        catch (JsonException)
                        {
                            configDict[property.Name] = property.Value.GetRawText();
                        }
                    }
                }

                // Zapisz tylko jeśli coś się zmieniło
                if (needsUpdate)
                {
                    var updatedJson = JsonSerializer.Serialize(configDict, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(appSettingsFilePath, updatedJson);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas sprawdzania/dodawania TelemetryEnabled: {ex.Message}");
            }
        }
    }
}

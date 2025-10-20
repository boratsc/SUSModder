using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Repositories;

namespace SUSModder.Core.Configuration
{
    public class ModConfiguration : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _isSelected;
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
                System.Diagnostics.Debug.WriteLine("Using local config");
                return localConfigs;
            }

            System.Diagnostics.Debug.WriteLine("Local config not found, fetching from API...");

            try
            {
                // Użyj Task.Run aby uniknąć deadlocka z kontekstem synchronizacji
                var apiConfigs = Task.Run(async () => await FetchConfigFromApiAsync()).GetAwaiter().GetResult();
                System.Diagnostics.Debug.WriteLine($"API returned {apiConfigs.Count} configs");

                if (apiConfigs.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine("Saving API config locally...");
                    configRepo.SaveConfig(apiConfigs);
                    System.Diagnostics.Debug.WriteLine("Config saved successfully");
                }

                return apiConfigs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API fetch failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<ModConfiguration>();
            }
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
    }
}

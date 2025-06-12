using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Repositories;

namespace SUSModder.Core.Configuration
{
    public class ModConfiguration
    {
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
                var apiConfigs = FetchConfigFromApiAsync().GetAwaiter().GetResult();
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
                return new List<ModConfiguration>();
            }
        }

        private static async Task<List<ModConfiguration>> FetchConfigFromApiAsync()
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    // Pobierz URL z appsettings.json
                    string configApiUrl = GetUpdateServerUrl();

                    // Dodaj token autoryzacji tak jak w UpdateConfigMenuItem_Click
                    string downloadToken = SecretProvider.GetDownloadToken();
                    httpClient.DefaultRequestHeaders.Add("Authorization", downloadToken);

                    var response = await httpClient.GetStringAsync(configApiUrl);
                    return JsonSerializer.Deserialize<List<ModConfiguration>>(response) ?? new List<ModConfiguration>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching config from API: {ex.Message}");
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
                        return jsonObj["Configuration"]["UpdateServerUrl"].ToString() ?? "https://susfuckr.boracik.pl/api/config";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading UpdateServerUrl from appsettings.json: {ex.Message}");
            }

            // Fallback do domyślnego URL-a
            return "https://susfuckr.boracik.pl/api/config";
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

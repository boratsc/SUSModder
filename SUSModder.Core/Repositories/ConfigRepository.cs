using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SUSModder.Core.Configuration;
using System.Net.Http;
using System.Threading.Tasks;

namespace SUSModder.Core.Repositories
{
    public class ConfigRepository
    {
        private readonly string configFilePath;
        private readonly string appSettingsFilePath;

        public ConfigRepository(string exeDir)
        {
            configFilePath = Path.Combine(exeDir, "config.json");
            appSettingsFilePath = Path.Combine(exeDir, "appsettings.json");
        }

        public List<ModConfiguration> LoadConfig()
        {
            if (File.Exists(configFilePath))
            {
                var json = File.ReadAllText(configFilePath);
                return JsonSerializer.Deserialize<List<ModConfiguration>>(json) ?? new List<ModConfiguration>();
            }
            return new List<ModConfiguration>();
        }

        public void SaveConfig(List<ModConfiguration> configs)
        {
            var dir = Path.GetDirectoryName(configFilePath) ?? string.Empty;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, json);
        }

        public Dictionary<string, object>? LoadAppSettings()
        {
            if (File.Exists(appSettingsFilePath))
            {
                var json = File.ReadAllText(appSettingsFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }
            return null;
        }

        public void SaveAppSettings(Dictionary<string, object> settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(appSettingsFilePath, json);
        }

        public async Task<List<ModConfiguration>?> LoadConfigFromApiAsync()
        {
            try
            {
                var appSettings = LoadAppSettings();
                if (appSettings == null)
                    return null;

                // Pobierz URL serwera z appsettings.json
                string? updateServerUrl = null;
                if (appSettings.TryGetValue("Configuration", out var configObj) &&
                    configObj is JsonElement configElement &&
                    configElement.TryGetProperty("UpdateServerUrl", out var urlElement))
                {
                    updateServerUrl = urlElement.GetString();
                }

                if (string.IsNullOrEmpty(updateServerUrl))
                {
                    System.Diagnostics.Debug.WriteLine("UpdateServerUrl not found in appsettings.json");
                    return null;
                }

                // Pobierz konfigurację z API
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync(updateServerUrl);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(jsonContent))
                    return null;

                var configs = JsonSerializer.Deserialize<List<ModConfiguration>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return configs;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP error loading config from API: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Timeout loading config from API: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON parsing error loading config from API: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config from API: {ex.Message}");
                return null;
            }
        }

    }
}

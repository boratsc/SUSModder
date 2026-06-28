using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using SUSModder.Core.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace SUSModder.Core.Repositories
{
    public class ConfigRepository
    {
        private readonly string configFilePath;
        private readonly string appSettingsFilePath;

        public ConfigRepository()
            : this(ApplicationPaths.GetApplicationDirectory())
        {
        }

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

                var apiClient = SUSModderApiClientProvider.TryGetDefault();
                if (apiClient is null)
                {
                    var configuration = new ConfigurationBuilder()
                        .SetBasePath(Path.GetDirectoryName(appSettingsFilePath) ?? ApplicationPaths.GetApplicationDirectory())
                        .AddJsonFile(Path.GetFileName(appSettingsFilePath), optional: false)
                        .Build();
                    apiClient = new SUSModderApiClient(configuration, new NullConfigRepositoryDiagnostics());
                }

                var configs = await apiClient.GetCatalogAsModConfigurationsAsync();
                return configs.Count == 0 ? null : configs;
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

        private sealed class NullConfigRepositoryDiagnostics : IDiagnosticsOutput
        {
            public void Write(string message) { }
        }
    }
}

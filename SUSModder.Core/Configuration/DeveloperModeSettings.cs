using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace SUSModder.Core.Configuration
{
    public static class DeveloperModeSettings
    {
        private static bool? _cachedDeveloperMode;

        public static bool IsEnabled
        {
            get
            {
                if (_cachedDeveloperMode == null)
                {
                    RefreshSettings();
                }
                return _cachedDeveloperMode ?? false;
            }
        }

        public static void RefreshSettings()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configPath = Path.Combine(exeDir, "appsettings.json");

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                    if (config != null && config.TryGetValue("AppSettings", out var appSettingsObj))
                    {
                        var appSettingsElement = (JsonElement)appSettingsObj;

                        if (appSettingsElement.TryGetProperty("DeveloperMode", out var devModeElement))
                        {
                            _cachedDeveloperMode = devModeElement.GetBoolean();
                        }
                        else
                        {
                            _cachedDeveloperMode = false;
                        }
                    }
                    else
                    {
                        _cachedDeveloperMode = false;
                    }
                }
                else
                {
                    _cachedDeveloperMode = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing DeveloperModeSettings: {ex.Message}");
                _cachedDeveloperMode = false;
            }
        }

        public static void SetDeveloperMode(bool enabled)
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configPath = Path.Combine(exeDir, "appsettings.json");

                Dictionary<string, object> config;

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    config = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
                else
                {
                    config = new Dictionary<string, object>();
                }

                // Pobierz istniejące AppSettings lub stwórz nowe
                Dictionary<string, object> appSettings;
                if (config.TryGetValue("AppSettings", out var appSettingsObj) && appSettingsObj is JsonElement element)
                {
                    appSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()) ?? new Dictionary<string, object>();
                }
                else
                {
                    appSettings = new Dictionary<string, object>();
                }

                // Aktualizuj DeveloperMode
                appSettings["DeveloperMode"] = enabled;
                config["AppSettings"] = appSettings;

                // Zapisz plik
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var updatedJson = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, updatedJson);

                // Aktualizuj cache
                _cachedDeveloperMode = enabled;

                System.Diagnostics.Debug.WriteLine($"DeveloperMode set to: {enabled}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting DeveloperMode: {ex.Message}");
            }
        }

        public static void ClearCache()
        {
            _cachedDeveloperMode = null;
        }
    }
}

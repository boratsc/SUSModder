using System;
using System.IO;
using System.Text.Json;
using SUSModder.Core.Services;

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
                var service = new UserSettingsService();
                var settings = service.LoadUserSettings();

                if (!settings.DeveloperMode && TryReadLegacyAppSettingsDeveloperMode(out var legacyEnabled) && legacyEnabled)
                {
                    service.UpdateUserSetting(s => s.DeveloperMode = true);
                    settings.DeveloperMode = true;
                }

                _cachedDeveloperMode = settings.DeveloperMode;
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
                var service = new UserSettingsService();
                service.UpdateUserSetting(s => s.DeveloperMode = enabled);
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

        private static bool TryReadLegacyAppSettingsDeveloperMode(out bool enabled)
        {
            enabled = false;
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configPath = Path.Combine(exeDir, "appsettings.json");
                if (!File.Exists(configPath))
                    return false;

                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("AppSettings", out var appSettings) &&
                    appSettings.TryGetProperty("DeveloperMode", out var devModeElement))
                {
                    enabled = devModeElement.GetBoolean();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Legacy DeveloperMode read failed: {ex.Message}");
            }

            return false;
        }
    }
}

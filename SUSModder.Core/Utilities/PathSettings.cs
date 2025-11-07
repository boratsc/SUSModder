using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Services;

namespace SUSModder.Core.Utilities
{
    public static class PathSettings
    {
        private static string _modsInstallPath = string.Empty;
        private static readonly string _defaultModsPath;
        private static readonly string _configFilePath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath)!,
            "appsettings.json");
        private static string? _cachedModsInstallPath = null;

        static PathSettings()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(_configFilePath, optional: true, reloadOnChange: true)
                .Build();

            _defaultModsPath = Environment.ExpandEnvironmentVariables(
                config["AppSettings:DefaultModsPath"] ?? "%APPDATA%\\Among Us - Mody");

            _modsInstallPath = config["AppSettings:ModsInstallPath"] ?? string.Empty;

            // Jeśli ścieżka nie jest ustawiona, użyj domyślnej
            if (string.IsNullOrEmpty(_modsInstallPath))
            {
                _modsInstallPath = _defaultModsPath;
            }
        }

        public static string ModsInstallPath
        {
            get
            {
                if (_cachedModsInstallPath != null)
                    return _cachedModsInstallPath;

                return LoadModsInstallPath();
            }
        }

        public static string DefaultModsPath => _defaultModsPath;

        public static void RefreshSettings()
        {
            _cachedModsInstallPath = null;
            System.Diagnostics.Debug.WriteLine("PathSettings cache cleared - will reload on next access");
        }

        public static string GetDefaultModsPath()
        {
            return _defaultModsPath;
        }

        // Opcjonalnie: metoda do ustawienia custom ścieżki (dla testów)
        public static void SetCustomPath(string path)
        {
            _cachedModsInstallPath = path;
        }

        private static string LoadModsInstallPath()
        {
            try
            {
                var userSettingsService = new UserSettingsService();
                var userSettings = userSettingsService.LoadUserSettings();

                // Jeśli użytkownik ustawił własną ścieżkę, użyj jej
                if (!string.IsNullOrEmpty(userSettings.ModsInstallPath))
                {
                    _cachedModsInstallPath = Environment.ExpandEnvironmentVariables(userSettings.ModsInstallPath);
                    return _cachedModsInstallPath;
                }

                // W przeciwnym razie użyj domyślnej ścieżki
                _cachedModsInstallPath = _defaultModsPath;
                return _cachedModsInstallPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading ModsInstallPath: {ex.Message}");
                _cachedModsInstallPath = _defaultModsPath;
                return _cachedModsInstallPath;
            }
        }

        private static void SavePathToConfig()
        {
            try
            {
                var json = File.ReadAllText(_configFilePath);
                var jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(json) as Newtonsoft.Json.Linq.JObject;

                if (jsonObj == null)
                {
                    jsonObj = new Newtonsoft.Json.Linq.JObject();
                }

                if (jsonObj["AppSettings"] == null)
                {
                    jsonObj["AppSettings"] = new Newtonsoft.Json.Linq.JObject();
                }

                jsonObj["AppSettings"]!["ModsInstallPath"] = _modsInstallPath;

                string output = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_configFilePath, output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas zapisywania ścieżki do konfiguracji: {ex.Message}");
                // Można dodać logowanie błędu
            }
        }

        /// <summary>
        /// Zwraca rzeczywistą ścieżkę bazową moda z uwzględnieniem struktury Epic (podkatalog AmongUs).
        /// Dla Epic Among Us gra jest instalowana w podkatalogu AmongUs, podczas gdy dla Steam bezpośrednio w katalogu moda.
        /// </summary>
        /// <param name="installPath">Ścieżka instalacji moda z config.json</param>
        /// <returns>Rzeczywista ścieżka do katalogu z plikami gry</returns>
        public static string GetActualModPath(string installPath)
        {
            if (string.IsNullOrEmpty(installPath))
                return installPath;

            // Sprawdź czy istnieje podkatalog AmongUs (typowe dla Epic)
            string epicSubPath = Path.Combine(installPath, "AmongUs");
            if (Directory.Exists(epicSubPath))
            {
                return epicSubPath;
            }

            // Dla Steam lub gdy nie ma podkatalogu AmongUs
            return installPath;
        }
    }
}

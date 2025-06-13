using System;
using System.IO;
using Microsoft.Extensions.Configuration;

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
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                var configuration = configBuilder.Build();

                string? modsInstallPath = configuration.GetSection("AppSettings")["ModsInstallPath"];

                if (!string.IsNullOrEmpty(modsInstallPath))
                {
                    _cachedModsInstallPath = Environment.ExpandEnvironmentVariables(modsInstallPath);
                    return _cachedModsInstallPath;
                }

                string? defaultPath = configuration.GetSection("AppSettings")["DefaultModsPath"];
                if (!string.IsNullOrEmpty(defaultPath))
                {
                    _cachedModsInstallPath = Environment.ExpandEnvironmentVariables(defaultPath);
                    return _cachedModsInstallPath;
                }

                _cachedModsInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Among Us - Mody");
                return _cachedModsInstallPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading ModsInstallPath: {ex.Message}");
                _cachedModsInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Among Us - Mody");
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
    }
}

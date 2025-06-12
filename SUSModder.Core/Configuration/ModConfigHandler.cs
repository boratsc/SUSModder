using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace SUSModder.Core.Configuration
{
    public static class ModConfigHandler
    {
        private static IConfiguration? _configuration;
        private static IUserInteraction? _userInteraction;

        public static void Initialize(IConfiguration configuration, IUserInteraction userInteraction)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration), "Configuration cannot be null");
            _userInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction), "UserInteraction cannot be null");
        }

        public static void SaveLocalConfig()
        {
            string sourceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            string configName = _userInteraction?.Prompt("Wpisz nazwę konfiguracji:", "Nazwa konfiguracji") ?? "";

            string zipFileName = string.IsNullOrWhiteSpace(configName)
                ? $"Konfiguracja z dnia - {DateTime.Now:yyyyMMddHHmmss}.zip"
                : $"{configName}.zip";
            string destinationPath = Path.Combine(configDir, zipFileName);

            using (var zipStream = new FileStream(destinationPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var filePath in Directory.GetFiles(sourceDir, "*.txt"))
                {
                    archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                }
            }

            _userInteraction?.ShowInfo("Konfiguracja została zapisana lokalnie.", "Sukces");
        }

        public static void LoadLocalConfig()
        {
            string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");
            if (!Directory.Exists(configDir))
            {
                _userInteraction?.ShowError("Nie znaleziono katalogu konfiguracji.", "Błąd");
                return;
            }
            string[] files = Directory.GetFiles(configDir, "*.zip");
            if (files.Length == 0)
            {
                _userInteraction?.ShowError("Nie znaleziono zapisanych konfiguracji.", "Błąd");
                return;
            }
            string? selectedFile = files.Length == 1
                ? files[0]
                : _userInteraction?.SelectFile("ZIP files (*.zip)|*.zip", configDir);

            if (!string.IsNullOrWhiteSpace(selectedFile))
            {
                LoadConfigFromFile(selectedFile);
            }
        }

        public static async Task SaveServerConfigAsync()
        {
            if (_configuration == null)
                throw new InvalidOperationException("Configuration has not been initialized. Call Initialize() method first.");
            if (_userInteraction == null)
                throw new InvalidOperationException("UserInteraction has not been initialized. Call Initialize() method first.");

            string sourceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            string tempDir = Path.Combine(Path.GetTempPath(), "AmongUsMods");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            string hash = Guid.NewGuid().ToString("N");
            string hashFileName = $"{hash}.zip";
            string tempFilePath = Path.Combine(tempDir, hashFileName);
            try
            {
                var filesToZip = Directory.GetFiles(sourceDir, "*.txt");
                if (filesToZip.Length == 0)
                {
                    _userInteraction.ShowError("No files available to zip.", "Error");
                    return;
                }
                using (var zipStream = new FileStream(tempFilePath, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var filePath in filesToZip)
                    {
                        archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Wystąpił błąd podczas tworzenia pliku ZIP: {ex}";
                LogErrorToFile(errorMessage);
                _userInteraction.ShowError(errorMessage, "Błąd");
                return;
            }

            var baseUrl = _configuration["Configuration:BaseUrl"];
            var apiPort = _configuration["Configuration:ApiPort"];
            var uploadEndpoint = _configuration["Configuration:UploadEndpoint"];
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiPort) || string.IsNullOrWhiteSpace(uploadEndpoint))
            {
                _userInteraction.ShowError("Configuration contains null or whitespace values. Ensure BaseUrl, ApiPort, and UploadEndpoint are correctly set.", "Error");
                return;
            }
            string serverUrl = $"{baseUrl.TrimEnd('/')}" + $":{apiPort}/" + $"{uploadEndpoint.TrimStart('/')}";
            try
            {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) => true;
                using var client = new HttpClient(handler);
                string downloadToken = SecretProvider.GetDownloadToken();
                client.DefaultRequestHeaders.Add("Authorization", downloadToken);
                using var content = new MultipartFormDataContent();
                using var fs = File.OpenRead(tempFilePath);
                content.Add(new StreamContent(fs), "file", Path.GetFileName(tempFilePath));
                var response = await client.PostAsync(serverUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    ShowHashDialog(hash);
                    AddConfigToJSON(hash);
                    _userInteraction.ShowInfo("Konfiguracja została zapisana na serwerze.", "Sukces");
                }
                else
                {
                    _userInteraction.ShowError("Błąd podczas zapisu. Kod statusu: " + response.StatusCode, "Błąd");
                }
            }
            catch (HttpRequestException ex)
            {
                string errorMessage = $"Wystąpił błąd przy zapisywaniu konfiguracji: {ex}";
                LogErrorToFile(errorMessage);
                _userInteraction.ShowError($"Wystąpił błąd przy zapisywaniu konfiguracji: {ex.Message}", "Błąd HTTP");
            }
            catch (Exception ex)
            {
                string errorMessage = $"Wystąpił nieoczekiwany błąd: {ex}";
                LogErrorToFile(errorMessage);
                _userInteraction.ShowError($"Wystąpił nieoczekiwany błąd: {ex.Message}", "Błąd");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        public static async Task LoadServerConfigAsync()
        {
            if (_configuration == null)
                throw new InvalidOperationException("Configuration has not been initialized. Call Initialize() method first.");
            if (_userInteraction == null)
                throw new InvalidOperationException("UserInteraction has not been initialized. Call Initialize() method first.");

            string jsonFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SUSModder", "touConfigsBase.json");
            List<dynamic> configs = new List<dynamic>();
            string hash = string.Empty;
            if (File.Exists(jsonFile))
            {
                var json = File.ReadAllText(jsonFile);
                configs = JsonConvert.DeserializeObject<List<dynamic>>(json) ?? new List<dynamic>();
            }

            // Prompt for hash or select from list
            string? inputHash = _userInteraction.Prompt("Podaj kod konfiguracji lub zostaw puste, aby wybrać z listy:", "Załaduj konfigurację");
            if (!string.IsNullOrWhiteSpace(inputHash))
            {
                hash = inputHash.Trim();
            }
            else if (configs.Count > 0)
            {
                // Build a selection string
                var options = new List<string>();
                foreach (var c in configs)
                {
                    options.Add($"{c.date} - {c.hash}");
                }
                string? selected = _userInteraction.SelectFile("ZIP files (*.zip)|*.zip", Path.GetDirectoryName(jsonFile) ?? "");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    hash = selected.Split('-').LastOrDefault()?.Trim() ?? string.Empty;
                }
            }
            if (string.IsNullOrWhiteSpace(hash)) return;

            var baseUrl = _configuration["Configuration:BaseUrl"];
            var apiPort = _configuration["Configuration:ApiPort"];
            var downloadEndpoint = _configuration["Configuration:DownloadEndpoint"];
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiPort) || string.IsNullOrWhiteSpace(downloadEndpoint))
            {
                _userInteraction.ShowError("Configuration contains null values. Ensure BaseUrl, ApiPort, and DownloadEndpoint are correctly set.", "Error");
                return;
            }
            string serverUrl = $"{baseUrl.TrimEnd('/')}" + $":{apiPort}/" + $"{downloadEndpoint.TrimStart('/')}/{hash}.zip";
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"{hash}.zip");
            try
            {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) => true;
                using var client = new HttpClient(handler);
                string downloadToken = SecretProvider.GetDownloadToken();
                client.DefaultRequestHeaders.Add("Authorization", downloadToken);
                var response = await client.GetAsync(serverUrl);
                if (response.IsSuccessStatusCode)
                {
                    using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                    LoadConfigFromFile(tempFilePath);
                    _userInteraction.ShowInfo("Konfiguracja z serwera została pomyślnie wczytana.", "Sukces");
                }
                else
                {
                    _userInteraction.ShowError($"Nie udało się pobrać konfiguracji z serwera. Kod statusu: {response.StatusCode}", "Błąd");
                }
            }
            catch (HttpRequestException ex)
            {
                string errorMessage = $"Wystąpił błąd przy pobieraniu konfiguracji: {ex}";
                LogErrorToFile(errorMessage);
                _userInteraction.ShowError($"Wystąpił błąd przy pobieraniu konfiguracji: {ex.Message}", "Błąd HTTP");
            }
            catch (SocketException ex)
            {
                string errorMessage = $"Nie udało się połączyć z serwerem. Szczegóły: {ex}";
                LogErrorToFile(errorMessage);
                _userInteraction.ShowError($"Nie udało się połączyć z serwerem. Szczegóły: {ex.Message}", "Błąd połączenia");
            }
            catch (Exception ex)
            {
                string errorMessage = $"Wystąpił nieoczekiwany błąd: {ex}";
                LogErrorToFile(errorMessage);
                _userInteraction.ShowError($"Wystąpił nieoczekiwany błąd: {ex.Message}", "Błąd");
            }
        }

        private static void LoadConfigFromFile(string filePath)
        {
            string destinationDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            using (var archive = ZipFile.OpenRead(filePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.EndsWith(".txt"))
                    {
                        entry.ExtractToFile(Path.Combine(destinationDir, entry.Name), true);
                    }
                }
            }
            _userInteraction?.ShowInfo("Konfiguracja została wczytana.", "Sukces");
        }

        private static void LogErrorToFile(string message)
        {
            string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SUSModder", "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
            using (var writer = new StreamWriter(logFilePath, append: true))
            {
                writer.WriteLine($"{DateTime.Now}: {message}");
            }
        }

        public static void ShowHashDialog(string hash)
        {
            _userInteraction?.ShowInfo("Twój kod: " + hash, "Hash Hasła");
        }

        private static void AddConfigToJSON(string hash)
        {
            string jsonFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SUSModder", "touConfigsBase.json");
            var newEntry = new
            {
                hash = hash,
                date = DateTime.Now.ToString("yyyy-MM-dd, HH:mm")
            };
            List<dynamic> configList;
            if (File.Exists(jsonFile))
            {
                var json = File.ReadAllText(jsonFile);
                configList = JsonConvert.DeserializeObject<List<dynamic>>(json) ?? new List<dynamic>();
            }
            else
            {
                configList = new List<dynamic>();
            }
            configList.Add(newEntry);
            File.WriteAllText(jsonFile, JsonConvert.SerializeObject(configList, Newtonsoft.Json.Formatting.Indented));
        }

        public static void LoadLocalTxtConfig()
        {
            string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            string? selectedFilePath = _userInteraction?.SelectFile("TXT files (*.txt)|*.txt");
            if (!string.IsNullOrWhiteSpace(selectedFilePath))
            {
                string destinationFilePath = Path.Combine(targetDir, Path.GetFileName(selectedFilePath));
                try
                {
                    File.Copy(selectedFilePath, destinationFilePath, overwrite: true);
                    _userInteraction?.ShowInfo("Konfiguracja została wczytana z pliku txt.", "Sukces");
                }
                catch (Exception ex)
                {
                    _userInteraction?.ShowError($"Błąd podczas ładowania konfiguracji: {ex.Message}", "Błąd");
                }
            }
        }

        public static void ChangePresetNames()
        {
            // Ta metoda wymaga UI do edycji wielu plików naraz.
            // W Core można przygotować tylko logikę do zmiany nazw plików na podstawie mapy stary->nowy.
            // UI powinien zebrać od użytkownika mapę nazw i przekazać ją tutaj.

            throw new NotImplementedException("Zmiana nazw presetów wymaga implementacji UI w warstwie frontend.");
        }
    }
}

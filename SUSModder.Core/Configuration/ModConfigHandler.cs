using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using SUSModder.Core.Services;
using SUSModder.Core.Repositories;
using Newtonsoft.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace SUSModder.Core.Configuration
{
    public static class ModConfigHandler
    {
        private static ConfigRepository? _configRepository;
        private static IUserInteraction? _userInteraction;

        public static void Initialize(ConfigRepository configRepository, IUserInteraction userInteraction)
        {
            _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
            _userInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction));
        }

        public static void SaveLocalConfig(string? configName = null)
        {
            string sourceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

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
        }

        public static void SaveLocalConfigWithName(string? configName)
        {
            string sourceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

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
        }

        public static void LoadLocalConfig(string? selectedFilePath = null)
        {
            string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");
            if (!Directory.Exists(configDir))
            {
                throw new DirectoryNotFoundException("Nie znaleziono katalogu konfiguracji.");
            }

            string[] files = Directory.GetFiles(configDir, "*.zip");
            if (files.Length == 0)
            {
                throw new FileNotFoundException("Nie znaleziono zapisanych konfiguracji.");
            }

            // Jeśli nie podano ścieżki i jest tylko jeden plik, użyj go
            string? fileToLoad = selectedFilePath ?? (files.Length == 1 ? files[0] : null);

            if (string.IsNullOrWhiteSpace(fileToLoad))
            {
                throw new ArgumentException("Nie wybrano pliku do wczytania.");
            }

            LoadConfigFromFile(fileToLoad);
        }

        public static async Task<string> SaveServerConfigAsync()
        {
            if (_configRepository == null)
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
                    throw new FileNotFoundException("Brak plików konfiguracyjnych do wysłania.");
                }

                using (var zipStream = new FileStream(tempFilePath, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var filePath in filesToZip)
                    {
                        archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                    }
                }

                // Pobierz konfigurację z ConfigRepository
                var appSettings = _configRepository.LoadAppSettings();

                string? baseUrl = null;
                string? apiPort = null;
                string? uploadEndpoint = null;

                if (appSettings != null && appSettings.TryGetValue("Configuration", out var configObj))
                {
                    if (configObj is JsonElement configElement)
                    {
                        if (configElement.TryGetProperty("BaseUrl", out var baseUrlElement))
                            baseUrl = baseUrlElement.GetString();
                        if (configElement.TryGetProperty("ApiPort", out var apiPortElement))
                            apiPort = apiPortElement.GetString();
                        if (configElement.TryGetProperty("UploadEndpoint", out var uploadElement))
                            uploadEndpoint = uploadElement.GetString();
                    }
                }

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiPort) || string.IsNullOrWhiteSpace(uploadEndpoint))
                {
                    throw new InvalidOperationException("Konfiguracja serwera jest niepełna. Sprawdź BaseUrl, ApiPort i UploadEndpoint w appsettings.json.");
                }

                string serverUrl = $"{baseUrl.TrimEnd('/')}/{uploadEndpoint.TrimStart('/')}";

                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Próba połączenia z: {serverUrl}");
                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] BaseUrl: {baseUrl}");
                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] ApiPort: {apiPort}");
                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] UploadEndpoint: {uploadEndpoint}");

                // Sprawdź czy URL używa HTTPS
                bool isHttps = serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Używa HTTPS: {isHttps}");

                var handler = new HttpClientHandler();

                if (isHttps)
                {
                    // Konfiguracja SSL tylko dla HTTPS
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[SSL] Walidacja certyfikatu dla: {message?.RequestUri}");
                        System.Diagnostics.Debug.WriteLine($"[SSL] Certificate Subject: {certificate?.Subject}");
                        System.Diagnostics.Debug.WriteLine($"[SSL] Certificate Issuer: {certificate?.Issuer}");
                        System.Diagnostics.Debug.WriteLine($"[SSL] Certificate Valid From: {certificate?.NotBefore}");
                        System.Diagnostics.Debug.WriteLine($"[SSL] Certificate Valid To: {certificate?.NotAfter}");
                        System.Diagnostics.Debug.WriteLine($"[SSL] SSL Policy Errors: {sslPolicyErrors}");

                        if (chain?.ChainStatus != null)
                        {
                            foreach (var status in chain.ChainStatus)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SSL] Chain Status: {status.Status} - {status.StatusInformation}");
                            }
                        }

                        if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SSL] UWAGA: Ignoruję błędy SSL: {sslPolicyErrors}");
                        }

                        return true; // Zawsze akceptuj (dla testów)
                    };

                    // Dodatkowe opcje SSL
                    //handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                    System.Diagnostics.Debug.WriteLine($"[SSL] Ustawiono protokoły: TLS 1.2 i TLS 1.3");
                }

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(30);

                string downloadToken = SecretProvider.GetDownloadToken();
                client.DefaultRequestHeaders.Add("Authorization", downloadToken);

                // Dodaj User-Agent
                client.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                using var content = new MultipartFormDataContent();
                using var fs = File.OpenRead(tempFilePath);
                content.Add(new StreamContent(fs), "file", Path.GetFileName(tempFilePath));

                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Wysyłanie pliku: {tempFilePath}");
                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Rozmiar pliku: {new FileInfo(tempFilePath).Length} bytes");
                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Authorization header: {downloadToken}");

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Rozpoczynam POST request...");
                    var response = await client.PostAsync(serverUrl, content);

                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Otrzymano odpowiedź: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");

                    if (response.IsSuccessStatusCode)
                    {
                        AddConfigToJSON(hash);
                        System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Sukces! Hash: {hash}");
                        return hash;
                    }
                    else
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Błąd serwera - Response content: {responseContent}");
                        throw new HttpRequestException($"Błąd serwera: {response.StatusCode}. Szczegóły: {responseContent}");
                    }
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("SSL connection could not be established"))
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Błąd SSL: {ex.Message}");

                    // Jeśli to HTTPS i mamy błąd SSL, spróbuj HTTP
                    if (isHttps)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Próbuję fallback na HTTP...");
                        string httpUrl = serverUrl.Replace("https://", "http://");

                        try
                        {
                            using var httpHandler = new HttpClientHandler();
                            using var httpClient = new HttpClient(httpHandler);
                            httpClient.Timeout = TimeSpan.FromSeconds(30);
                            httpClient.DefaultRequestHeaders.Add("Authorization", downloadToken);
                            httpClient.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                            using var httpContent = new MultipartFormDataContent();
                            using var httpFs = File.OpenRead(tempFilePath);
                            httpContent.Add(new StreamContent(httpFs), "file", Path.GetFileName(tempFilePath));

                            System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Fallback POST do: {httpUrl}");
                            var httpResponse = await httpClient.PostAsync(httpUrl, httpContent);

                            if (httpResponse.IsSuccessStatusCode)
                            {
                                AddConfigToJSON(hash);
                                System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Sukces przez HTTP! Hash: {hash}");
                                return hash;
                            }
                            else
                            {
                                var httpResponseContent = await httpResponse.Content.ReadAsStringAsync();
                                throw new HttpRequestException($"Błąd serwera (HTTP fallback): {httpResponse.StatusCode}. Szczegóły: {httpResponseContent}");
                            }
                        }
                        catch (Exception httpEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] HTTP fallback też nie działa: {httpEx.Message}");
                            throw new HttpRequestException($"Nie udało się połączyć ani przez HTTPS ani przez HTTP. HTTPS: {ex.Message}, HTTP: {httpEx.Message}", ex);
                        }
                    }
                    else
                    {
                        throw; // Jeśli to już było HTTP, przekaż błąd dalej
                    }
                }
                catch (HttpRequestException ex)
                {
                    string errorMessage = $"Błąd HTTP podczas zapisywania konfiguracji: {ex.Message}";
                    LogErrorToFile(errorMessage);
                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] HttpRequestException: {ex}");
                    throw new HttpRequestException(errorMessage, ex);
                }
                catch (TaskCanceledException ex)
                {
                    string errorMessage = $"Przekroczono limit czasu podczas zapisywania konfiguracji: {ex.Message}";
                    LogErrorToFile(errorMessage);
                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] TaskCanceledException: {ex}");
                    throw new TimeoutException(errorMessage, ex);
                }
                catch (Exception ex)
                {
                    string errorMessage = $"Nieoczekiwany błąd podczas zapisywania konfiguracji: {ex.Message}";
                    LogErrorToFile(errorMessage);
                    System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Exception: {ex}");
                    throw;
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveServerConfig] Nie udało się usunąć temp dir: {ex.Message}");
                    }
                }
            }
        }



        public static async Task LoadServerConfigAsync(string hash)
        {
            if (_configRepository == null)
                throw new InvalidOperationException("ConfigRepository has not been initialized. Call Initialize() method first.");

            if (string.IsNullOrWhiteSpace(hash))
                throw new ArgumentException("Hash nie może być pusty.", nameof(hash));

            // Pobierz konfigurację z ConfigRepository
            var appSettings = _configRepository.LoadAppSettings();

            string? baseUrl = null;
            string? apiPort = null;
            string? downloadEndpoint = null;

            if (appSettings != null && appSettings.TryGetValue("Configuration", out var configObj))
            {
                if (configObj is JsonElement configElement)
                {
                    if (configElement.TryGetProperty("BaseUrl", out var baseUrlElement))
                        baseUrl = baseUrlElement.GetString();
                    if (configElement.TryGetProperty("ApiPort", out var apiPortElement))
                        apiPort = apiPortElement.GetString();
                    if (configElement.TryGetProperty("DownloadEndpoint", out var downloadElement))
                        downloadEndpoint = downloadElement.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiPort) || string.IsNullOrWhiteSpace(downloadEndpoint))
            {
                throw new InvalidOperationException("Konfiguracja serwera jest niepełna. Sprawdź BaseUrl, ApiPort i DownloadEndpoint w appsettings.json.");
            }

            string serverUrl = $"{baseUrl.TrimEnd('/')}/{downloadEndpoint.TrimStart('/')}/{hash}.zip";
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"{hash}.zip");

            System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Próba pobrania z: {serverUrl}");
            System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Hash: {hash}");
            System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Temp file: {tempFilePath}");

            // Sprawdź czy URL używa HTTPS
            bool isHttps = serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Używa HTTPS: {isHttps}");

            var handler = new HttpClientHandler();

            if (isHttps)
            {
                // Konfiguracja SSL tylko dla HTTPS
                handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SSL] Walidacja certyfikatu dla: {message?.RequestUri}");
                    System.Diagnostics.Debug.WriteLine($"[SSL] SSL Policy Errors: {sslPolicyErrors}");

                    if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SSL] UWAGA: Ignoruję błędy SSL: {sslPolicyErrors}");
                    }

                    return true; // Zawsze akceptuj (dla testów)
                };

                handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                System.Diagnostics.Debug.WriteLine($"[SSL] Ustawiono protokoły: TLS 1.2 i TLS 1.3");
            }

            try
            {
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(30);

                string downloadToken = SecretProvider.GetDownloadToken();
                client.DefaultRequestHeaders.Add("Authorization", downloadToken);
                client.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Rozpoczynam GET request...");
                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Authorization header: {downloadToken}");

                var response = await client.GetAsync(serverUrl);

                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Otrzymano odpowiedź: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Pobieranie pliku...");
                    using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }

                    System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Plik pobrany, rozmiar: {new FileInfo(tempFilePath).Length} bytes");

                    // Wczytaj konfigurację z pobranego pliku
                    LoadConfigFromFile(tempFilePath);

                    System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Konfiguracja wczytana pomyślnie");
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Błąd serwera - Response content: {responseContent}");
                    throw new HttpRequestException($"Nie udało się pobrać konfiguracji z serwera. Kod statusu: {response.StatusCode}. Szczegóły: {responseContent}");
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("SSL connection could not be established"))
            {
                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Błąd SSL: {ex.Message}");

                // Jeśli to HTTPS i mamy błąd SSL, spróbuj HTTP
                if (isHttps)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Próbuję fallback na HTTP...");
                    string httpUrl = serverUrl.Replace("https://", "http://");

                    try
                    {
                        using var httpHandler = new HttpClientHandler();
                        using var httpClient = new HttpClient(httpHandler);
                        httpClient.Timeout = TimeSpan.FromSeconds(30);

                        string downloadToken = SecretProvider.GetDownloadToken();
                        httpClient.DefaultRequestHeaders.Add("Authorization", downloadToken);
                        httpClient.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                        System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Fallback GET do: {httpUrl}");
                        var httpResponse = await httpClient.GetAsync(httpUrl);

                        if (httpResponse.IsSuccessStatusCode)
                        {
                            using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await httpResponse.Content.CopyToAsync(fs);
                            }

                            LoadConfigFromFile(tempFilePath);
                            System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Sukces przez HTTP!");
                            return;
                        }
                        else
                        {
                            var httpResponseContent = await httpResponse.Content.ReadAsStringAsync();
                            throw new HttpRequestException($"Błąd serwera (HTTP fallback): {httpResponse.StatusCode}. Szczegóły: {httpResponseContent}");
                        }
                    }
                    catch (Exception httpEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] HTTP fallback też nie działa: {httpEx.Message}");
                        throw new HttpRequestException($"Nie udało się połączyć ani przez HTTPS ani przez HTTP. HTTPS: {ex.Message}, HTTP: {httpEx.Message}", ex);
                    }
                }
                else
                {
                    throw; // Jeśli to już było HTTP, przekaż błąd dalej
                }
            }
            catch (HttpRequestException ex)
            {
                string errorMessage = $"Błąd HTTP podczas pobierania konfiguracji: {ex.Message}";
                LogErrorToFile(errorMessage);
                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] HttpRequestException: {ex}");
                throw new HttpRequestException(errorMessage, ex);
            }
            catch (TaskCanceledException ex)
            {
                string errorMessage = $"Przekroczono limit czasu podczas pobierania konfiguracji: {ex.Message}";
                LogErrorToFile(errorMessage);
                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] TaskCanceledException: {ex}");
                throw new TimeoutException(errorMessage, ex);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Nieoczekiwany błąd podczas pobierania konfiguracji: {ex.Message}";
                LogErrorToFile(errorMessage);
                System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Exception: {ex}");
                throw;
            }
            finally
            {
                // Usuń tymczasowy plik
                if (File.Exists(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                        System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Usunięto tymczasowy plik: {tempFilePath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadServerConfig] Nie udało się usunąć temp file: {ex.Message}");
                    }
                }
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
                // Utwórz katalog jeśli nie istnieje
                Directory.CreateDirectory(Path.GetDirectoryName(jsonFile)!);
            }
            configList.Add(newEntry);
            File.WriteAllText(jsonFile, JsonConvert.SerializeObject(configList, Newtonsoft.Json.Formatting.Indented));
        }


        public static void LoadLocalTxtConfig(string selectedFilePath)
        {
            if (string.IsNullOrWhiteSpace(selectedFilePath))
            {
                throw new ArgumentException("Nie wybrano pliku do wczytania.");
            }

            string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Innersloth\Among Us");
            string destinationFilePath = Path.Combine(targetDir, Path.GetFileName(selectedFilePath));
            File.Copy(selectedFilePath, destinationFilePath, overwrite: true);
        }

        public static string[] GetAvailableConfigFiles()
        {
            string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");
            if (!Directory.Exists(configDir))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(configDir, "*.zip");
        }

        public static void ChangePresetNames()
        {
            // Ta metoda jest teraz obsługiwana przez UI w AdditionalActionsPanel
            // Logika została przeniesiona do ChangePresetNamesDialog
            System.Diagnostics.Debug.WriteLine("[ModConfigHandler] ChangePresetNames wywołane - obsługa w UI");
        }
    }
}

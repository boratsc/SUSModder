using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;
using System.IO;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis telemetrii - zbieranie anonimowych statystyk użytkowania
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class TelemetryService : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly SessionTracker _sessionTracker;
        private readonly UserSettingsService _userSettingsService;
        private readonly string _userHash;
        private readonly string _appVersion;
        private bool _isEnabled;

        public TelemetryService(IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2) // Krótki timeout - nie blokujemy UI
            };
            _sessionTracker = new SessionTracker();
            _userSettingsService = new UserSettingsService();

            // Generuj/wczytaj anonimowy hash użytkownika
            _userHash = HardwareIdProvider.GetAnonymousUserHash();

            // Wczytaj wersję z version.json
            _appVersion = LoadAppVersionFromFile();

            // Sprawdź czy telemetria jest włączona (z UserSettings)
            var userSettings = _userSettingsService.LoadUserSettings();
            _isEnabled = userSettings.TelemetryEnabled;
        }

        /// <summary>
        /// Wysyła heartbeat do API (fire-and-forget)
        /// </summary>
        public async Task SendHeartbeatAsync()
        {
            // Jeśli telemetria wyłączona - nic nie rób
            if (!_isEnabled)
            {
                System.Diagnostics.Debug.WriteLine("Telemetry disabled - skipping heartbeat");
                return;
            }

            try
            {
                var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
                if (string.IsNullOrEmpty(baseUrl))
                {
                    System.Diagnostics.Debug.WriteLine("BaseUrl not configured - skipping telemetry");
                    return;
                }

                var telemetryUrl = $"{baseUrl}/api/telemetry/heartbeat";

                // Zbierz dane do wysłania z UserSettings
                var userSettings = _userSettingsService.LoadUserSettings();

                var language = userSettings.Language;
                if (string.IsNullOrWhiteSpace(language))
                {
                    // Fallback do system locale jeśli nie ustawiono
                    language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                }

                var data = new
                {
                    userHash = _userHash,
                    appVersion = _appVersion,
                    platform = userSettings.Mode,
                    language = language,
                    installedModIds = GetInstalledModIds(),
                    sessionTimeSeconds = _sessionTracker.GetSessionTimeSeconds(),
                    timestamp = DateTime.UtcNow.ToString("O") // ISO 8601
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"Sending telemetry heartbeat to {telemetryUrl}: {json}");

                // Fire-and-forget - nie czekamy na odpowiedź
                _ = _httpClient.PostAsync(telemetryUrl, content).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine($"Telemetry heartbeat failed: {task.Exception?.GetBaseException().Message}");
                    }
                    else if (task.IsCompletedSuccessfully)
                    {
                        try
                        {
                            var resp = task.Result;
                            if (resp == null)
                            {
                                System.Diagnostics.Debug.WriteLine("Telemetry heartbeat: no response object");
                                return;
                            }

                            if (!resp.IsSuccessStatusCode)
                            {
                                System.Diagnostics.Debug.WriteLine($"Telemetry heartbeat HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("Telemetry heartbeat sent (HTTP 2xx)");
                            }
                            resp.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Telemetry post-processing error: {ex.Message}");
                        }
                    }
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Ignoruj błędy telemetrii - nie przerywamy działania aplikacji
                System.Diagnostics.Debug.WriteLine($"Telemetry error: {ex.Message}");
            }
        }

        /// <summary>
        /// Wysyła końcowy heartbeat przy zamykaniu aplikacji
        /// </summary>
        public async Task SendShutdownHeartbeatAsync()
        {
            _sessionTracker.Stop();
            await SendHeartbeatAsync();
        }

        /// <summary>
        /// Pobiera listę ID zainstalowanych modów
        /// </summary>
        private List<int> GetInstalledModIds()
        {
            try
            {
                // Odkryj zainstalowane mody przez InstallationMapManager
                var modsBasePath = Utilities.PathSettings.ModsInstallPath;
                
                if (string.IsNullOrEmpty(modsBasePath) || !Directory.Exists(modsBasePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Telemetry] ModsBasePath nie istnieje: {modsBasePath}");
                    return new List<int>();
                }

                // Synchroniczne skanowanie katalogów (dla uproszczenia w telemetrii)
                var installedIds = new List<int>();
                var directories = Directory.GetDirectories(modsBasePath);

                foreach (var dir in directories)
                {
                    // Sprawdź bezpośrednio w katalogu (STEAM)
                    var mapPath = Path.Combine(dir, ".susmodder-install.json");
                    
                    if (File.Exists(mapPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(mapPath);
                            var map = System.Text.Json.JsonSerializer.Deserialize<Models.InstallationMap>(json, 
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            
                            if (map?.FullMod != null)
                            {
                                installedIds.Add(map.FullMod.ModId);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Telemetry] Failed to read map from {mapPath}: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Sprawdź podkatalogi (EPIC: poziom 2, np. "ModName\AmongUs\")
                        try
                        {
                            var subDirectories = Directory.GetDirectories(dir);
                            foreach (var subDir in subDirectories)
                            {
                                var subMapPath = Path.Combine(subDir, ".susmodder-install.json");
                                
                                if (File.Exists(subMapPath))
                                {
                                    try
                                    {
                                        var json = File.ReadAllText(subMapPath);
                                        var map = System.Text.Json.JsonSerializer.Deserialize<Models.InstallationMap>(json,
                                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                        
                                        if (map?.FullMod != null)
                                        {
                                            installedIds.Add(map.FullMod.ModId);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[Telemetry] Failed to read map from {subMapPath}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch { /* Ignoruj błędy skanowania podkatalogów */ }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Telemetry] Found {installedIds.Count} installed mods: [{string.Join(", ", installedIds)}]");
                
                return installedIds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telemetry] Failed to get installed mod IDs: {ex.Message}");
                return new List<int>();
            }
        }

        /// <summary>
        /// Włącza lub wyłącza telemetrię
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;

            // Zapisz do user-settings.json
            _userSettingsService.UpdateUserSetting(settings => settings.TelemetryEnabled = enabled);
        }

        /// <summary>
        /// Wczytuje wersję aplikacji z version.json
        /// </summary>
        private string LoadAppVersionFromFile()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var versionFilePath = Path.Combine(exeDir, "version.json");

                if (File.Exists(versionFilePath))
                {
                    var json = File.ReadAllText(versionFilePath);
                    var versionData = JsonSerializer.Deserialize<Configuration.AppVersion>(json);

                    if (versionData != null && !string.IsNullOrWhiteSpace(versionData.CurrentVersion))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Telemetry] Loaded version from version.json: {versionData.CurrentVersion}");
                        return versionData.CurrentVersion;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[Telemetry] version.json not found or invalid - using fallback");
                return "unknown";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telemetry] Failed to load version: {ex.Message}");
                return "unknown";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

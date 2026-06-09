using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;
using System.IO;
using System.Threading;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis telemetrii - zbieranie anonimowych statystyk użytkowania
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class TelemetryService
    {
        private readonly ISUSModderApiClient _apiClient;
        private readonly SessionTracker _sessionTracker;
        private readonly UserSettingsService _userSettingsService;
        private readonly string _userHash;
        private readonly string _appVersion;
        private bool _isEnabled;
        private readonly object _heartbeatLock = new();
        private DateTimeOffset _lastHeartbeatAttemptUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _nextAllowedHeartbeatUtc = DateTimeOffset.MinValue;
        private static readonly TimeSpan MinHeartbeatInterval = TimeSpan.FromSeconds(30);

        public TelemetryService(IConfiguration configuration, ISUSModderApiClient? apiClient = null)
        {
            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? new SUSModderApiClient(configuration, new NullDiagnosticsOutput());
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
        public async Task SendHeartbeatAsync(bool force = false)
        {
            // Jeśli telemetria wyłączona - nic nie rób
            if (!_isEnabled)
            {
                System.Diagnostics.Debug.WriteLine("Telemetry disabled - skipping heartbeat");
                return;
            }

            var now = DateTimeOffset.UtcNow;
            lock (_heartbeatLock)
            {
                if (!force)
                {
                    if (now < _nextAllowedHeartbeatUtc)
                    {
                        return;
                    }

                    if (now - _lastHeartbeatAttemptUtc < MinHeartbeatInterval)
                    {
                        return;
                    }
                }

                _lastHeartbeatAttemptUtc = now;
            }

            try
            {
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

                System.Diagnostics.Debug.WriteLine($"Sending telemetry heartbeat: {JsonSerializer.Serialize(data)}");

                _ = _apiClient.SendHeartbeatAsync(data).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                        System.Diagnostics.Debug.WriteLine($"Telemetry heartbeat failed: {task.Exception?.GetBaseException().Message}");
                }, TaskScheduler.Default);

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
            await SendHeartbeatAsync(force: false);
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
                
                return installedIds.Where(id => id > 0).Distinct().ToList();
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

                var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (assemblyVersion is not null)
                    return assemblyVersion.ToString(3);

                System.Diagnostics.Debug.WriteLine("[Telemetry] version.json not found - using 0.0.0 fallback");
                return "0.0.0";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telemetry] Failed to load version: {ex.Message}");
                return "0.0.0";
            }
        }

    }

    internal sealed class NullDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }
}

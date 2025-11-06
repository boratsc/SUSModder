using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Velopack;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Services
{
    public sealed class VelopackUpdateService : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;
        private readonly UserSettingsService _userSettingsService;
        private readonly string _currentVersion;
    private readonly object _initializationLock = new();
    private UpdateManager? _updateManager;
    private VelopackApiSource? _apiSource;
        private bool _disposed;

        public VelopackUpdateService(string currentVersion, IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput)
        {
            _currentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0.0.0" : currentVersion;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _diagnosticsOutput = diagnosticsOutput ?? throw new ArgumentNullException(nameof(diagnosticsOutput));
            _userSettingsService = new UserSettingsService();
        }

        public async Task InitializeAsync()
        {
            if (_updateManager != null)
                return;

            lock (_initializationLock)
            {
                if (_updateManager != null)
                    return;

                var updateFeedUri = GetUpdateFeedUri();
                var updateChannel = GetUpdateChannel();
                _diagnosticsOutput.Write($"[Velopack] Initializing UpdateManager with feed: {updateFeedUri}, channel: {updateChannel}");

                _apiSource = new VelopackApiSource(updateFeedUri);

                var updateOptions = new UpdateOptions
                {
                    ExplicitChannel = updateChannel
                };

                _updateManager = new UpdateManager(_apiSource, updateOptions);
            }

            await Task.CompletedTask;
        }

        public async Task<VelopackUpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureInitializedAsync();

                _diagnosticsOutput.Write("[Velopack] Checking for updates...");
                var updateInfo = await _updateManager!.CheckForUpdatesAsync().ConfigureAwait(false);

                if (updateInfo == null)
                {
                    _diagnosticsOutput.Write("[Velopack] No updates available");
                    return VelopackUpdateCheckResult.NoUpdate(_currentVersion);
                }

                var latestVersion = updateInfo.TargetFullRelease.Version.ToString();
                _diagnosticsOutput.Write($"[Velopack] Update available: {_currentVersion} -> {latestVersion}");

                return VelopackUpdateCheckResult.UpdateAvailable(_currentVersion, latestVersion, updateInfo);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"[Velopack] Failed to check for updates: {ex.Message}");
                return VelopackUpdateCheckResult.Failed(_currentVersion, ex);
            }
        }

        public async Task<VelopackUpdateDownloadResult> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (updateInfo == null) throw new ArgumentNullException(nameof(updateInfo));

            try
            {
                await EnsureInitializedAsync();

                _diagnosticsOutput.Write("[Velopack] Downloading update package...");

                await _updateManager!.DownloadUpdatesAsync(updateInfo, percent =>
                {
                    var clamped = Math.Clamp(percent, 0, 100);
                    progress?.Report(clamped);
                }, cancellationToken).ConfigureAwait(false);

                _diagnosticsOutput.Write("[Velopack] Update package downloaded successfully");
                return VelopackUpdateDownloadResult.Successful(updateInfo);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"[Velopack] Failed to download update: {ex.Message}");
                return VelopackUpdateDownloadResult.Failed(ex);
            }
        }

        public async Task<bool> IsInstalledAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            return _updateManager!.IsInstalled;
        }

        public async Task ApplyUpdateAndRestartAsync(UpdateInfo updateInfo, bool silent = false, bool restart = true, string[]? restartArgs = null)
        {
            if (updateInfo == null) throw new ArgumentNullException(nameof(updateInfo));

            await EnsureInitializedAsync();

            if (updateInfo.TargetFullRelease == null)
                throw new InvalidOperationException("UpdateInfo does not contain a target release.");

            _diagnosticsOutput.Write("[Velopack] Preparing to apply update and restart application...");

            await _updateManager!.WaitExitThenApplyUpdatesAsync(updateInfo.TargetFullRelease, silent, restart, restartArgs).ConfigureAwait(false);
        }

        /// <summary>
        /// Pobiera aktualny kanał aktualizacji z ustawień użytkownika
        /// </summary>
        public string GetCurrentUpdateChannel()
        {
            return GetUpdateChannel();
        }

        /// <summary>
        /// Ustawia kanał aktualizacji w ustawieniach użytkownika i resetuje UpdateManager
        /// </summary>
        public void SetUpdateChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel) || (channel != "release" && channel != "beta"))
                throw new ArgumentException("Invalid channel. Must be 'release' or 'beta'.", nameof(channel));

            _userSettingsService.UpdateUserSetting(settings => settings.UpdateChannel = channel);

            // Reset UpdateManager aby użyć nowego kanału przy następnym sprawdzeniu
            lock (_initializationLock)
            {
                _apiSource?.Dispose();
                _apiSource = null;
                _updateManager = null;
            }

            _diagnosticsOutput.Write($"[Velopack] Update channel changed to: {channel}");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private async Task EnsureInitializedAsync()
        {
            if (_updateManager == null)
                await InitializeAsync().ConfigureAwait(false);
        }

        private static Uri EnsureAbsoluteUri(string candidate)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
                return absolute;

            throw new InvalidOperationException($"Update feed URL '{candidate}' is not a valid absolute URI.");
        }

        private Uri GetUpdateFeedUri()
        {
            var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("Configuration:BaseUrl is not configured.");

            return EnsureAbsoluteUri($"{baseUrl}/api/releases");
        }

        private string GetUpdateChannel()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                var channel = userSettings.UpdateChannel;

                // Walidacja kanału - dopuszczamy tylko "release" lub "beta"
                if (string.IsNullOrWhiteSpace(channel) || (channel != "release" && channel != "beta"))
                {
                    _diagnosticsOutput.Write($"[Velopack] Invalid update channel '{channel}', defaulting to 'release'");
                    return "release";
                }

                return channel;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"[Velopack] Failed to load update channel: {ex.Message}, defaulting to 'release'");
                return "release";
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _apiSource?.Dispose();
                _apiSource = null;

                _updateManager = null;
            }

            _disposed = true;
        }

        ~VelopackUpdateService()
        {
            Dispose(false);
        }
    }

    public readonly struct VelopackUpdateCheckResult
    {
        private VelopackUpdateCheckResult(bool success, bool isUpdateAvailable, string currentVersion, string latestVersion, string? errorMessage, UpdateInfo? updateInfo)
        {
            Success = success;
            IsUpdateAvailable = isUpdateAvailable;
            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            ErrorMessage = errorMessage;
            UpdateInfo = updateInfo;
        }

        public bool Success { get; }
        public bool IsUpdateAvailable { get; }
        public string CurrentVersion { get; }
        public string LatestVersion { get; }
        public string? ErrorMessage { get; }
        public UpdateInfo? UpdateInfo { get; }

        public static VelopackUpdateCheckResult UpdateAvailable(string currentVersion, string latestVersion, UpdateInfo updateInfo) =>
            new(true, true, currentVersion, latestVersion, null, updateInfo ?? throw new ArgumentNullException(nameof(updateInfo)));

        public static VelopackUpdateCheckResult NoUpdate(string currentVersion) =>
            new(true, false, currentVersion, currentVersion, null, null);

        public static VelopackUpdateCheckResult Failed(string currentVersion, Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            return new(false, false, currentVersion, currentVersion, exception.Message, null);
        }
    }

    public readonly struct VelopackUpdateDownloadResult
    {
        private VelopackUpdateDownloadResult(bool success, string? errorMessage, UpdateInfo? updateInfo)
        {
            Success = success;
            ErrorMessage = errorMessage;
            UpdateInfo = updateInfo;
        }

        public bool Success { get; }
        public string? ErrorMessage { get; }
        public UpdateInfo? UpdateInfo { get; }

        public static VelopackUpdateDownloadResult Successful(UpdateInfo updateInfo) =>
            new(true, null, updateInfo ?? throw new ArgumentNullException(nameof(updateInfo)));

        public static VelopackUpdateDownloadResult Failed(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            return new(false, exception.Message, null);
        }
    }
}

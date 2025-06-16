using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Services
{
    public class AppUpdateService
    {
        private readonly string _currentVersion;
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;

        public AppUpdateService(string currentVersion, IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput)
        {
            _currentVersion = currentVersion ?? "0.0.0";
            _configuration = configuration;
            _diagnosticsOutput = diagnosticsOutput;
        }

        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                _diagnosticsOutput.Write("Sprawdzanie dostępności aktualizacji...");

                string latestVersion = await GetLatestVersionAsync();
                bool isUpdateAvailable = string.Compare(latestVersion, _currentVersion, StringComparison.OrdinalIgnoreCase) > 0;

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = isUpdateAvailable,
                    CurrentVersion = _currentVersion,
                    LatestVersion = latestVersion,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas sprawdzania aktualizacji: {ex.Message}");
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    LatestVersion = _currentVersion,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<UpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null)
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "SUSModder_Update.zip");

            try
            {
                _diagnosticsOutput.Write("Rozpoczynanie pobierania aktualizacji...");
                progress?.Report(0);

                using (HttpClient client = new HttpClient())
                {
                    // Pobierz najnowszą wersję
                    string downloadUrl = GetDownloadUrl();

                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var totalBytesRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            do
                            {
                                var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead == 0)
                                {
                                    isMoreToRead = false;
                                    continue;
                                }

                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;

                                if (totalBytes != -1L)
                                {
                                    var progressPercentage = (int)((totalBytesRead * 100L) / totalBytes);
                                    progress?.Report(progressPercentage);
                                }
                            }
                            while (isMoreToRead);
                        }
                    }
                }

                progress?.Report(100);
                _diagnosticsOutput.Write("Pobieranie aktualizacji zakończone pomyślnie");

                return new UpdateDownloadResult
                {
                    Success = true,
                    FilePath = tempFilePath
                };
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas pobierania aktualizacji: {ex.Message}");

                // Usuń niepełny plik jeśli istnieje
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }

                return new UpdateDownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public bool RunUpdater(string updateFilePath)
        {
            try
            {
                string? appDirPath = Path.GetDirectoryName(Environment.ProcessPath);
                if (string.IsNullOrEmpty(appDirPath))
                {
                    throw new InvalidOperationException("Nie można określić katalogu aplikacji.");
                }

                string updaterPath = Path.Combine(appDirPath, "updater", "updater.exe");

                if (!File.Exists(updaterPath))
                {
                    throw new FileNotFoundException($"Nie znaleziono updatera w ścieżce: {updaterPath}");
                }

                _diagnosticsOutput.Write($"Uruchamianie updatera: {updaterPath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true,
                    Arguments = $"\"{appDirPath}\" \"{updateFilePath}\""
                });

                return true;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas uruchamiania updatera: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetLatestVersionAsync()
        {
            using (HttpClient client = new HttpClient())
            {
                string versionUrl = GetVersionCheckUrl();
                HttpResponseMessage response = await client.GetAsync(versionUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var versionInfo = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                return versionInfo?["version"] ?? "0.0.0";
            }
        }

        private string GetVersionCheckUrl()
        {
            var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
            return $"{baseUrl}/api/susmodder-current-version";
        }

        private string GetDownloadUrl()
        {
            var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
            return $"{baseUrl}/api/download-latest";
        }
    }

    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class UpdateDownloadResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

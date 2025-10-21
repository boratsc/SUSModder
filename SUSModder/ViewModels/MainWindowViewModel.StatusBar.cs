using ReactiveUI;
using SUSModder.Core.Utilities;
using SUSModder.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class MainWindowViewModel - Status Bar functionality
    /// </summary>
    public partial class MainWindowViewModel
    {
        #region Status Bar Properties

        // Statystyki modów
        private int _installedModsCount;
        private int _installedFullModsCount;
        private int _installedDllModsCount;
        private List<string> _installedModsList = new();

        public int InstalledModsCount
        {
            get => _installedModsCount;
            set => this.RaiseAndSetIfChanged(ref _installedModsCount, value);
        }

        public int InstalledFullModsCount
        {
            get => _installedFullModsCount;
            set => this.RaiseAndSetIfChanged(ref _installedFullModsCount, value);
        }

        public int InstalledDllModsCount
        {
            get => _installedDllModsCount;
            set => this.RaiseAndSetIfChanged(ref _installedDllModsCount, value);
        }

        public List<string> InstalledModsList
        {
            get => _installedModsList;
            set => this.RaiseAndSetIfChanged(ref _installedModsList, value);
        }

        // Przestrzeń dyskowa
        private double _modsFolderSizeGB;
        private double _totalDiskSpaceGB;
        private double _diskUsagePercentage;
        private string _diskSpaceDetailsTooltip = string.Empty;

        public double ModsFolderSizeGB
        {
            get => _modsFolderSizeGB;
            set => this.RaiseAndSetIfChanged(ref _modsFolderSizeGB, value);
        }

        public double TotalDiskSpaceGB
        {
            get => _totalDiskSpaceGB;
            set => this.RaiseAndSetIfChanged(ref _totalDiskSpaceGB, value);
        }

        public double DiskUsagePercentage
        {
            get => _diskUsagePercentage;
            set => this.RaiseAndSetIfChanged(ref _diskUsagePercentage, value);
        }

        public string DiskSpaceDetailsTooltip
        {
            get => _diskSpaceDetailsTooltip;
            set => this.RaiseAndSetIfChanged(ref _diskSpaceDetailsTooltip, value);
        }

        // Status API
        private ApiConnectionStatus _apiStatus = ApiConnectionStatus.Checking;
        private int _apiPingMs;
        private DateTime _lastApiCheck = DateTime.Now;
        private string _apiBaseUrl = string.Empty;

        public ApiConnectionStatus ApiStatus
        {
            get => _apiStatus;
            set
            {
                this.RaiseAndSetIfChanged(ref _apiStatus, value);
                this.RaisePropertyChanged(nameof(ApiStatusText));
                this.RaisePropertyChanged(nameof(ApiStatusColor));
                this.RaisePropertyChanged(nameof(IsApiOnline));
            }
        }

        public int ApiPingMs
        {
            get => _apiPingMs;
            set
            {
                this.RaiseAndSetIfChanged(ref _apiPingMs, value);
                this.RaisePropertyChanged(nameof(ApiStatusText));
            }
        }

        public DateTime LastApiCheck
        {
            get => _lastApiCheck;
            set => this.RaiseAndSetIfChanged(ref _lastApiCheck, value);
        }

        public string ApiBaseUrl
        {
            get => _apiBaseUrl;
            set => this.RaiseAndSetIfChanged(ref _apiBaseUrl, value);
        }

        public string ApiStatusText => ApiStatus switch
        {
            ApiConnectionStatus.Online => $"Online ({ApiPingMs}ms)",
            ApiConnectionStatus.Offline => "Offline",
            ApiConnectionStatus.Checking => "Sprawdzanie...",
            _ => "Nieznany"
        };

        public string ApiStatusColor => ApiStatus switch
        {
            ApiConnectionStatus.Online => "#4CAF50", // Zielony
            ApiConnectionStatus.Offline => "#F44336", // Czerwony
            ApiConnectionStatus.Checking => "#FFC107", // Żółty
            _ => "#9E9E9E" // Szary
        };

        public bool IsApiOnline => ApiStatus == ApiConnectionStatus.Online;

        #endregion

        #region Status Bar Methods

        /// <summary>
        /// Odświeża wszystkie statystyki panelu statusu
        /// </summary>
        public async Task RefreshStatusBarAsync()
        {
            await Task.Run(() =>
            {
                UpdateModsStatistics();
                UpdateDiskSpaceStatistics();
            });

            await CheckApiConnectionAsync();
        }

        /// <summary>
        /// Aktualizuje statystyki zainstalowanych modów
        /// </summary>
        private void UpdateModsStatistics()
        {
            try
            {
                InstalledFullModsCount = Mods.Count(m => m.ModType == "full" && m.IsInstalled);
                InstalledDllModsCount = Mods.Count(m => m.ModType == "dll" && m.IsInstalled);
                InstalledModsCount = InstalledFullModsCount + InstalledDllModsCount;

                // Lista nazw zainstalowanych modów (max 10)
                var installedMods = Mods
                    .Where(m => m.IsInstalled)
                    .Select(m => m.Name)
                    .Take(10)
                    .ToList();

                if (Mods.Count(m => m.IsInstalled) > 10)
                {
                    installedMods.Add($"...i {Mods.Count(m => m.IsInstalled) - 10} więcej");
                }

                InstalledModsList = installedMods;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating mods statistics: {ex.Message}");
            }
        }

        /// <summary>
        /// Oblicza zajętość dysku przez folder z modami
        /// </summary>
        private void UpdateDiskSpaceStatistics()
        {
            try
            {
                var modsPath = PathSettings.ModsInstallPath;

                if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
                {
                    ModsFolderSizeGB = 0;
                    TotalDiskSpaceGB = 0;
                    DiskUsagePercentage = 0;
                    DiskSpaceDetailsTooltip = "Folder modów nie istnieje";
                    return;
                }

                // Oblicz rozmiar folderu z modami
                var directoryInfo = new DirectoryInfo(modsPath);
                long totalSize = CalculateDirectorySize(directoryInfo);
                ModsFolderSizeGB = totalSize / (1024.0 * 1024.0 * 1024.0); // Bytes to GB

                // Pobierz dostępną przestrzeń na dysku
                var driveInfo = new DriveInfo(Path.GetPathRoot(modsPath) ?? "C:\\");
                TotalDiskSpaceGB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

                // Oblicz procent zajętości (względem całego dysku)
                DiskUsagePercentage = (ModsFolderSizeGB / TotalDiskSpaceGB) * 100;

                // Tooltip ze szczegółami
                DiskSpaceDetailsTooltip = $"Folder modów: {ModsFolderSizeGB:F2} GB\n" +
                                         $"Wolne miejsce na dysku: {freeSpaceGB:F2} GB\n" +
                                         $"Całkowita przestrzeń: {TotalDiskSpaceGB:F2} GB\n" +
                                         $"Ścieżka: {modsPath}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating disk space: {ex.Message}");
                ModsFolderSizeGB = 0;
                TotalDiskSpaceGB = 0;
                DiskUsagePercentage = 0;
                DiskSpaceDetailsTooltip = $"Błąd: {ex.Message}";
            }
        }

        /// <summary>
        /// Rekurencyjnie oblicza rozmiar katalogu
        /// </summary>
        private long CalculateDirectorySize(DirectoryInfo directory)
        {
            long size = 0;

            try
            {
                // Rozmiar plików w bieżącym katalogu
                FileInfo[] files = directory.GetFiles();
                foreach (FileInfo file in files)
                {
                    size += file.Length;
                }

                // Rekurencyjnie dla podkatalogów
                DirectoryInfo[] subdirs = directory.GetDirectories();
                foreach (DirectoryInfo subdir in subdirs)
                {
                    size += CalculateDirectorySize(subdir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignoruj katalogi bez dostępu
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating directory size: {ex.Message}");
            }

            return size;
        }

        /// <summary>
        /// Sprawdza status połączenia z API
        /// </summary>
        private async Task CheckApiConnectionAsync()
        {
            ApiStatus = ApiConnectionStatus.Checking;

            try
            {
                // Załaduj konfigurację
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                var configuration = configBuilder.Build();

                var baseUrl = configuration["Configuration:BaseUrl"]?.TrimEnd('/');
                ApiBaseUrl = baseUrl ?? "Nie ustawiono";

                if (string.IsNullOrEmpty(baseUrl))
                {
                    ApiStatus = ApiConnectionStatus.Offline;
                    return;
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                
                // Sprawdzamy endpoint do pobierania konfiguracji (UpdateServerUrl)
                var updateServerUrl = configuration["Configuration:UpdateServerUrl"];
                if (string.IsNullOrEmpty(updateServerUrl))
                {
                    // Fallback do baseUrl
                    updateServerUrl = $"{baseUrl}/api/susmodder-current-version";
                }

                var response = await client.GetAsync(updateServerUrl);

                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    ApiStatus = ApiConnectionStatus.Online;
                    ApiPingMs = (int)stopwatch.ElapsedMilliseconds;
                }
                else
                {
                    ApiStatus = ApiConnectionStatus.Offline;
                }

                LastApiCheck = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API connection check failed: {ex.Message}");
                ApiStatus = ApiConnectionStatus.Offline;
                LastApiCheck = DateTime.Now;
            }
        }

        /// <summary>
        /// Timer do auto-refresh statusu API (co 30 sekund)
        /// </summary>
        private void StartApiStatusAutoRefresh()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    
                    try
                    {
                        await CheckApiConnectionAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-refresh API status error: {ex.Message}");
                    }
                }
            });
        }

        #endregion
    }
}

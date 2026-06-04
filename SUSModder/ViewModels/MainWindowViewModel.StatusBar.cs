using ReactiveUI;
using SUSModder.Core.Utilities;
using SUSModder.Core.Services;
using SUSModder.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Avalonia.Threading;

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
        private double _freeDiskSpaceGB;
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

        public double FreeDiskSpaceGB
        {
            get => _freeDiskSpaceGB;
            set => this.RaiseAndSetIfChanged(ref _freeDiskSpaceGB, value);
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
        private DateTime _lastConfigSyncUtc = DateTime.MinValue;
        private DateTime _lastModUpdateCheckUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _modUpdateCheckSemaphore = new(1, 1);
        private static readonly TimeSpan ConfigSyncInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ModUpdatesRefreshInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MinModUpdateCheckInterval = TimeSpan.FromMinutes(2);
        private string _apiBaseUrl = string.Empty;
        private int _onlineUsersCount;
        private CancellationTokenSource? _statusBarCts;

        public int OnlineUsersCount
        {
            get => _onlineUsersCount;
            set
            {
                this.RaiseAndSetIfChanged(ref _onlineUsersCount, value);
                this.RaisePropertyChanged(nameof(OnlineUsersText));
            }
        }

        public string OnlineUsersText => _localizationService.GetFormatted("UI.StatusBar.OnlineUsers", OnlineUsersCount);

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
            ApiConnectionStatus.Online => _localizationService.GetFormatted("UI.StatusBar.ApiOnline", ApiPingMs),
            ApiConnectionStatus.Offline => _localizationService.Get("UI.StatusBar.ApiOffline"),
            ApiConnectionStatus.Checking => _localizationService.Get("UI.StatusBar.ApiChecking"),
            _ => _localizationService.Get("UI.StatusBar.ApiUnknown")
        };

        public string ApiStatusColor => ApiStatus switch
        {
            ApiConnectionStatus.Online => "#4CAF50", // Zielony
            ApiConnectionStatus.Offline => "#F44336", // Czerwony
            ApiConnectionStatus.Checking => "#FFC107", // Żółty
            _ => "#9E9E9E" // Szary
        };

        public bool IsApiOnline => ApiStatus == ApiConnectionStatus.Online;

        // Dostępne aktualizacje modów
        private int _availableUpdatesCount;
        private List<string> _availableUpdatesList = new();
        private string _availableUpdatesTooltip = string.Empty;
        private string _modsStatusMainText = string.Empty;
        private string _modsStatusSubText = string.Empty;
        private string _modsStatusTooltip = string.Empty;

public int AvailableUpdatesCount
        {
            get => _availableUpdatesCount;
            set
            {
                System.Diagnostics.Debug.WriteLine($"[FAB-DEBUG] AvailableUpdatesCount SET: {_availableUpdatesCount} -> {value}");
                this.RaiseAndSetIfChanged(ref _availableUpdatesCount, value);
                this.RaisePropertyChanged(nameof(FabHasBadge));
                this.RaisePropertyChanged(nameof(FabBadgeCount));
                this.RaisePropertyChanged(nameof(FabBadgeTooltip));
                this.RaisePropertyChanged(nameof(FabIconSymbol));
                System.Diagnostics.Debug.WriteLine($"[FAB-DEBUG] After RaisePropertyChanged: FabHasBadge={FabHasBadge}, FabBadgeCount={FabBadgeCount}");
            }
        }

        public List<string> AvailableUpdatesList
        {
            get => _availableUpdatesList;
            set => this.RaiseAndSetIfChanged(ref _availableUpdatesList, value);
        }

        public string AvailableUpdatesTooltip
        {
            get => _availableUpdatesTooltip;
            set => this.RaiseAndSetIfChanged(ref _availableUpdatesTooltip, value);
        }

        /// <summary>
        /// Główny tekst sekcji modów - pokazuje aktualizacje lub zainstalowane mody
        /// </summary>
        public string ModsStatusMainText
        {
            get => _modsStatusMainText;
            set => this.RaiseAndSetIfChanged(ref _modsStatusMainText, value);
        }

        /// <summary>
        /// Tekst pomocniczy pod głównym - liczba zainstalowanych modów
        /// </summary>
        public string ModsStatusSubText
        {
            get => _modsStatusSubText;
            set => this.RaiseAndSetIfChanged(ref _modsStatusSubText, value);
        }

        /// <summary>
        /// Tooltip dla sekcji modów - zawiera listę zainstalowanych i dostępne aktualizacje
        /// </summary>
        public string ModsStatusTooltip
        {
            get => _modsStatusTooltip;
            set => this.RaiseAndSetIfChanged(ref _modsStatusTooltip, value);
        }

        #endregion

        #region Status Bar Methods

        /// <summary>
        /// Odświeża wszystkie statystyki panelu statusu
        /// </summary>
        public async Task RefreshStatusBarAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
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
                    var moreCount = Mods.Count(m => m.IsInstalled) - 10;
                    installedMods.Add(_localizationService.GetFormatted("UI.StatusBar.AndMoreMods", moreCount));
                }

                InstalledModsList = installedMods;

                // Aktualizuj status modów (wywoływane przy każdej zmianie listy modów)
                UpdateModsStatusDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating mods statistics: {ex.Message}");
            }
        }

        /// <summary>
        /// Aktualizuje wyświetlanie statusu modów (główny/pomocniczy tekst + tooltip)
        /// </summary>
        private void UpdateModsStatusDisplay()
        {
            // Główny tekst i tekst pomocniczy zależą od dostępności aktualizacji
            if (AvailableUpdatesCount > 0)
            {
                // Gdy są aktualizacje - pokazujemy je w głównym tekście
                ModsStatusMainText = _localizationService.GetFormatted("UI.StatusBar.AvailableUpdatesCount", AvailableUpdatesCount);
                // A poniżej pokazujemy liczbę zainstalowanych
                ModsStatusSubText = _localizationService.GetFormatted("UI.StatusBar.InstalledModsCount", InstalledFullModsCount);
            }
            else
            {
                // Gdy nie ma aktualizacji - pokazujemy tylko zainstalowane w głównym tekście
                ModsStatusMainText = _localizationService.GetFormatted("UI.StatusBar.InstalledModsCount", InstalledFullModsCount);
                // Ukrywamy tekst pomocniczy (pusty string)
                ModsStatusSubText = string.Empty;
            }

            // Tooltip - połączenie zainstalowanych i dostępnych aktualizacji
            BuildModsStatusTooltip();
        }

        /// <summary>
        /// Tworzy tooltip z listą zainstalowanych modów i dostępnych aktualizacji
        /// </summary>
        private void BuildModsStatusTooltip()
        {
            var tooltipBuilder = new System.Text.StringBuilder();

            // Sekcja zainstalowanych modów
            if (InstalledModsList.Any())
            {
                tooltipBuilder.AppendLine(_localizationService.Get("UI.StatusBar.InstalledModsList"));
                foreach (var mod in InstalledModsList)
                {
                    tooltipBuilder.AppendLine($"  • {mod}");
                }
            }

            // Sekcja dostępnych aktualizacji (jeśli są)
            if (AvailableUpdatesList.Any())
            {
                if (tooltipBuilder.Length > 0)
                    tooltipBuilder.AppendLine();

                tooltipBuilder.AppendLine(_localizationService.Get("UI.StatusBar.UpdatesRequiredList"));
                foreach (var update in AvailableUpdatesList)
                {
                    tooltipBuilder.AppendLine($"  • {update}");
                }
            }

            ModsStatusTooltip = tooltipBuilder.ToString().TrimEnd();
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
                    DiskSpaceDetailsTooltip = _localizationService.Get("UI.StatusBar.ModsFolderNotExist");
                    return;
                }

                // Oblicz rozmiar folderu z modami
                var directoryInfo = new DirectoryInfo(modsPath);
                long totalSize = CalculateDirectorySize(directoryInfo);
                ModsFolderSizeGB = totalSize / (1024.0 * 1024.0 * 1024.0); // Bytes to GB

                // Pobierz dostępną przestrzeń na dysku
                var driveInfo = new DriveInfo(Path.GetPathRoot(modsPath) ?? "C:\\");
                TotalDiskSpaceGB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                FreeDiskSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

                // Oblicz procent zajętości (względem wolnego miejsca + zajętego przez mody)
                // tj. ile % z dostępnej przestrzeni zajmują mody
                double totalAvailableSpace = FreeDiskSpaceGB + ModsFolderSizeGB;
                DiskUsagePercentage = totalAvailableSpace > 0 
                    ? (ModsFolderSizeGB / totalAvailableSpace) * 100 
                    : 0;

                // Tooltip ze szczegółami
                DiskSpaceDetailsTooltip = $"{_localizationService.GetFormatted("UI.StatusBar.ModsFolder", ModsFolderSizeGB)}\n" +
                                         $"{_localizationService.GetFormatted("UI.StatusBar.FreeSpace", FreeDiskSpaceGB)}\n" +
                                         $"{_localizationService.GetFormatted("UI.StatusBar.TotalSpace", TotalDiskSpaceGB)}\n" +
                                         $"{_localizationService.GetFormatted("UI.StatusBar.Path", modsPath)}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating disk space: {ex.Message}");
                ModsFolderSizeGB = 0;
                TotalDiskSpaceGB = 0;
                DiskUsagePercentage = 0;
                DiskSpaceDetailsTooltip = _localizationService.GetFormatted("UI.StatusBar.Error", ex.Message);
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
                    .SetBasePath(SUSModder.Core.Utilities.ApplicationPaths.GetApplicationDirectory())
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

                    // Synchronizuj config z API nie częściej niż co 15 minut.
                    var nowUtc = DateTime.UtcNow;
                    if (nowUtc - _lastConfigSyncUtc >= ConfigSyncInterval)
                    {
                        var configService = new ConfigService();
                        bool configRefreshed = await configService.RefreshConfigFromApiAsync();
                        _lastConfigSyncUtc = nowUtc;

                        if (configRefreshed)
                        {
                            // Pomiń odświeżenie jeśli trwa instalacja (nie niszcz ModItem w trakcie)
                            if (_activeInstallationsCount == 0)
                            {
                                await RefreshModsListAsync(checkUpdates: false, deferIfToolModalOpen: true);
                            }
                        }
                    }
                    
                    // Pobierz liczbę użytkowników online
                    await FetchOnlineUsersAsync(baseUrl, client);
                }
                else
                {
                    ApiStatus = ApiConnectionStatus.Offline;
                    OnlineUsersCount = 0;
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
        /// Pobiera liczbę użytkowników online z API
        /// </summary>
        private async Task FetchOnlineUsersAsync(string baseUrl, HttpClient client)
        {
            try
            {
                var onlineUsersUrl = $"{baseUrl}/api/online-users";
                var response = await client.GetAsync(onlineUsersUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<OnlineUsersResponse>(json);
                    OnlineUsersCount = data?.Online ?? 0;
                }
                else
                {
                    OnlineUsersCount = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching online users: {ex.Message}");
                OnlineUsersCount = 0;
            }
        }

        /// <summary>
        /// Timer do auto-refresh statusu API (co 30 sekund)
        /// Używa CancellationToken, aby można było przerwać przy Dispose.
        /// </summary>
        private void StartApiStatusAutoRefresh()
        {
            _statusBarCts = new CancellationTokenSource();
            var token = _statusBarCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Normalne przerwanie przy Dispose, wychodzimy
                        break;
                    }

                    if (token.IsCancellationRequested)
                        break;

                    try
                    {
                        await CheckApiConnectionAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-refresh API status error: {ex.Message}");
                    }
                }
            }, token);
        }

        /// <summary>
        /// Sprawdza dostępne aktualizacje modów (bez wyświetlania dialogu)
        /// </summary>
        private async Task CheckForModUpdatesForStatusBarAsync(bool force = false)
        {
            if (!force && DateTime.UtcNow - _lastModUpdateCheckUtc < MinModUpdateCheckInterval)
                return;

            if (!await _modUpdateCheckSemaphore.WaitAsync(0))
                return;

            try
            {
                _lastModUpdateCheckUtc = DateTime.UtcNow;
                var updateManager = new ModUpdateManager();
                var result = await updateManager.CheckForUpdatesAsync();

                // Jeśli config został zaktualizowany (np. doszły nowe mody),
                // przeładuj listę modów od razu bez czekania na restart aplikacji.
                // Pomiń jeśli trwa instalacja (nie niszcz ModItem w trakcie).
                if (result.ConfigWasUpdated && _activeInstallationsCount == 0)
                {
                    await RefreshModsListAsync(checkUpdates: false, deferIfToolModalOpen: true);
                }

                if (result.Success && result.InstalledModUpdates.Any())
                {
                    AvailableUpdatesCount = result.InstalledModUpdates.Count;
                    AvailableUpdatesList = result.InstalledModUpdates
                        .Select(u => $"{u.ModName} ({u.CurrentVersion} → {u.NewVersion})")
                        .ToList();

                    // Stwórz tooltip
                    var tooltipBuilder = new System.Text.StringBuilder();
                    tooltipBuilder.AppendLine("Dostępne aktualizacje modów:");
                    foreach (var update in result.InstalledModUpdates)
                    {
                        tooltipBuilder.AppendLine($"• {update.ModName}");
                    }
                    AvailableUpdatesTooltip = tooltipBuilder.ToString().TrimEnd();

                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        SyncModUpdateBadges(result.InstalledModUpdates.Select(u => u.ModName));
                        await RefreshPackInstancesAsync();
                    });

                    // Auto-aktualizacja w tle dla modów z włączoną auto-aktualizacją
                    // Nie blokujemy - uruchamiamy w tle
                    _ = ProcessAutoUpdatesSilentlyAsync(result.InstalledModUpdates);
                }
                else
                {
                    AvailableUpdatesCount = 0;
                    AvailableUpdatesList.Clear();
                    AvailableUpdatesTooltip = string.Empty;

                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        SyncModUpdateBadges(Array.Empty<string>());
                        await RefreshPackInstancesAsync();
                    });
                }

                // Aktualizuj wyświetlanie statusu po sprawdzeniu aktualizacji
                await Dispatcher.UIThread.InvokeAsync(() => UpdateModsStatusDisplay());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for mod updates in status bar: {ex.Message}");
                AvailableUpdatesCount = 0;
                AvailableUpdatesList.Clear();
                AvailableUpdatesTooltip = string.Empty;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SyncModUpdateBadges(Array.Empty<string>());
                    UpdateModsStatusDisplay();
                });
            }
            finally
            {
                _modUpdateCheckSemaphore.Release();
            }
        }

        /// <summary>
        /// Timer do auto-refresh dostępnych aktualizacji modów (co 5 minut)
        /// </summary>
        private void StartModUpdatesAutoRefresh()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(ModUpdatesRefreshInterval);

                    try
                    {
                        await CheckForModUpdatesForStatusBarAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-refresh mod updates error: {ex.Message}");
                    }
                }
            });
        }

        #region IDisposable support

        /// <summary>
        /// Anuluje background task do auto-refresh statusu API i zwalnia semafor.
        /// </summary>
        private void CancelStatusBarBackgroundTask()
        {
            if (_statusBarCts != null)
            {
                _statusBarCts.Cancel();
                _statusBarCts.Dispose();
                _statusBarCts = null;
            }

            _modUpdateCheckSemaphore.Dispose();
        }

        #endregion

        #endregion
    }
}

using System;
using ReactiveUI;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    public class ModItem : ReactiveObject
    {
        // ── Statyczny dostęp do lokalizacji (ustawiany raz przez ModItem.InitializeLocalization) ──
        private static ILocalizationService? _loc;

        /// <summary>
        /// Wywołaj raz przy starcie aplikacji z referencją do serwisu lokalizacji.
        /// </summary>
        public static void InitializeLocalization(ILocalizationService localizationService)
        {
            _loc = localizationService;
        }

        private static ILocalizationService? Loc => _loc;
        private int _id;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _pngFileName = string.Empty;
        private string _modVersion = string.Empty;
        private string _amongVersion = string.Empty;
        private string? _installPath;
        private string _gitHubRepoOrLink = string.Empty;
        private string? _epicGitHubRepoOrLink;
        private string _modType = string.Empty;
        private string _dllInstallPath = string.Empty;
        private DateTime? _lastUpdated;
        private bool? _hasRoles;
        private string? _pinnedInstallVersion;
        private bool _disableAutoUpdatePrompt;
        private bool _autoUpdateEnabled;

        // Nowe właściwości dla instalacji
        private bool _isInstalling = false;
        private int _installProgress = 0;
        private string _installStatusMessage = string.Empty;
        private string? _downloadSpeed;
        private bool _showProgress = false;
        private bool _isCheckedForBulk;
        private bool _hasUpdateAvailable;
        private string? _targetInstanceId;
        private string _installedInSummary = string.Empty;
        private int _installedInCount;

        /// <summary>
        /// Gdy ustawione — instalacja DLL dotyczy lokalnej instancji modpacka (Moje zestawy).
        /// </summary>
        public string? TargetInstanceId
        {
            get => _targetInstanceId;
            set => this.RaiseAndSetIfChanged(ref _targetInstanceId, value);
        }

        // ── DLL Auto-Update properties ──
        private bool _dllAutoUpdateEnabled;
        private bool _dllAutoUpdateIsMixed;
        private bool _dllAutoUpdateNotInstalled;
        private int _dllInstallationCount;

        /// <summary>
        /// Auto-aktualizacja DLL włączona (globalnie dla wszystkich lokalizacji).
        /// </summary>
        public bool DllAutoUpdateEnabled
        {
            get => _dllAutoUpdateEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _dllAutoUpdateEnabled, value);
                this.RaisePropertyChanged(nameof(DllAutoUpdateLabel));
                this.RaisePropertyChanged(nameof(DllAutoUpdateTooltip));
            }
        }

        /// <summary>
        /// Część lokalizacji ma ON, część OFF (mixed state).
        /// </summary>
        public bool DllAutoUpdateIsMixed
        {
            get => _dllAutoUpdateIsMixed;
            set
            {
                this.RaiseAndSetIfChanged(ref _dllAutoUpdateIsMixed, value);
                this.RaisePropertyChanged(nameof(DllAutoUpdateLabel));
                this.RaisePropertyChanged(nameof(DllAutoUpdateTooltip));
            }
        }

        /// <summary>
        /// DLL nie jest nigdzie zainstalowany.
        /// </summary>
        public bool DllAutoUpdateNotInstalled
        {
            get => _dllAutoUpdateNotInstalled;
            set
            {
                this.RaiseAndSetIfChanged(ref _dllAutoUpdateNotInstalled, value);
                this.RaisePropertyChanged(nameof(DllAutoUpdateLabel));
                this.RaisePropertyChanged(nameof(DllAutoUpdateTooltip));
            }
        }

        /// <summary>
        /// Liczba modów FULL w których zainstalowany jest DLL.
        /// </summary>
        public int DllInstallationCount
        {
            get => _dllInstallationCount;
            set => this.RaiseAndSetIfChanged(ref _dllInstallationCount, value);
        }

        /// <summary>
        /// Etykieta stanu auto-update dla DLL (ON/OFF/MIXED/Nie zainstalowano).
        /// Używa kluczy i18n: UI.DllManager.AutoUpdateEnabled/Disabled/Mixed/NotInstalled.
        /// </summary>
        public string DllAutoUpdateLabel
        {
            get
            {
                if (DllAutoUpdateNotInstalled) return "—";
                if (DllAutoUpdateIsMixed) return Loc?.Get("UI.DllManager.AutoUpdateMixed") ?? "⚠ MIXED";
                return DllAutoUpdateEnabled
                    ? Loc?.Get("UI.DllManager.AutoUpdateEnabled") ?? "✓ ON"
                    : Loc?.Get("UI.DllManager.AutoUpdateDisabled") ?? "OFF";
            }
        }

        /// <summary>
        /// Tooltip dla toggla auto-update DLL.
        /// Używa kluczy i18n: UI.DllManager.AutoUpdateNotInstalled, UI.DllManager.AutoUpdateMixed,
        /// UI.DllManager.AutoUpdateEnabled, UI.DllManager.AutoUpdateDisabled.
        /// </summary>
        public string DllAutoUpdateTooltip
        {
            get
            {
                if (DllAutoUpdateNotInstalled)
                    return Loc?.Get("UI.DllManager.AutoUpdateNotInstalled") ?? "Zainstaluj DLL, aby włączyć auto-update";
                if (DllAutoUpdateIsMixed)
                    return Loc?.Get("UI.DllManager.AutoUpdateMixed") ?? "Niektóre lokalizacje mają różne ustawienia. Kliknij, aby ujednolicić.";
                return DllAutoUpdateEnabled
                    ? Loc?.Get("UI.DllManager.AutoUpdateEnabled") ?? "Auto-update włączone dla wszystkich lokalizacji"
                    : Loc?.Get("UI.DllManager.AutoUpdateDisabled") ?? "Kliknij, aby włączyć auto-update";
            }
        }

        public int Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        public string PngFileName
        {
            get => _pngFileName;
            set
            {
                this.RaiseAndSetIfChanged(ref _pngFileName, value);
                this.RaisePropertyChanged(nameof(IconPath));
            }
        }

        public string ModVersion
        {
            get => _modVersion;
            set
            {
                this.RaiseAndSetIfChanged(ref _modVersion, value);
                this.RaisePropertyChanged(nameof(IsPinnedVersionInstall));
                this.RaisePropertyChanged(nameof(IsAutoUpdateToggleVisible));
            }
        }

        public string AmongVersion
        {
            get => _amongVersion;
            set => this.RaiseAndSetIfChanged(ref _amongVersion, value);
        }

        public string? InstallPath
        {
            get => _installPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _installPath, value);
                this.RaisePropertyChanged(nameof(IsInstalled));
                this.RaisePropertyChanged(nameof(CanInstall));
                this.RaisePropertyChanged(nameof(CanUninstall));
                this.RaisePropertyChanged(nameof(IsPinnedVersionInstall));
                this.RaisePropertyChanged(nameof(IsAutoUpdateToggleVisible));
                this.RaisePropertyChanged(nameof(ShowStatusInstalled));
                this.RaisePropertyChanged(nameof(ShowStatusNotInstalled));
            }
        }

        public string? PinnedInstallVersion
        {
            get => _pinnedInstallVersion;
            set
            {
                this.RaiseAndSetIfChanged(ref _pinnedInstallVersion, value);
                this.RaisePropertyChanged(nameof(IsPinnedVersionInstall));
                this.RaisePropertyChanged(nameof(IsAutoUpdateToggleVisible));
            }
        }

        public bool DisableAutoUpdatePrompt
        {
            get => _disableAutoUpdatePrompt;
            set
            {
                this.RaiseAndSetIfChanged(ref _disableAutoUpdatePrompt, value);
                this.RaisePropertyChanged(nameof(IsPinnedVersionInstall));
                this.RaisePropertyChanged(nameof(IsAutoUpdateToggleVisible));
            }
        }

        /// <summary>
        /// Gdy true, aktualizacje tego moda są instalowane automatycznie
        /// (bez wyświetlania dialogu potwierdzenia).
        /// </summary>
        public bool AutoUpdateEnabled
        {
            get => _autoUpdateEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _autoUpdateEnabled, value);
            }
        }

        /// <summary>
        /// Czy przełącznik auto-aktualizacji powinien być widoczny.
        /// Tylko dla zainstalowanych modów (nie Vanilla, nie przypiętych wersji).
        /// </summary>
        public bool IsAutoUpdateToggleVisible =>
            IsInstalled && !IsVanilla && !IsPinnedVersionInstall;

        public string GitHubRepoOrLink
        {
            get => _gitHubRepoOrLink;
            set => this.RaiseAndSetIfChanged(ref _gitHubRepoOrLink, value);
        }

        public string? EpicGitHubRepoOrLink
        {
            get => _epicGitHubRepoOrLink;
            set => this.RaiseAndSetIfChanged(ref _epicGitHubRepoOrLink, value);
        }

        public string ModType
        {
            get => _modType;
            set
            {
                this.RaiseAndSetIfChanged(ref _modType, value);
                this.RaisePropertyChanged(nameof(IsVanilla));
                this.RaisePropertyChanged(nameof(IsAutoUpdateToggleVisible));
            }
        }

        public string DllInstallPath
        {
            get => _dllInstallPath;
            set => this.RaiseAndSetIfChanged(ref _dllInstallPath, value);
        }

        public DateTime? LastUpdated
        {
            get => _lastUpdated;
            set => this.RaiseAndSetIfChanged(ref _lastUpdated, value);
        }

        public bool? HasRoles
        {
            get => _hasRoles;
            set => this.RaiseAndSetIfChanged(ref _hasRoles, value);
        }

        // Nowe właściwości dla instalacji
        public bool IsInstalling
        {
            get => _isInstalling;
            set
            {
                this.RaiseAndSetIfChanged(ref _isInstalling, value);
                this.RaisePropertyChanged(nameof(CanInstall));
                this.RaisePropertyChanged(nameof(CanUninstall));
                this.RaisePropertyChanged(nameof(ShowStatusBusy));
            }
        }

        public bool IsCheckedForBulk
        {
            get => _isCheckedForBulk;
            set => this.RaiseAndSetIfChanged(ref _isCheckedForBulk, value);
        }

        public bool HasUpdateAvailable
        {
            get => _hasUpdateAvailable;
            set
            {
                this.RaiseAndSetIfChanged(ref _hasUpdateAvailable, value);
                this.RaisePropertyChanged(nameof(ShowStatusUpdate));
            }
        }

        public bool ShowStatusBusy => IsInstalling;
        public bool ShowStatusUpdate => HasUpdateAvailable && !IsInstalling;
        public bool ShowStatusInstalled => IsInstalled && !ShowStatusBusy && !ShowStatusUpdate;
        public bool ShowStatusNotInstalled => !IsInstalled && !ShowStatusBusy;
        public bool IsBulkEligible => !IsVanilla && IsFullMod;

        public bool IsDllBulkEligible => IsDllMod;

        public int InstalledInCount
        {
            get => _installedInCount;
            set
            {
                this.RaiseAndSetIfChanged(ref _installedInCount, value);
                this.RaisePropertyChanged(nameof(InstalledInSummary));
            }
        }

        public string InstalledInSummary
        {
            get => _installedInSummary;
            set => this.RaiseAndSetIfChanged(ref _installedInSummary, value);
        }

        public string BrowserSubtitle =>
            string.IsNullOrWhiteSpace(ModVersion)
                ? (string.IsNullOrWhiteSpace(AmongVersion) ? string.Empty : AmongVersion)
                : string.IsNullOrWhiteSpace(AmongVersion)
                    ? $"v{ModVersion}"
                    : $"v{ModVersion} · {AmongVersion}";

        public string TypeBadge => IsDllMod ? "DLL" : IsFullMod ? "FULL" : ModType;

        public int InstallProgress
        {
            get => _installProgress;
            set => this.RaiseAndSetIfChanged(ref _installProgress, value);
        }

        public string InstallStatusMessage
        {
            get => _installStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _installStatusMessage, value);
        }

        public string? DownloadSpeed
        {
            get => _downloadSpeed;
            set => this.RaiseAndSetIfChanged(ref _downloadSpeed, value);
        }

        public bool ShowProgress
        {
            get => _showProgress;
            set => this.RaiseAndSetIfChanged(ref _showProgress, value);
        }

        // Właściwość do bindowania ścieżki do ikony
        public string IconPath => $"avares://SUSModder/Assets/{PngFileName}";

        // Pomocnicze właściwości
        public bool IsInstalled => !string.IsNullOrEmpty(InstallPath);
        public bool IsFullMod => ModType.Equals("full", StringComparison.OrdinalIgnoreCase);
        public bool IsDllMod => ModType.Equals("dll", StringComparison.OrdinalIgnoreCase);
        // Lobby Board
        private string? _lobbyRegionBaseUrl;

        public string? LobbyRegionBaseUrl
        {
            get => _lobbyRegionBaseUrl;
            set => this.RaiseAndSetIfChanged(ref _lobbyRegionBaseUrl, value);
        }

        private bool _supportsLobbySharing;

        public bool SupportsLobbySharing
        {
            get => _supportsLobbySharing;
            set => this.RaiseAndSetIfChanged(ref _supportsLobbySharing, value);
        }

        public bool IsVanilla => ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);
        public bool IsPinnedVersionInstall =>
            IsInstalled &&
            DisableAutoUpdatePrompt &&
            !string.IsNullOrWhiteSpace(PinnedInstallVersion) &&
            string.Equals(ModVersion, PinnedInstallVersion, StringComparison.OrdinalIgnoreCase);

        // Nowe właściwości dla kontroli przycisków
        public bool CanInstall => !IsInstalled && !IsInstalling;
        public bool CanUninstall => IsInstalled && !IsInstalling;

        // ── VirusTotal / Security Scan (z configu / DB) ──
        private string? _vtScanStatus;
        private string? _vtPermalink;
        private string? _vtLastCheckedAt;
        private string? _vtStats;
        private string? _vtAiReviewStatus;
        private string? _vtAiReviewSummary;

        public string? VtScanStatus
        {
            get => _vtScanStatus;
            set
            {
                this.RaiseAndSetIfChanged(ref _vtScanStatus, value);
                this.RaisePropertyChanged(nameof(VtStatusEmoji));
                this.RaisePropertyChanged(nameof(VtTooltip));
                this.RaisePropertyChanged(nameof(IsVtClean));
                this.RaisePropertyChanged(nameof(IsVtRisky));
            }
        }

        public string? VtPermalink
        {
            get => _vtPermalink;
            set => this.RaiseAndSetIfChanged(ref _vtPermalink, value);
        }

        public string? VtLastCheckedAt
        {
            get => _vtLastCheckedAt;
            set => this.RaiseAndSetIfChanged(ref _vtLastCheckedAt, value);
        }

        public string? VtStats
        {
            get => _vtStats;
            set
            {
                this.RaiseAndSetIfChanged(ref _vtStats, value);
                this.RaisePropertyChanged(nameof(VtTooltip));
            }
        }

        public string? VtAiReviewStatus
        {
            get => _vtAiReviewStatus;
            set
            {
                this.RaiseAndSetIfChanged(ref _vtAiReviewStatus, value);
                this.RaisePropertyChanged(nameof(VtTooltip));
            }
        }

        public string? VtAiReviewSummary
        {
            get => _vtAiReviewSummary;
            set
            {
                this.RaiseAndSetIfChanged(ref _vtAiReviewSummary, value);
                this.RaisePropertyChanged(nameof(VtTooltip));
            }
        }

        // ── Właściwości pochodne dla UI ──

        /// <summary>Czy mamy jakikolwiek raport VT (status != null).</summary>
        public bool HasVtReport => !string.IsNullOrWhiteSpace(VtScanStatus);

        /// <summary>Czy status to clean.</summary>
        public bool IsVtClean => string.Equals(VtScanStatus, "clean", StringComparison.OrdinalIgnoreCase);

        /// <summary>Czy status to suspicious lub malicious.</summary>
        public bool IsVtRisky =>
            string.Equals(VtScanStatus, "suspicious", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(VtScanStatus, "malicious", StringComparison.OrdinalIgnoreCase);

        /// <summary>Czy mamy permalink.</summary>
        public bool HasVtPermalink => !string.IsNullOrWhiteSpace(VtPermalink);

        /// <summary>Emotka statusu VT do wyświetlenia na karcie.</summary>
        public string VtStatusEmoji => VtScanStatus?.ToLowerInvariant() switch
        {
            "clean" => "\u2705",       // ✅
            "suspicious" => "\u26a0\ufe0f", // ⚠️
            "malicious" => "\u274c",   // ❌
            _ => "\u2754"              // ❔
        };

        /// <summary>Tooltip z wyjaśnieniem statusu VT – używa lokalizacji PL/EN.</summary>
        public string VtTooltip
        {
            get
            {
                var l = Loc;
                if (l == null)
                    return "VirusTotal: no data";

                if (!HasVtReport)
                    return l.Get("SecurityScan.Tooltip.NoData");

                var stats = ParseVtStats();
                var totalEngines = stats.TotalEngines;
                var detected = Math.Max(stats.Malicious, stats.Suspicious);

                string explanation;

                if (IsVtClean)
                {
                    explanation = totalEngines > 0
                        ? l.GetFormatted("SecurityScan.Tooltip.Clean", totalEngines)
                        : l.Get("SecurityScan.Tooltip.CleanNoCount");
                }
                else if (detected <= 2 && totalEngines > 50)
                {
                    explanation = l.GetFormatted("SecurityScan.Tooltip.LowDetection", detected, totalEngines);
                }
                else if (detected <= 5)
                {
                    explanation = l.GetFormatted("SecurityScan.Tooltip.ModerateDetection", detected, totalEngines);
                }
                else
                {
                    explanation = l.GetFormatted("SecurityScan.Tooltip.HighDetection", detected, totalEngines);
                }

                // Dodaj AI review jeśli dostępne
                var aiNote = VtAiReviewStatus switch
                {
                    "ai_review_false_positive_likely" => l.Get("SecurityScan.Tooltip.AiFalsePositive"),
                    "ai_review_risk_confirmed" => l.Get("SecurityScan.Tooltip.AiRiskConfirmed"),
                    "ai_review_inconclusive" => l.Get("SecurityScan.Tooltip.AiInconclusive"),
                    "ai_review_pending" => l.Get("SecurityScan.Tooltip.AiPending"),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(aiNote))
                    explanation += $"\n{aiNote}";

                // Data sprawdzenia
                if (!string.IsNullOrWhiteSpace(VtLastCheckedAt))
                {
                    explanation += $"\n{l.Get("SecurityScan.LastChecked")}: {VtLastCheckedAt}";
                }

                // Link
                if (HasVtPermalink)
                {
                    explanation += $"\n{l.Get("SecurityScan.Tooltip.ClickToOpen")}";
                }

                return explanation;
            }
        }

        /// <summary>Parsuje VtStats JSON na liczby.</summary>
        private (int Malicious, int Suspicious, int Undetected, int Harmless, int Timeout, int TotalEngines) ParseVtStats()
        {
            if (string.IsNullOrWhiteSpace(VtStats))
                return (0, 0, 0, 0, 0, 0);

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(VtStats);
                var root = doc.RootElement;

                int malicious = TryGetInt(root, "malicious");
                int suspicious = TryGetInt(root, "suspicious");
                int undetected = TryGetInt(root, "undetected");
                int harmless = TryGetInt(root, "harmless");
                int timeout = TryGetInt(root, "timeout");
                int total = malicious + suspicious + undetected + harmless + timeout;

                return (malicious, suspicious, undetected, harmless, timeout, total);
            }
            catch
            {
                return (0, 0, 0, 0, 0, 0);
            }
        }

        private static int TryGetInt(System.Text.Json.JsonElement element, string property)
        {
            if (element.TryGetProperty(property, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Number)
                return prop.GetInt32();
            return 0;
        }
    }
}

using System;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    public class ModItem : ReactiveObject
    {
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
    }
}

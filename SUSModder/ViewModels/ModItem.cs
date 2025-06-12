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

        // Nowe właściwości dla instalacji
        private bool _isInstalling = false;
        private int _installProgress = 0;
        private string _installStatusMessage = string.Empty;
        private bool _showProgress = false;

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
            set => this.RaiseAndSetIfChanged(ref _modVersion, value);
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
            }
        }

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
            set => this.RaiseAndSetIfChanged(ref _modType, value);
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

        // Nowe właściwości dla instalacji
        public bool IsInstalling
        {
            get => _isInstalling;
            set
            {
                this.RaiseAndSetIfChanged(ref _isInstalling, value);
                this.RaisePropertyChanged(nameof(CanInstall));
                this.RaisePropertyChanged(nameof(CanUninstall));
            }
        }

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
        public bool IsVanilla => ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);

        // Nowe właściwości dla kontroli przycisków
        public bool CanInstall => !IsInstalled && !IsInstalling;
        public bool CanUninstall => IsInstalled && !IsInstalling;
    }
}

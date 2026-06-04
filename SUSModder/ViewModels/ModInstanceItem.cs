using System;
using ReactiveUI;
using SUSModder.Core.Models;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Prezentacja lokalnej instancji modpacka w UI (Moje zestawy).
    /// </summary>
    public class ModInstanceItem : ReactiveObject
    {
        private bool _isBusy;
        private int _progress;
        private string _statusMessage = string.Empty;
        private bool _hasUpdateAvailable;
        private bool _isCheckedForBulk;

        public ModInstanceItem(ModInstance instance, string? pngFileName, int dllCount, bool hasTouConfig)
        {
            Instance = instance;
            PngFileName = pngFileName ?? string.Empty;
            DllCount = dllCount;
            HasTouConfig = hasTouConfig;
        }

        public ModInstance Instance { get; }

        public string InstanceId => Instance.InstanceId;
        public string DisplayName => Instance.DisplayName;
        public string BaseModName => Instance.BaseModName;
        public string FullModVersion => Instance.FullModVersion;
        public string AmongVersion => Instance.AmongVersion;
        public string InstallPath => Instance.InstallPath;
        public string Origin => Instance.Origin;
        public int BaseModId => Instance.BaseModId;
        public string PngFileName { get; }
        public int DllCount { get; }
        public bool HasTouConfig { get; }

        public string Subtitle =>
            string.IsNullOrWhiteSpace(FullModVersion)
                ? BaseModName
                : $"{BaseModName} · v{FullModVersion}";

        public string ContentsSummary =>
            HasTouConfig
                ? $"{DllCount} DLL · config ToU"
                : $"{DllCount} DLL";

        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public int Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool HasUpdateAvailable
        {
            get => _hasUpdateAvailable;
            set
            {
                this.RaiseAndSetIfChanged(ref _hasUpdateAvailable, value);
                this.RaisePropertyChanged(nameof(ShowStatusUpdate));
                this.RaisePropertyChanged(nameof(ShowStatusInstalled));
            }
        }

        public bool ShowStatusBusy => IsBusy;
        public bool ShowStatusInstalled => !IsBusy && !HasUpdateAvailable;
        public bool ShowStatusNotInstalled => false;
        public bool ShowStatusUpdate => HasUpdateAvailable && !IsBusy;
        public bool IsInstalled => true;

        public bool IsBulkEligible => !IsBusy;

        public bool IsCheckedForBulk
        {
            get => _isCheckedForBulk;
            set => this.RaiseAndSetIfChanged(ref _isCheckedForBulk, value);
        }
    }
}

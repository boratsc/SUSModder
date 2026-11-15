using System;
using System.Globalization;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Prezentacja lokalnej instancji modpacka w UI (Moje zestawy).
    /// </summary>
    public class ModInstanceItem : ReactiveObject
    {
        // ── Statyczny dostęp do lokalizacji (jak w ModItem) ──
        private static ILocalizationService? _loc;

        public static void InitializeLocalization(ILocalizationService localizationService)
        {
            _loc = localizationService;
        }

        private static ILocalizationService? Loc => _loc;

        private bool _isBusy;
        private int _progress;
        private string _statusMessage = string.Empty;
        private bool _hasUpdateAvailable;
        private bool _isCheckedForBulk;
        private ModPackUpdateInfo? _packUpdateInfo;
        private string? _versionedIconKey;

        public string? VersionedIconKey
        {
            get => _versionedIconKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _versionedIconKey, value);
                this.RaisePropertyChanged(nameof(IconSourceKey));
            }
        }

        public string IconSourceKey => VersionedIconKey is { } key ? "susmodder-icon:" + key : PngFileName;

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

        /// <summary>E4: alias dla AutomationProperties.Name wiersza listy (styl catalog-list).</summary>
        public string Name => DisplayName;

        /// <summary>
        /// E4: licznik zawartości „1 mod · N DLL" — instancja to zawsze 1 mod FULL + DLL-e.
        /// Presentation-only (wiersz listy, chip i tabela tech szczegółów).
        /// </summary>
        public string PackContentsCount =>
            Loc?.GetFormatted("UI.Packs.ContentsCountFormat", DllCount) ?? $"1 mod · {DllCount} DLL";

        /// <summary>E4: podtytuł wiersza zestawu — licznik zawartości + krótki opis (mod bazowy · wersja).</summary>
        public string PackRowSubtitle =>
            string.IsNullOrWhiteSpace(Subtitle) ? PackContentsCount : $"{PackContentsCount} · {Subtitle}";

        /// <summary>E4: badge wiersza — „Zapisany" albo stan aktualizacji (istniejący stan).</summary>
        public string PackStatusBadge =>
            HasUpdateAvailable
                ? Loc?.Get("UI.ModStatus.UpdateAvailable") ?? "Dostępna aktualizacja"
                : Loc?.Get("UI.Packs.SavedBadge") ?? "Zapisany";

        /// <summary>E4: licznik pozycji rozwijanej „Zawartość zestawu" (DLL-e + config ToU).</summary>
        public int PackContentsItemCount => DllCount + (HasTouConfig ? 1 : 0);

        /// <summary>E4: data ostatniej aktualizacji do tabeli tech (dd.MM.yyyy).</summary>
        public string LastUpdatedText =>
            DateTime.TryParse(Instance.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)
                ? updated.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                : "—";

        /// <summary>E4: podsumowanie zgodności zestawu z istniejących stanów (bez nowej logiki Core).</summary>
        public bool PackCompatIsReady => !IsBusy && !HasUpdateAvailable;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                this.RaiseAndSetIfChanged(ref _isBusy, value);
                this.RaisePropertyChanged(nameof(ShowStatusBusy));
                this.RaisePropertyChanged(nameof(ShowStatusInstalled));
                this.RaisePropertyChanged(nameof(ShowStatusUpdate));
                this.RaisePropertyChanged(nameof(PackCompatIsReady));
            }
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
                this.RaisePropertyChanged(nameof(PackStatusBadge));
                this.RaisePropertyChanged(nameof(PackCompatIsReady));
            }
        }

        private string _packUpdateChangesText = string.Empty;

        /// <summary>
        /// Szczegóły aktualizacji paczki (jeśli HasUpdateAvailable = true).
        /// </summary>
        public ModPackUpdateInfo? PackUpdateInfo
        {
            get => _packUpdateInfo;
            set
            {
                this.RaiseAndSetIfChanged(ref _packUpdateInfo, value);
                this.RaisePropertyChanged(nameof(PackUpdateChangesText));
            }
        }

        /// <summary>
        /// Sformatowana lista zmian z ostatniego sprawdzenia aktualizacji paczki (do UI).
        /// </summary>
        public string PackUpdateChangesText
        {
            get => _packUpdateChangesText;
            set => this.RaiseAndSetIfChanged(ref _packUpdateChangesText, value);
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

using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System;

namespace SUSModder.ViewModels
{
    public class DiscordServerViewModel : ViewModelBase, IDisposable
    {
        // ── Statyczny dostęp do lokalizacji (jak w ModItem/ModInstanceItem) ──
        private static ILocalizationService? _loc;

        public static void InitializeLocalization(ILocalizationService localizationService)
        {
            _loc = localizationService;
        }

        private static readonly HttpClient _httpClient = new HttpClient();
        private Bitmap? _iconBitmap;
        private bool _isIconLoading = false;
        private readonly DiscordServer _model;
        private int _memberCount;
        private bool _disposed;

        public DiscordServerViewModel(DiscordServer model)
        {
            _model = model;
            _memberCount = model.MemberCount;
        }

        // Właściwości z modelu
        public long Id => _model.Id;
        public string Name => _model.Name;
        public string InviteLink => _model.InviteLink;
        public string Description => _model.Description;
        public string? IconPath => _model.IconPath;
        public int MemberCount
        {
            get => _memberCount;
            set
            {
                this.RaiseAndSetIfChanged(ref _memberCount, value);
                this.RaisePropertyChanged(nameof(MemberCountDisplay));
            }
        }

        public string MemberCountDisplay => MemberCount.ToString("N0");

        // ── F3b: paczka zestawu przypisana do serwera (stary backend = brak pól = brak akcji) ──
        private bool _hasPackInstance;
        private bool _isPackBusy;

        public string? PackCode => _model.PackCode;
        public string? PackModName => _model.PackModName;
        public string? FullModVersion => _model.FullModVersion;
        public bool Installable => _model.Installable;
        public string? VtStatus => _model.VtStatus;

        public bool HasPack => !string.IsNullOrWhiteSpace(PackCode);

        /// <summary>Czy lokalna instancja z SourcePackCode == PackCode już istnieje (odświeżane z repozytorium).</summary>
        public bool HasPackInstance
        {
            get => _hasPackInstance;
            set
            {
                this.RaiseAndSetIfChanged(ref _hasPackInstance, value);
                this.RaisePropertyChanged(nameof(ShowInstallAction));
                this.RaisePropertyChanged(nameof(ShowUpdateAction));
            }
        }

        /// <summary>Trwa instalacja/aktualizacja zestawu z tego wiersza — blokuje ponowne kliknięcie.</summary>
        public bool IsPackBusy
        {
            get => _isPackBusy;
            set
            {
                this.RaiseAndSetIfChanged(ref _isPackBusy, value);
                this.RaisePropertyChanged(nameof(ShowInstallAction));
                this.RaisePropertyChanged(nameof(ShowUpdateAction));
                this.RaisePropertyChanged(nameof(ShowPackUpdateBadge));
            }
        }

        public bool ShowInstallAction => HasPack && Installable && !HasPackInstance && !IsPackBusy;
        public bool ShowUpdateAction => HasPack && Installable && HasPackInstance && !IsPackBusy;
        public bool ShowPackUnavailable => HasPack && !Installable;

        // ── F3g: badge „Dostępna aktualizacja" + lista zmian w Szczegółach (stan z checku paczek) ──
        private bool _hasPackUpdate;
        private string _packUpdateChangesText = string.Empty;

        /// <summary>Czy zestaw tego serwera ma dostępną aktualizację paczki (z ModInstanceItem).</summary>
        public bool HasPackUpdate
        {
            get => _hasPackUpdate;
            set
            {
                this.RaiseAndSetIfChanged(ref _hasPackUpdate, value);
                this.RaisePropertyChanged(nameof(ShowPackUpdateBadge));
            }
        }

        /// <summary>Sformatowana lista zmian paczki (bullets) — pusta, gdy brak aktualizacji.</summary>
        public string PackUpdateChangesText
        {
            get => _packUpdateChangesText;
            set
            {
                this.RaiseAndSetIfChanged(ref _packUpdateChangesText, value);
                this.RaisePropertyChanged(nameof(HasPackUpdateChanges));
            }
        }

        public bool HasPackUpdateChanges => !string.IsNullOrWhiteSpace(PackUpdateChangesText);

        /// <summary>Badge widoczny tylko gdy aktualizacja jest realna i możliwa do wykonania.</summary>
        public bool ShowPackUpdateBadge => HasPackUpdate && !ShowPackUnavailable && !IsPackBusy;

        /// <summary>Nazwa + wersja zestawu (dane z backendu; wzorzec ModInstanceItem.Subtitle).</summary>
        public string PackSummary
        {
            get
            {
                if (!HasPack)
                    return string.Empty;
                var name = string.IsNullOrWhiteSpace(PackModName) ? PackCode! : PackModName!;
                return string.IsNullOrWhiteSpace(FullModVersion) ? name : $"{name} · v{FullModVersion}";
            }
        }

        /// <summary>I1: status skanowania przez te same klucze co lista udostępnionych paczek (bez surowych „suspicious"/„pending").</summary>
        public string VtStatusDisplay => string.IsNullOrWhiteSpace(VtStatus)
            ? "—"
            : _loc?.Get(SharedModPackItem.ResolveVtStatusKey(VtStatus)) ?? VtStatus!;

        /// <summary>F3f: tooltip pod kursorem na „Zainstaluj/Zaktualizuj zestaw" — czyj zestaw modów zostanie zainstalowany.</summary>
        public string PackActionTooltip => _loc?.GetFormatted("RecommendedDiscords.PackActionTooltip", Name) ?? Name;

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
        public bool HasDetails => HasPack || HasDescription;

        /// <summary>Odświeża teksty zależne od języka (wywoływane przez panel przy zmianie kultury).</summary>
        public void RefreshLocalization()
        {
            this.RaisePropertyChanged(nameof(VtStatusDisplay));
            this.RaisePropertyChanged(nameof(PackActionTooltip));
        }

        /// <summary>
        /// Wykrywanie „zestaw z tego serwera już istnieje": porównanie SourcePackCode z PackCode
        /// (OrdinalIgnoreCase). Wśród dopasowań preferuje Origin == shared_pack — klon kopiuje
        /// SourcePackCode, ale checker aktualizacji flaguje tylko shared_pack (W4). Czysta funkcja.
        /// </summary>
        public static ModInstance? FindPackInstance(IEnumerable<ModInstance> instances, string? packCode)
        {
            if (string.IsNullOrWhiteSpace(packCode))
                return null;
            var matches = instances
                .Where(i => string.Equals(i.SourcePackCode, packCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return matches.FirstOrDefault(i => i.Origin == ModInstanceOrigins.SharedPack)
                   ?? matches.FirstOrDefault();
        }

        /// <summary>
        /// Odświeża stan instancji dla wszystkich wierszy na podstawie danych z repozytorium.
        /// F3g: packItems (stan z checku aktualizacji) dokłada badge „Dostępna aktualizacja" i listę zmian.
        /// </summary>
        public static void ApplyPackInstances(
            IEnumerable<DiscordServerViewModel> servers,
            IEnumerable<ModInstance> instances,
            IEnumerable<ModInstanceItem>? packItems = null)
        {
            var list = instances.ToList();
            var items = packItems?.ToList();
            foreach (var server in servers)
            {
                var instance = FindPackInstance(list, server.PackCode);
                server.HasPackInstance = instance != null;
                var item = instance != null
                    ? items?.FirstOrDefault(p => p.InstanceId == instance.InstanceId)
                    : null;
                // W2: badge z manifestu paczki, nie ze sticky flagi katalogowej (aktualizacja-widmo).
                server.HasPackUpdate = item?.HasManifestPackUpdate == true;
                server.PackUpdateChangesText = item?.PackUpdateChangesText ?? string.Empty;
            }
        }

        // Właściwości UI
        public Bitmap? IconBitmap
        {
            get => _iconBitmap;
            private set => this.RaiseAndSetIfChanged(ref _iconBitmap, value);
        }

        public bool HasIcon => IconBitmap != null;
        public bool IsIconLoading
        {
            get => _isIconLoading;
            private set => this.RaiseAndSetIfChanged(ref _isIconLoading, value);
        }

        public async Task LoadIconAsync()
        {
            if (string.IsNullOrEmpty(IconPath) || _iconBitmap != null || _isIconLoading)
                return;

            IsIconLoading = true;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Loading icon from: {IconPath}");

                using var response = await _httpClient.GetAsync(IconPath);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    IconBitmap = new Bitmap(memoryStream);
                    this.RaisePropertyChanged(nameof(HasIcon));
                    System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Successfully loaded icon for: {Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Failed to load icon for {Name}: HTTP {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordServerVM] Error loading icon for {Name}: {ex.Message}");
            }
            finally
            {
                IsIconLoading = false;
            }
        }

        /// <summary>
        /// Zwalnia bitmapę ikony. Wywoływane przy czyszczeniu cache lub przy Dispose ViewModelu.
        /// Bezpieczne do wielokrotnego wywołania.
        /// </summary>
        public void DisposeIconBitmap()
        {
            if (_iconBitmap != null)
            {
                var oldBitmap = _iconBitmap;
                _iconBitmap = null;
                oldBitmap.Dispose();
                this.RaisePropertyChanged(nameof(IconBitmap));
                this.RaisePropertyChanged(nameof(HasIcon));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeIconBitmap();
        }
    }
}

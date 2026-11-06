using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Storage;
using SUSModder.Core.Utilities;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class MainWindowViewModel — podsumowanie miejsca (3.1-08) i rozmiar
    /// katalogu na kafelku moda (3.1-09).
    ///
    /// Zasady:
    /// - UI nigdy nie kasuje plików (3.1 jest tylko-pomiarowe);
    /// - pomiary idą przez współdzielony <see cref="IStorageSizeMeasurementService"/>
    ///   (cache, deduplikacja, bounded concurrency, cancellation) — brak własnej rekursji;
    /// - etykiety kafelków unieważniane zdarzeniami kolekcji/właściwości, nie pollingiem;
    /// - wynik starszego przebiegu ani inna ścieżka nie nadpiszą świeżego (sprawdzenie
    ///   generacji i bieżącego InstallPath przed każdym zapisem).
    /// </summary>
    public partial class MainWindowViewModel
    {
        private const int TileDebounceMs = 250;

        private ReactiveCommand<Unit, Unit>? _showStorageOverviewCommand;
        private IStorageSizeMeasurementService? _storageMeasurementService;
        private bool _storageInvalidatedHooked;
        private bool _storageHooksAttached;
        private bool _storageOverviewDisposed;

        private readonly HashSet<ModItem> _storageSubscribedItems = new();
        private CancellationTokenSource? _storageTilesCts;
        private long _storageTileGeneration;
        private IDisposable? _storageTileDebounce; // RunOnce zwraca IDisposable (12.x): Dispose = anuluj jednostrzał
        private bool _storageDebounceWillRescan;

        /// <summary>
        /// Fallback, gdy DI jeszcze nie rejestruje serwisu (Designer / etap startu).
        /// Statyczny, bo pomiar ma być jeden na proces — nie dorabiamy drugiej rekursji.
        /// </summary>
        private static readonly Lazy<StorageSizeMeasurementService> StorageMeasurementFallback =
            new(() => new StorageSizeMeasurementService());

        // ── Wejście do podsumowania miejsca ──

        /// <summary>Polecenie otwierające okno podsumowania miejsca (wejście z paska statusu / menu).</summary>
        public ReactiveCommand<Unit, Unit> ShowStorageOverviewCommand =>
            _showStorageOverviewCommand ??= ReactiveCommand.CreateFromTask(ShowStorageOverviewAsync);

        /// <summary>Stabilna aliasowa nazwa polecenia dla bindingów w MainWindow (główny plik decyduje, której użyć).</summary>
        public ReactiveCommand<Unit, Unit> StorageOverviewCommand => ShowStorageOverviewCommand;

        /// <summary>
        /// Otwiera modalne okno podsumowania i inicjuje podsystem pomiarów.
        /// Po zamknięciu z udanym pomiarem: kafelki biblioteki odświeżają etykiety z cache,
        /// a pasek statusu dostaje pełny przeliczny obieg (istniejące public API).
        /// </summary>
        public async Task ShowStorageOverviewAsync()
        {
            if (_storageOverviewDisposed) return;

            EnsureStorageOverviewHooks();

            try
            {
                Window? owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner is null)
                {
                    Debug.WriteLine("[StorageOverview] Brak okna właściciela — pomijam otwarcie podsumowania.");
                    return;
                }

                using var vm = new StorageOverviewViewModel(
                    _localizationService,
                    new StorageOverviewBackend(GetStorageMeasurementService()),
                    BuildStorageOverviewScope);

                var dialog = new StorageOverviewDialog(vm);
                _ = vm.MeasureAsync(); // błędy powierzchniowo obsługuje VM (stabilne stany), nie polecenie

                await dialog.ShowDialog(owner);

                if (vm.HasFreshMeasurement)
                {
                    await RefreshLibraryStorageAsync();
                    await RefreshStatusBarAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StorageOverview] Otwarcie podsumowania nie powiodło się: {ex.Message}");
            }
        }

        /// <summary>
        /// Opcjonalny haczyk inicjalizacji z konstruktora (jeśli główny plik go podepnie);
        /// jest idempotentny — właściwości i polecenia inicjują się też leniwie same.
        /// </summary>
        public void InitializeStorageOverview()
        {
            EnsureStorageOverviewHooks();
            RescheduleTileRefresh(rescan: true);
        }

        // ── 3.1-09: etykiety rozmiaru na kafelkach ──

        /// <summary>
        /// Odświeża etykiety rozmiaru katalogu na kafelkach modsów zainstalowanych
        /// (snapshot katalogu + bieżąca widoczna lista). Pomiar asynchroniczny, bounded
        /// w serwisie Core; późne wyniki starych przebiegów są odrzucane.
        /// Wywoływać z wątku UI (np. po CaptureCatalogModsSnapshot / odświeżeniu listy).
        /// </summary>
        public async Task RefreshLibraryStorageAsync(CancellationToken ct = default)
        {
            if (_storageOverviewDisposed) return;

            EnsureStorageOverviewHooks();
            SyncStorageItemSubscriptions();

            var service = GetStorageMeasurementService();

            var previousCts = Interlocked.Exchange(ref _storageTilesCts, CancellationTokenSource.CreateLinkedTokenSource(ct));
            try { previousCts?.Cancel(); } catch (ObjectDisposedException) { }

            var runCts = _storageTilesCts!;
            var token = runCts.Token;
            var generation = Interlocked.Increment(ref _storageTileGeneration);

            var targets = CollectStorageTileTargets();

            // 1) Natychmiastowa prezentacja: świeża/cached wartość albo „Obliczanie…” — bez skanu.
            foreach (var (item, path) in targets)
            {
                if (Interlocked.Read(ref _storageTileGeneration) != generation) return;
                if (!string.Equals(item.InstallPath, path, StringComparison.OrdinalIgnoreCase)) continue;
                item.DirectorySizeText = StorageTileSizePresenter.BuildText(_localizationService, service.GetDisplaySnapshot(path));
            }

            // 2) Kafelki poza zakresem (np. odinstalowane w trakcie) — czyste etykiety.
            var targetItems = new HashSet<ModItem>(targets.Select(t => t.Item));
            foreach (var mod in Mods.Where(m => m.HasDirectorySize && !targetItems.Contains(m)))
                mod.DirectorySizeText = string.Empty;

            // 3) Pomiary — współbieżność limituje sam serwis (deduplikacja lotów per ścieżka).
            var measureTasks = targets
                .Select(t => ObserveTileAsync(t.Item, t.Path, generation, token))
                .ToArray();

            await Task.WhenAll(measureTasks);
        }

        /// <summary>
        /// Unieważnia cache pomiarów biblioteki (po operacji zmieniającej pliki poza tym widokiem)
        /// i aktualizuje etykiety kafelków na stan „nieaktualne”. Bez skanu w tle.
        /// </summary>
        public void InvalidateLibraryStorage()
        {
            if (_storageOverviewDisposed) return;
            // Includes installed instances outside the default library and containing aggregates.
            GetStorageMeasurementService().InvalidateAll();

            RescheduleTileRefresh(rescan: false);
        }

        private void NotifyStorageFilesChanged()
        {
            if (_storageOverviewDisposed) return;
            InvalidateLibraryStorage();
            RescheduleTileRefresh(rescan: true);
        }

        /// <summary>
        /// Odwiesza wszystko, co podsystem miejsca podpiął do VM (kolekcja Mods,
        /// właściwości pozycji, lokalizacja, zdarzenie unieważnienia serwisu),
        /// anuluje oczekujące zadania/timer kafelków i rozprasza polecenie.
        /// Wywoływane z <c>MainWindowViewModel.Dispose</c> (integracja main).
        /// Współdzielonego serwisu pomiaru NIE dysponujemy — to singleton DI /
        /// fallback procesowy, nie nasza własność.
        /// </summary>
        public void DisposeStorageOverview()
        {
            if (_storageOverviewDisposed) return;
            _storageOverviewDisposed = true;

            if (_storageHooksAttached)
            {
                Mods.CollectionChanged -= OnStorageModsCollectionChanged;
                if (_localizationService is INotifyPropertyChanged localizationNotify)
                    localizationNotify.PropertyChanged -= OnStorageCultureChanged;
                _storageHooksAttached = false;
            }

            foreach (var item in _storageSubscribedItems)
                item.PropertyChanged -= OnStorageModItemChanged;
            _storageSubscribedItems.Clear();

            if (_storageInvalidatedHooked && _storageMeasurementService is StorageSizeMeasurementService concrete)
            {
                concrete.Invalidated -= OnStorageMeasurementsInvalidated;
                _storageInvalidatedHooked = false;
            }

            _storageTileDebounce?.Dispose(); // jednostrzał RunOnce: Dispose anuluje oczekujący tick
            _storageTileDebounce = null;
            _storageDebounceWillRescan = false;

            // Strażnik pokolenia — ewentualne późne wyniki przebiegów nie mają gdzie pisać.
            Interlocked.Increment(ref _storageTileGeneration);

            // CTS trzymany przez oczekujące MeasureAsync: tylko Cancel, bez Dispose
            // (rejestracje callbacka mogą jeszcze trwać; GC zwolni nietimerowany CTS).
            var tilesCts = Interlocked.Exchange(ref _storageTilesCts, null);
            try { tilesCts?.Cancel(); } catch (ObjectDisposedException) { }

            _showStorageOverviewCommand?.Dispose();
            _showStorageOverviewCommand = null;

            _storageMeasurementService = null;
        }

        private async Task ObserveTileAsync(ModItem item, string path, long generation, CancellationToken token)
        {
            StorageSizeResult result;
            try
            {
                result = await GetStorageMeasurementService().MeasureAsync(path, StorageMeasurementOptions.Default, token);
            }
            catch (OperationCanceledException)
            {
                RestoreTileFromCache(item, path, generation);
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StorageOverview] Pomiar '{path}' nieudany: {ex.Message}");
                return;
            }

            WriteTileText(item, path, generation, result);
        }

        private void WriteTileText(ModItem item, string path, long generation, StorageSizeResult result)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => WriteTileText(item, path, generation, result));
                return;
            }
            if (_storageOverviewDisposed) return;
            // Kontrola „ścieżka/pokolenie”: późny wynik nigdy nie nadpisuje świeższego stanu.
            if (Interlocked.Read(ref _storageTileGeneration) != generation) return;
            if (!string.Equals(item.InstallPath, path, StringComparison.OrdinalIgnoreCase)) return;
            if (!IsStorageTileCandidate(item)) return;
            if (result.Revision != GetStorageMeasurementService().GetRevision(path)) return;

            item.DirectorySizeText = StorageTileSizePresenter.BuildText(_localizationService, result);
        }

        private void RestoreTileFromCache(ModItem item, string path, long generation)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RestoreTileFromCache(item, path, generation));
                return;
            }
            if (_storageOverviewDisposed) return;
            if (Interlocked.Read(ref _storageTileGeneration) != generation) return;
            if (!string.Equals(item.InstallPath, path, StringComparison.OrdinalIgnoreCase)) return;

            var service = GetStorageMeasurementService();
            var snapshot = service.GetSnapshot(path);
            item.DirectorySizeText = snapshot is null
                ? string.Empty
                : StorageTileSizePresenter.BuildText(_localizationService, service.GetDisplaySnapshot(path));
        }

        private List<(ModItem Item, string Path)> CollectStorageTileTargets()
        {
            var result = new List<(ModItem, string)>();
            var seenIds = new HashSet<int>();

            foreach (var mod in Mods.Concat(_catalogModsSnapshot))
            {
                if (!seenIds.Add(mod.Id)) continue;
                if (!IsStorageTileCandidate(mod)) continue;

                var path = mod.InstallPath!;
                if (File.Exists(path)) continue; // ścieżka pliku (dll-like) — nie jest katalogiem moda

                result.Add((mod, path));
            }

            return result;
        }

        /// <summary>
        /// Zakres kafelka (3.1-09): pełne mody z faktycznym katalogiem instalacji.
        /// DLL i vanilla celowo poza podstawowym zakresem — ich mapowanie folderów
        /// wymaga osobnego potwierdzenia semantyki (rozszerzenie poza ModCard).
        /// </summary>
        private static bool IsStorageTileCandidate(ModItem mod) =>
            mod.IsFullMod && mod.IsInstalled && !string.IsNullOrWhiteSpace(mod.InstallPath);

        // ── Zakres dla okna podsumowania ──

        private StorageOverviewScope BuildStorageOverviewScope()
        {
            var root = PathSettings.ModsInstallPath ?? string.Empty;

            bool exists = false;
            try { exists = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root); }
            catch { exists = false; }

            var known = new List<string>();

            foreach (var mod in _catalogModsSnapshot.Concat(Mods))
            {
                if (IsStorageTileCandidate(mod) && !File.Exists(mod.InstallPath!))
                    known.Add(mod.InstallPath!);
            }

            foreach (var instance in PackInstances)
            {
                if (!string.IsNullOrWhiteSpace(instance.InstallPath) && !File.Exists(instance.InstallPath))
                    known.Add(instance.InstallPath);
            }

            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in known)
            {
                var normalized = StoragePathNormalizer.TryNormalize(raw);
                if (!string.IsNullOrEmpty(normalized) && seen.Add(normalized!))
                    distinct.Add(normalized!);
            }

            return new StorageOverviewScope(root, distinct, exists);
        }

        // ── Serwis pomiaru i haczyki unieważniania ──

        private IStorageSizeMeasurementService GetStorageMeasurementService()
        {
            if (_storageMeasurementService is not null) return _storageMeasurementService;

            IStorageSizeMeasurementService? resolved = null;
            try { resolved = App.GetService<IStorageSizeMeasurementService>(); }
            catch (InvalidOperationException) { /* DI niezarejestrowane (start/Designer) — fallback współdzielony */ }

            _storageMeasurementService = resolved ?? StorageMeasurementFallback.Value;
            EnsureStorageInvalidationHook(_storageMeasurementService);
            return _storageMeasurementService;
        }

        private void EnsureStorageInvalidationHook(IStorageSizeMeasurementService service)
        {
            if (_storageInvalidatedHooked) return;
            if (service is StorageSizeMeasurementService concrete)
            {
                concrete.Invalidated += OnStorageMeasurementsInvalidated;
                _storageInvalidatedHooked = true;
            }
        }

        private void OnStorageMeasurementsInvalidated(object? sender, StorageInvalidatedEventArgs e)
        {
            // Zdarzenie z Core: odśwież tylko prezentację z cache (oznaczenie „nieaktualne”),
            // nie uruchamiaj nowego skanu — skan ruszy przy jawnym odświeżeniu.
            RescheduleTileRefresh(rescan: false);
        }

        private void EnsureStorageOverviewHooks()
        {
            if (_storageHooksAttached || _storageOverviewDisposed) return;
            _storageHooksAttached = true;

            Mods.CollectionChanged += OnStorageModsCollectionChanged;

            if (_localizationService is INotifyPropertyChanged localizationNotify)
                localizationNotify.PropertyChanged += OnStorageCultureChanged;
        }

        private void OnStorageModsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SyncStorageItemSubscriptions();
            RescheduleTileRefresh(rescan: true);
        }

        private void OnStorageModItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ModItem.InstallPath) or nameof(ModItem.ModVersion) ||
                (e.PropertyName == nameof(ModItem.IsInstalling) && sender is ModItem { IsInstalling: false }))
                NotifyStorageFilesChanged();
        }

        private void OnStorageCultureChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SUSModder.Core.Services.Localization.ILocalizationService.CurrentCulture))
                RescheduleTileRefresh(rescan: false);
        }

        private void SyncStorageItemSubscriptions()
        {
            var current = Mods.Concat(_catalogModsSnapshot).ToHashSet();
            foreach (var item in current)
            {
                if (_storageSubscribedItems.Add(item))
                    item.PropertyChanged += OnStorageModItemChanged;
            }

            // Pozbądź się subskrypcji itemsów, które zniknęły z bieżącej listy (przebudowa katalogu).
            List<ModItem>? stale = null;
            foreach (var item in _storageSubscribedItems)
            {
                if (!current.Contains(item))
                    (stale ??= new List<ModItem>()).Add(item);
            }

            if (stale is null) return;
            foreach (var item in stale)
            {
                item.PropertyChanged -= OnStorageModItemChanged;
                _storageSubscribedItems.Remove(item);
            }
        }

        // ── Debounce (event-driven, bez globalnego pollingu) ──

        /// <summary>
        /// Łączy serie zdarzeń kolekcji/właściwości w jedno odświeżenie.
        /// <c>rescan: true</c> uruchamia pomiary, <c>false</c> tylko przepisanie etykiet z cache.
        /// </summary>
        private void RescheduleTileRefresh(bool rescan)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RescheduleTileRefresh(rescan));
                return;
            }
            if (_storageOverviewDisposed) return;

            _storageTileDebounce?.Dispose(); // anuluj poprzedni jednostrzał

            _storageDebounceWillRescan = _storageDebounceWillRescan || rescan;

            _storageTileDebounce = DispatcherTimer.RunOnce(
                OnStorageTileDebounceTick,
                TimeSpan.FromMilliseconds(TileDebounceMs),
                DispatcherPriority.Background);
        }

        private void OnStorageTileDebounceTick()
        {
            bool rescan = _storageDebounceWillRescan;
            _storageDebounceWillRescan = false;
            _storageTileDebounce = null;

            if (_storageOverviewDisposed) return;

            try
            {
                if (rescan)
                    _ = RefreshLibraryStorageAsync();
                else
                    RenderAllTilesFromCache();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StorageOverview] Odświeżanie kafelków nieudane: {ex.Message}");
            }
        }

        /// <summary>Przepisuje etykiety kafelków wyłącznie z cache pomiarów (bez nowych skanów).</summary>
        private void RenderAllTilesFromCache()
        {
            try
            {
                var service = GetStorageMeasurementService();
                var seenIds = new HashSet<int>();

                foreach (var mod in Mods.Concat(_catalogModsSnapshot))
                {
                    if (!seenIds.Add(mod.Id)) continue;

                    if (!IsStorageTileCandidate(mod) || File.Exists(mod.InstallPath))
                    {
                        if (mod.HasDirectorySize)
                            mod.DirectorySizeText = string.Empty;
                        continue;
                    }

                    var path = mod.InstallPath!;
                    var cached = service.GetSnapshot(path);
                    // Brak cache → brak etykiety, nie wymuszamy „Obliczanie…” bez zleconego skanu.
                    mod.DirectorySizeText = cached is null
                        ? string.Empty
                        : StorageTileSizePresenter.BuildText(_localizationService, service.GetDisplaySnapshot(path));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StorageOverview] Render z cache: {ex.Message}");
            }
        }
    }
}

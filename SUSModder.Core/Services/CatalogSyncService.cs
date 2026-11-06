using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

public sealed class CatalogSyncService
{
    public const string CatalogStateKey = "catalog.config";
    public const string CompatibilityStateKey = "compatibility.snapshot";

    /// <summary>
    /// Klucz stanu synchronizacji metadanych ikon (3.1-04). Osobny ETag, bo zmiana
    /// globalnej ikony nie musi zmieniać ETagu katalogu.
    /// </summary>
    public const string IconMetaStateKey = "catalog.icons.meta";

    /// <summary>Wersja schematu kontraktu ikon obsługiwana przez klienta (3.1-04).</summary>
    public const int IconSchemaVersion = 1;

    private static readonly TimeSpan CatalogMinInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CompatibilityMinInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RemoteConfigMemoryTtl = TimeSpan.FromMinutes(2);
    private static readonly MemoryCache RemoteConfigCache = new(new MemoryCacheOptions());

    private readonly ISUSModderApiClient _apiClient;
    private readonly IModRepository _modRepository;
    private readonly ICatalogSyncStateRepository _syncState;
    private readonly ICompatibilityCacheRepository _compatibilityCache;
    private readonly IIconAssignmentRepository? _iconAssignments;
    private readonly IDiagnosticsOutput _log;
    private readonly SemaphoreSlim _catalogFlight = new(1, 1);
    private readonly SemaphoreSlim _compatibilityFlight = new(1, 1);

    /// <summary>
    /// Zgłasza zmiany przypisań ikon po synchronizacji katalogu (3.1-04).
    /// Wywoływane na wątku synchronizacji — UI przechodzi na wątek interfejsu.
    /// </summary>
    public event EventHandler<IconAssignmentsChangedEventArgs>? IconAssignmentsChanged;

    public CatalogSyncService(
        ISUSModderApiClient apiClient,
        IModRepository modRepository,
        ICatalogSyncStateRepository syncState,
        ICompatibilityCacheRepository compatibilityCache,
        IDiagnosticsOutput log,
        IIconAssignmentRepository? iconAssignments = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _modRepository = modRepository ?? throw new ArgumentNullException(nameof(modRepository));
        _syncState = syncState ?? throw new ArgumentNullException(nameof(syncState));
        _compatibilityCache = compatibilityCache ?? throw new ArgumentNullException(nameof(compatibilityCache));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _iconAssignments = iconAssignments;
    }

    /// <summary>
    /// True, gdy ostatnia udana odpowiedź metadanych potwierdziła obsługiwany schemat
    /// kontraktu ikon. UI używa tego, aby zdecydować, czy wiązać ikonę wersjonowaną.
    /// </summary>
    public bool IsVersionedIconSchemaActive =>
        _syncState.Get(IconMetaStateKey)?.SchemaVersion == IconSchemaVersion;

    /// <summary>Migawka wszystkich zapisanych przypisań ikon (wraz z tombstonami).</summary>
    public IReadOnlyList<IconAssignment> GetIconAssignments() =>
        _iconAssignments?.GetAll() ?? Array.Empty<IconAssignment>();

    /// <summary>Przypisanie ikony o podanym stabilnym kluczu (albo null).</summary>
    public IconAssignment? GetIconAssignment(string assignmentKey) => _iconAssignments?.Get(assignmentKey);

    /// <summary>Przypisanie ikony moda (klucz <c>mod:&lt;id&gt;</c>, albo null).</summary>
    public IconAssignment? GetIconAssignmentForMod(int modId) => _iconAssignments?.GetForMod(modId);

    /// <summary>Przypisanie ikony globalnej (vanilla / dll, albo null).</summary>
    public IconAssignment? GetGlobalIconAssignment(IconGlobalRole role) => _iconAssignments?.GetGlobal(role);

    /// <summary>
    /// True, gdy zapisana rewizja ikon różni się od podanej (np. z lekkiego pingu
    /// metadanych statusu API). Pozwala odświeżyć same ikony bez czekania na
    /// pełny, 15-minutowy cykl katalogu.
    /// </summary>
    public bool IsIconMetadataStale(string? iconsRevision)
    {
        var state = _syncState.Get(IconMetaStateKey);
        if (state is null)
            return true;

        // Rewizja rośnie przy każdej zmianie roli globalnej; to najczulszy sygnał.
        if (!string.Equals(state.LastModified, iconsRevision, StringComparison.Ordinal))
            return true;

        // Potwierdzony schemat 0 przy tej samej rewizji to świadomy tryb legacy —
        // nie odpyłujemy backendu co 30 s.
        return false;
    }

    /// <summary>
    /// Odświeża wyłącznie metadane ikon (role globalne), z własnym ETagiem
    /// i bez pobierania katalogu. Przypisania modów pozostają z ostatniej
    /// kompletnej migawki katalogu.
    /// </summary>
    public async Task RefreshIconAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        if (IsInBackoff(IconMetaStateKey))
            return;

        await SyncIconAssignmentsAsync(null, catalogComplete: false, cancellationToken);
    }

    /// <summary>
    /// Odświeża przypisania ikon — globalne i per-mod — bez zapisywania katalogu
    /// do repozytorium. Ikony modów przychodzą wyłącznie w pozycjach katalogu,
    /// więc samo <c>catalog-meta</c> nie wystarcza. Żądanie katalogu jest warunkowe
    /// (ETag), a stan katalogu pozostaje nietknięty, aby normalny sync nadal
    /// wykrył zmiany wersji i odświeżył listę modów.
    /// </summary>
    public async Task RefreshIconAssignmentsFromCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (IsInBackoff(IconMetaStateKey) || IsInBackoff(CatalogStateKey))
            return;

        if (!await _catalogFlight.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            return;

        try
        {
            var state = _syncState.Get(CatalogStateKey);
            var fetch = await FetchCatalogAsync(state?.ETag, cancellationToken);

            if (fetch.NotModified)
            {
                // Katalog bez zmian: mogą zmienić się tylko role globalne.
                await SyncIconAssignmentsAsync(null, catalogComplete: false, cancellationToken);
                return;
            }

            if (fetch.Complete && fetch.Items.Count > 0)
                await SyncIconAssignmentsAsync(fetch.Items, catalogComplete: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[CatalogSync] icon catalog refresh failed: {ex.Message}");
        }
        finally
        {
            _catalogFlight.Release();
        }
    }

    public async Task<CatalogSyncResult> RefreshCatalogIfDueAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && IsInBackoff(CatalogStateKey))
        {
            return OfflineResult(CatalogStateKey, CatalogSyncStatus.SkippedBackoff);
        }

        if (!force && !IsDue(CatalogStateKey, CatalogMinInterval))
        {
            var meta = _syncState.Get(CatalogStateKey);
            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.NotModified,
                ETag = meta?.ETag,
                ModCount = _modRepository.GetAllMods().Count,
                LastSuccessUtc = meta?.LastSuccessUtc
            };
        }

        if (!await _catalogFlight.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            return new CatalogSyncResult { Status = CatalogSyncStatus.Failed, ErrorMessage = "catalog sync busy" };
        }

        try
        {
            return await RefreshCatalogCoreAsync(force, cancellationToken);
        }
        finally
        {
            _catalogFlight.Release();
        }
    }

    public async Task<CatalogSyncResult> RefreshCompatibilityIfDueAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && IsInBackoff(CompatibilityStateKey))
        {
            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.SkippedBackoff,
                ModCount = _compatibilityCache.Count()
            };
        }

        if (!force && !IsDue(CompatibilityStateKey, CompatibilityMinInterval) && _compatibilityCache.Count() > 0)
        {
            var meta = _syncState.Get(CompatibilityStateKey);
            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.NotModified,
                ETag = meta?.ETag,
                ModCount = _compatibilityCache.Count(),
                LastSuccessUtc = meta?.LastSuccessUtc
            };
        }

        if (!await _compatibilityFlight.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            return new CatalogSyncResult { Status = CatalogSyncStatus.Failed, ErrorMessage = "compatibility sync busy" };
        }

        try
        {
            return await RefreshCompatibilityCoreAsync(cancellationToken);
        }
        finally
        {
            _compatibilityFlight.Release();
        }
    }

    public async Task<List<ModConfiguration>> EnsureRemoteConfigCachedAsync(CancellationToken cancellationToken = default)
    {
        if (RemoteConfigCache.TryGetValue<List<ModConfiguration>>("remote_catalog", out var cached) && cached != null)
            return cached;

        await RefreshCatalogIfDueAsync(force: false, cancellationToken);
        var mods = _modRepository.GetAllMods();
        RemoteConfigCache.Set("remote_catalog", mods, RemoteConfigMemoryTtl);
        return mods;
    }

    public void InvalidateRemoteConfigCache() => RemoteConfigCache.Remove("remote_catalog");

    private async Task<CatalogSyncResult> RefreshCatalogCoreAsync(bool force, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var state = _syncState.Get(CatalogStateKey);
        var localCount = _modRepository.GetAllMods().Count;

        try
        {
            var fetch = await FetchCatalogAsync(state?.ETag, cancellationToken);

            if (fetch.NotModified)
            {
                _syncState.SaveSuccess(CatalogStateKey, fetch.ETag ?? state?.ETag, null, nowUtc);

                // Katalog bez zmian nie oznacza braku zmian ikon — metadane globalne
                // są sprawdzane warunkowo i przy własnym ETagu, także po 304 katalogu.
                await SyncIconAssignmentsAsync(null, false, cancellationToken);

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.NotModified,
                    ETag = fetch.ETag ?? state?.ETag,
                    ModCount = localCount,
                    LastSuccessUtc = nowUtc
                };
            }

            if (fetch.Mods.Count == 0)
            {
                _syncState.SaveFailure(CatalogStateKey, "EMPTY_RESPONSE", nowUtc, ComputeNextAttemptUtc(state));
                if (localCount > 0)
                {
                    return OfflineResult(CatalogStateKey, CatalogSyncStatus.OfflineUsingCache,
                        "Pusta odpowiedź API — użyto lokalnego katalogu.");
                }

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.InvalidResponse,
                    ErrorMessage = "Pusta odpowiedź katalogu",
                    ModCount = 0
                };
            }

            if (!ValidateCatalog(fetch.Mods, localCount, out var validationError))
            {
                _syncState.SaveFailure(CatalogStateKey, "INVALID_RESPONSE", nowUtc, ComputeNextAttemptUtc(state));
                if (localCount > 0)
                {
                    return OfflineResult(CatalogStateKey, CatalogSyncStatus.OfflineUsingCache, validationError);
                }

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.InvalidResponse,
                    ErrorMessage = validationError,
                    ModCount = localCount
                };
            }

            var changed = await _modRepository.ApplyRemoteCatalogAsync(fetch.Mods);
            _syncState.SaveSuccess(CatalogStateKey, fetch.ETag, null, nowUtc);
            InvalidateRemoteConfigCache();

            // Przypisania ikon commitujemy dopiero po pełnej, zwalidowanej migawce katalogu.
            await SyncIconAssignmentsAsync(fetch.Items, fetch.Complete, cancellationToken);

            return new CatalogSyncResult
            {
                Status = changed ? CatalogSyncStatus.Updated : CatalogSyncStatus.NotModified,
                ETag = fetch.ETag,
                ModCount = fetch.Mods.Count,
                LastSuccessUtc = nowUtc
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[CatalogSync] catalog refresh failed: {ex.Message}");
            _syncState.SaveFailure(CatalogStateKey, "EXCEPTION", nowUtc, ComputeNextAttemptUtc(state));

            if (localCount > 0)
            {
                return OfflineResult(CatalogStateKey, CatalogSyncStatus.OfflineUsingCache, ex.Message);
            }

            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.Failed,
                ErrorMessage = ex.Message,
                ModCount = localCount
            };
        }
    }

    private async Task<CatalogSyncResult> RefreshCompatibilityCoreAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var state = _syncState.Get(CompatibilityStateKey);
        var cachedCount = _compatibilityCache.Count();

        try
        {
            var response = await _apiClient.GetCompatibilitySnapshotAsync(
                onlyCurrentVersions: true,
                ifNoneMatch: state?.ETag,
                cancellationToken: cancellationToken);

            if (response.IsNotModified)
            {
                _syncState.SaveSuccess(CompatibilityStateKey, response.ETag ?? state?.ETag, null, nowUtc);
                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.NotModified,
                    ETag = response.ETag ?? state?.ETag,
                    ModCount = cachedCount,
                    LastSuccessUtc = nowUtc
                };
            }

            if (!response.IsSuccess || response.Data?.Entries is null)
            {
                _syncState.SaveFailure(CompatibilityStateKey, $"HTTP_{response.StatusCode}", nowUtc, ComputeNextAttemptUtc(state));
                if (cachedCount > 0)
                {
                    return new CatalogSyncResult
                    {
                        Status = CatalogSyncStatus.OfflineUsingCache,
                        ModCount = cachedCount,
                        LastSuccessUtc = state?.LastSuccessUtc
                    };
                }

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.Failed,
                    ErrorMessage = response.Error?.Message ?? $"HTTP {response.StatusCode}",
                    ModCount = 0
                };
            }

            var entries = response.Data.Entries
                .Select(e => new CompatibilityCacheEntry
                {
                    FullModId = e.FullModId,
                    FullModVersion = e.FullModVersion,
                    DllModId = e.DllModId,
                    DllModVersion = e.DllModVersion,
                    Status = CompatibilityMerger.NormalizeStatusCode(e.Status),
                    IsExactVersion = e.IsExactVersion,
                    Warning = e.Warning,
                    SourceUpdatedAt = response.Data.GeneratedAtUtc ?? response.Data.Revision,
                    FetchedAtUtc = nowUtc
                })
                .ToList();

            _compatibilityCache.SaveSnapshot(entries, response.Data.Revision ?? response.ETag, nowUtc);
            _syncState.SaveSuccess(CompatibilityStateKey, response.ETag ?? response.Data.Revision, null, nowUtc);

            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.Updated,
                ETag = response.ETag ?? response.Data.Revision,
                ModCount = entries.Count,
                LastSuccessUtc = nowUtc
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[CatalogSync] compatibility refresh failed: {ex.Message}");
            _syncState.SaveFailure(CompatibilityStateKey, "EXCEPTION", nowUtc, ComputeNextAttemptUtc(state));

            if (cachedCount > 0)
            {
                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.OfflineUsingCache,
                    ModCount = cachedCount,
                    LastSuccessUtc = state?.LastSuccessUtc,
                    ErrorMessage = ex.Message
                };
            }

            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.Failed,
                ErrorMessage = ex.Message,
                ModCount = 0
            };
        }
    }

    /// <summary>
    /// Synchronizuje oczekiwane referencje ikon (3.1-04).
    ///
    /// Metadane globalne są sprawdzane warunkowo przy własnym ETagu — również po 304
    /// katalogu, bo zmiana ikony globalnej nie musi zmieniać ETagu katalogu. Przypisania
    /// per mod commitujemy wyłącznie z kompletnej, zwalidowanej migawki katalogu.
    /// Błąd sieci, ucięta odpowiedź lub nieobsługiwany schemat zachowują ostatni dobry
    /// stan przypisań; żadne przypisanie nie jest usuwane w wyniku błędu.
    /// </summary>
    private async Task SyncIconAssignmentsAsync(
        IReadOnlyList<CatalogItemDto>? catalogItems,
        bool catalogComplete,
        CancellationToken cancellationToken)
    {
        var repository = _iconAssignments;
        if (repository is null)
            return;

        var nowUtc = DateTime.UtcNow;
        var metaState = _syncState.Get(IconMetaStateKey);

        // Schemat potwierdzony w poprzedniej udanej synchronizacji — służy do wykrycia
        // przejścia 0 → 1 (powrót backendu do kontraktu wersjonowanego).
        var previousSchema = metaState?.SchemaVersion ?? 0;

        try
        {
            var response = await _apiClient.GetCatalogMetaAsync(metaState?.ETag, cancellationToken);

            if (response.IsNotModified)
            {
                // Metadane bez zmian — schemat pozostaje taki sam, więc przejścia nie ma.
                // Metadane 304 nie potwierdzają zmiany schematu i nie wyzwalają dodatkowego
                // zdarzenia; UI zachowuje swoją bieżącą decyzję o wersjonowanych ikonach.
                var confirmedSchema = previousSchema;
                if (confirmedSchema != IconSchemaVersion)
                    return;

                var unchanged = new List<IconAssignmentChange>();
                if (catalogComplete && catalogItems is not null)
                    ApplyModAssignments(unchanged, catalogItems, confirmedSchema);

                RaiseIconAssignmentsChanged(unchanged, schemaActive: true);
                return;
            }

            if (!response.IsSuccess || response.Data is null)
            {
                _syncState.SaveFailure(IconMetaStateKey, $"HTTP_{response.StatusCode}", nowUtc, ComputeNextAttemptUtc(metaState));
                return;
            }

            var meta = response.Data;
            var schemaVersion = meta.IconsSchemaVersion ?? 0;
            _syncState.SaveSuccess(IconMetaStateKey, response.ETag, meta.IconsRevision, nowUtc, schemaVersion);

            // Przejście potwierdzone odpowiedzią: 0 → 1 (aktywacja) albo 1 → 0 (wycofanie).
            var schemaTransition = schemaVersion != previousSchema;

            if (schemaVersion != IconSchemaVersion)
            {
                // Backend potwierdził brak obsługiwanego schematu — UI wraca do ścieżki legacy.
                _log.Write("[CatalogSync] icons schema unsupported - legacy fallback");
                RaiseIconAssignmentsChanged(
                    Array.Empty<IconAssignmentChange>(),
                    schemaActive: false,
                    clearVersionedIcons: true,
                    schemaTransition: schemaTransition);
                return;
            }

            var changes = new List<IconAssignmentChange>();
            ApplyGlobalAssignment(changes, IconAssignmentKeys.GlobalVanilla, meta.Icons?.Vanilla, meta.Icons?.VanillaMemberPresent == true, schemaVersion);
            ApplyGlobalAssignment(changes, IconAssignmentKeys.GlobalDll, meta.Icons?.Dll, meta.Icons?.DllMemberPresent == true, schemaVersion);

            if (catalogComplete && catalogItems is not null)
                ApplyModAssignments(changes, catalogItems, schemaVersion);

            RaiseIconAssignmentsChanged(changes, schemaActive: true, schemaTransition: schemaTransition);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[CatalogSync] icon metadata refresh failed: {ex.Message}");
            _syncState.SaveFailure(IconMetaStateKey, "EXCEPTION", nowUtc, ComputeNextAttemptUtc(metaState));
            // Ostatni dobry stan przypisań zostaje — błąd transportu niczego nie kasuje.
        }
    }

    /// <summary>
    /// Stosuje przypisanie ikony globalnej. Człon nieobecny w ładunku nie zmienia
    /// poprzedniego przypisania; jawne null zapisuje trwały tombstone.
    /// </summary>
    private void ApplyGlobalAssignment(
        List<IconAssignmentChange> changes,
        string assignmentKey,
        CatalogIconDto? icon,
        bool memberPresent,
        int schemaVersion)
    {
        if (icon is null)
        {
            if (!memberPresent)
                return;

            ApplyRemoval(changes, assignmentKey, schemaVersion);
            return;
        }

        ApplyReference(changes, assignmentKey, icon, schemaVersion);
    }

    /// <summary>Stosuje przypisania ikon modów z kompletnej migawki katalogu.</summary>
    private void ApplyModAssignments(
        List<IconAssignmentChange> changes,
        IReadOnlyList<CatalogItemDto> items,
        int schemaVersion)
    {
        foreach (var item in items)
        {
            if (item.Id <= 0)
                continue;

            var assignmentKey = IconAssignmentKeys.ForMod(item.Id);

            if (item.Icon is null)
            {
                if (!item.IconMemberPresent)
                    continue;

                ApplyRemoval(changes, assignmentKey, schemaVersion);
                continue;
            }

            ApplyReference(changes, assignmentKey, item.Icon, schemaVersion);
        }
    }

    /// <summary>
    /// Zapisuje trwały tombstone: jawne null w schemacie 1 oznacza usunięcie przypisania.
    /// Wiersz pozostaje w bazie, aby odróżnić „usunięte” od „nigdy nie było przypisania”.
    /// </summary>
    private void ApplyRemoval(List<IconAssignmentChange> changes, string assignmentKey, int schemaVersion)
    {
        var repository = _iconAssignments!;
        var existing = repository.Get(assignmentKey);
        if (existing is { IsRemoved: true } && existing.SchemaVersion == schemaVersion)
            return;

        repository.MarkRemoved(assignmentKey, schemaVersion);
        changes.Add(new IconAssignmentChange { AssignmentKey = assignmentKey, Reference = null });
    }

    /// <summary>Zapisuje oczekiwaną referencję zasobu (bez zmiany ścieżki legacy).</summary>
    private void ApplyReference(
        List<IconAssignmentChange> changes,
        string assignmentKey,
        CatalogIconDto icon,
        int schemaVersion)
    {
        var repository = _iconAssignments!;

        if (!IsStructurallyValid(icon))
        {
            // Nieprawidłowa referencja nie jest zapisywana i nie usuwa poprzedniej.
            _log.Write($"[CatalogSync] icon reference invalid for {assignmentKey} - keeping last good");
            return;
        }

        var existing = repository.Get(assignmentKey);
        if (existing is { IsRemoved: false } &&
            existing.SchemaVersion == schemaVersion &&
            string.Equals(existing.Sha256, icon.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return; // Bez zmian — bez zapisu i bez zdarzenia.
        }

        repository.Upsert(new IconAssignment
        {
            AssignmentKey = assignmentKey,
            IsRemoved = false,
            AssetId = icon.AssetId,
            Url = icon.Url,
            Sha256 = icon.Sha256,
            ContentType = icon.ContentType,
            SizeBytes = icon.SizeBytes,
            SchemaVersion = schemaVersion,
            UpdatedAtUtc = DateTime.UtcNow
        });

        changes.Add(new IconAssignmentChange { AssignmentKey = assignmentKey, Reference = icon });
    }

    /// <summary>Strukturalna walidacja referencji (walidację hosta robi warstwa pobierania).</summary>
    private static bool IsStructurallyValid(CatalogIconDto icon) =>
        !string.IsNullOrWhiteSpace(icon.AssetId) &&
        !string.IsNullOrWhiteSpace(icon.Url) &&
        !string.IsNullOrWhiteSpace(icon.ContentType) &&
        icon.SizeBytes > 0 &&
        icon.Sha256 is { Length: 64 } sha && sha.All(Uri.IsHexDigit);

    private void RaiseIconAssignmentsChanged(
        IReadOnlyList<IconAssignmentChange> changes,
        bool schemaActive,
        bool clearVersionedIcons = false,
        bool schemaTransition = false)
    {
        // Zdarzenie jest potrzebne także wtedy, gdy nie zmieniło się żadne przypisanie,
        // ale zmieniła się aktywność schematu (np. 0 → 1 po powrocie backendu do
        // kontraktu wersjonowanego). Bez tego UI pozostałoby w trybie legacy do restartu.
        if (changes.Count == 0 && !clearVersionedIcons && !schemaTransition)
            return;

        IconAssignmentsChanged?.Invoke(this, new IconAssignmentsChangedEventArgs
        {
            Changes = changes,
            IsSchemaActive = schemaActive,
            ClearVersionedIcons = clearVersionedIcons,
            SchemaTransition = schemaTransition
        });
    }

    private async Task<(List<ModConfiguration> Mods, List<CatalogItemDto> Items, bool Complete, string? ETag, bool NotModified)> FetchCatalogAsync(        string? etag,
        CancellationToken cancellationToken)
    {
        var allItems = new List<CatalogItemDto>();
        const int pageSize = 200;
        var offset = 0;
        int? total = null;
        string? responseEtag = etag;
        var notModified = false;
        var complete = true;

        while (true)
        {
            var page = await _apiClient.GetCatalogAsync(
                new CatalogQuery { Offset = offset, Limit = pageSize },
                ifNoneMatch: offset == 0 ? etag : null,
                cancellationToken: cancellationToken);

            if (page.IsNotModified)
            {
                notModified = true;
                responseEtag = page.ETag ?? etag;
                break;
            }

            if (!page.IsSuccess || page.Data is null)
            {
                if (allItems.Count > 0)
                {
                    // Ucięta odpowiedź — migawka nie jest kompletna i nie może być
                    // traktowana jako pełny zestaw przypisań ikon.
                    complete = false;
                    break;
                }

                return ([], [], false, page.ETag, false);
            }

            responseEtag = page.ETag ?? responseEtag;
            allItems.AddRange(page.Data);
            total ??= page.Meta?.Total;

            if (page.Data.Count == 0 ||
                page.Data.Count < pageSize ||
                (total.HasValue && allItems.Count >= total.Value))
                break;

            offset += pageSize;
        }

        if (notModified)
            return ([], [], complete, responseEtag, true);

        if (total.HasValue && allItems.Count != total.Value)
            complete = false;

        var mods = allItems
            .Select(item => CatalogMapper.ToModConfiguration(item, _apiClient))
            .ToList();

        return (mods, allItems, complete, responseEtag, false);
    }

    private static bool ValidateCatalog(List<ModConfiguration> mods, int localCount, out string error)
    {
        error = string.Empty;
        if (mods.Count == 0)
        {
            error = "Katalog jest pusty.";
            return false;
        }

        var ids = new HashSet<int>();
        foreach (var mod in mods)
        {
            if (mod.Id <= 0 && !IsVanilla(mod))
            {
                error = $"Niepoprawne Id moda: {mod.Id} ({mod.ModName}).";
                return false;
            }

            if (!ids.Add(mod.Id))
            {
                error = $"Duplikat Id w odpowiedzi API: {mod.Id}.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(mod.ModName))
            {
                error = $"Pusty ModName dla Id={mod.Id}.";
                return false;
            }

            if (!IsAllowedType(mod.ModType))
            {
                error = $"Niepoprawny ModType '{mod.ModType}' dla {mod.ModName}.";
                return false;
            }

            if (!IsVanilla(mod) &&
                (mod.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) ||
                 mod.ModType.Equals("dll", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(mod.ModVersion))
                {
                    error = $"Brak ModVersion dla {mod.ModName}.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(mod.GitHubRepoOrLink))
                {
                    error = $"Brak linku pobierania dla {mod.ModName}.";
                    return false;
                }
            }
        }

        if (localCount > 0 && mods.Count < localCount * 0.5)
        {
            error = $"Podejrzany spadek liczby modów: {localCount} → {mods.Count}.";
            return false;
        }

        return true;
    }

    private bool IsDue(string key, TimeSpan minInterval)
    {
        var state = _syncState.Get(key);
        if (state?.LastSuccessUtc is null)
            return true;

        return DateTime.UtcNow - state.LastSuccessUtc.Value >= minInterval;
    }

    private bool IsInBackoff(string key)
    {
        var state = _syncState.Get(key);
        return state?.NextAllowedAttemptUtc is DateTime next && next > DateTime.UtcNow;
    }

    private CatalogSyncResult OfflineResult(string key, CatalogSyncStatus status, string? message = null)
    {
        var meta = _syncState.Get(key);
        return new CatalogSyncResult
        {
            Status = status,
            ETag = meta?.ETag,
            ModCount = key == CatalogStateKey ? _modRepository.GetAllMods().Count : _compatibilityCache.Count(),
            LastSuccessUtc = meta?.LastSuccessUtc,
            ErrorMessage = message
        };
    }

    private static DateTime ComputeNextAttemptUtc(CatalogSnapshotMetadata? state)
    {
        var failures = (state?.FailureCount ?? 0) + 1;
        var delayMinutes = failures switch
        {
            1 => 1,
            2 => 2,
            3 => 5,
            _ => 15
        };
        var jitterSeconds = Random.Shared.Next(0, 30);
        return DateTime.UtcNow.AddMinutes(delayMinutes).AddSeconds(jitterSeconds);
    }

    private static bool IsVanilla(ModConfiguration mod) =>
        mod.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase) ||
        mod.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedType(string? modType) =>
        modType is "full" or "dll" or "Vanilla";
}

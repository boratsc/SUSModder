using System.Collections.Concurrent;
using System.Diagnostics;

namespace SUSModder.Core.Storage;

/// <summary>
/// Punkt rozszerzenia dla skanowania systemu plików. Umożliwia testy deterministyczne
/// (błędy dostępu, znikające pliki) bez ingerencji w uprawnienia systemowe.
/// Wyliczenie jest strumieniowe — katalog nie jest materializowany w całości.
/// </summary>
internal interface IStorageFileSystemProbe
{
    /// <summary>
    /// Zwraca wpisy katalogu asynchronicznie (strumieniowo).
    /// Może rzucić wyjątek dostępu/braku I/O przy pierwszym pobraniu wpisu.
    /// </summary>
    IEnumerable<FileSystemInfo> EnumerateEntries(string directoryPath, CancellationToken cancellationToken);

    /// <summary>Zwraca atrybuty wpisu. Może rzucić wyjątek, gdy wpis zniknął.</summary>
    FileAttributes GetAttributes(FileSystemInfo entry);

    /// <summary>Zwraca logiczną długość pliku. Może rzucić wyjątek, gdy plik zniknął lub jest niedostępny.</summary>
    long GetFileLength(FileInfo file);
}

/// <summary>Domyślna implementacja probe'u oparta o standardowe API .NET.</summary>
internal sealed class DefaultStorageFileSystemProbe : IStorageFileSystemProbe
{
    public static readonly DefaultStorageFileSystemProbe Instance = new();

    public IEnumerable<FileSystemInfo> EnumerateEntries(string directoryPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var enumerator = new DirectoryInfo(directoryPath).EnumerateFileSystemInfos().GetEnumerator();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!enumerator.MoveNext())
                    yield break;

                yield return enumerator.Current;
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    public FileAttributes GetAttributes(FileSystemInfo entry) => entry.Attributes;

    public long GetFileLength(FileInfo file) => file.Length;
}

/// <summary>
/// Wspólna implementacja serwisu pomiaru rozmiaru.
/// Wystarczy jedna instancja na proces (rejestrowana jako singleton w DI).
///
/// Kontrakt bezpieczeństwa:
/// - nigdy nie mierzymy ścieżki przechodzącej przez reparse point (junction/symlink) —
///   ani korzenia, ani przodka w zakresie zaufania (patrz <see cref="StorageMeasurementOptions.TrustedRoot"/>),
/// - rewizja, publikacja wyniku i unieważnienie odbywają się pod jednym lockiem wpisu (bez globalnego blokowania),
/// - wywołanie po unieważnieniu nie dołącza do lotu o starej rewizji,
/// - wyliczanie katalogów jest strumieniowe i respektuje cancellation.
/// </summary>
public sealed class StorageSizeMeasurementService : IStorageSizeMeasurementService, IDisposable
{
    /// <summary>Domyślna liczba równoległych skanów.</summary>
    public const int DefaultMaxConcurrency = 4;

    private static readonly TimeSpan DefaultMaxCacheAge = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scanGate;
    private readonly TimeSpan _defaultMaxCacheAge;
    private readonly IStorageFileSystemProbe _probe;
    private bool _disposed;

    /// <summary>
    /// Zdarzenie wywoływane po unieważnieniu pomiaru — UI czyści etykiety rozmiaru.
    /// </summary>
    public event EventHandler<StorageInvalidatedEventArgs>? Invalidated;

    /// <summary>
    /// Tworzy serwis pomiaru.
    /// </summary>
    /// <param name="maxConcurrency">Maksymalna liczba równoległych skanów.</param>
    /// <param name="defaultMaxCacheAge">Jak długo wynik w cache jest uznawany za świeży.</param>
    public StorageSizeMeasurementService(
        int maxConcurrency = DefaultMaxConcurrency,
        TimeSpan? defaultMaxCacheAge = null)
        : this(maxConcurrency, defaultMaxCacheAge, DefaultStorageFileSystemProbe.Instance)
    {
    }

    /// <summary>Wariant dla testów — pozwala podstawić deterministyczny probe systemu plików.</summary>
    internal StorageSizeMeasurementService(
        int maxConcurrency,
        TimeSpan? defaultMaxCacheAge,
        IStorageFileSystemProbe probe)
    {
        if (maxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Liczba równoległych skanów musi być dodatnia.");

        ArgumentNullException.ThrowIfNull(probe);

        _scanGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _defaultMaxCacheAge = defaultMaxCacheAge ?? DefaultMaxCacheAge;
        _probe = probe;
    }

    /// <inheritdoc />
    public async Task<StorageSizeResult> MeasureAsync(
        string rootPath,
        StorageMeasurementOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var opts = options ?? StorageMeasurementOptions.Default;
        var trustedRoot = StoragePathNormalizer.TryNormalize(opts.TrustedRoot);

        string key;
        try
        {
            key = StoragePathNormalizer.Normalize(rootPath);
        }
        catch (Exception ex)
        {
            return StorageSizeResult.Unavailable(rootPath ?? string.Empty, StorageSizeErrorCode.PathInvalid, ex.Message);
        }

        if (key.Length == 0)
            return StorageSizeResult.Unavailable(rootPath ?? string.Empty, StorageSizeErrorCode.PathEmpty);

        var entry = _cache.GetOrAdd(key, static _ => new CacheEntry());
        var maxAge = opts.MaxCacheAge ?? _defaultMaxCacheAge;

        // Wspólny lot dla kanonicznej ścieżki: równoległe wywołania nie skanują dwa razy.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Flight flight;
            StorageSizeResult? cached = null;

            lock (entry.Sync)
            {
                if (!opts.ForceRefresh
                    && string.Equals(entry.TrustedRoot, trustedRoot, StringComparison.OrdinalIgnoreCase)
                    && TryGetFreshLocked(entry, maxAge, out var fresh))
                {
                    cached = fresh;
                    flight = null!;
                }
                else
                {
                    var current = entry.Flight;

                    // Lot o innej rewizji (po unieważnieniu albo po nowszym zleceniu) nie może być dołączony.
                    // Lot o innym zasięgu zaufania też nie — wynik policzony bez granicy zaufania
                    // nie jest tym samym pomiarem co wynik ograniczony do biblioteki.
                    if (current is not null
                        && !current.Cts.IsCancellationRequested
                        && current.Revision == entry.Revision
                        && string.Equals(current.TrustedRoot, trustedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        current.Waiters++;
                        flight = current;
                    }
                    else
                    {
                        if (current is not null)
                            entry.Flight = null;

                        var revision = ++entry.Revision;
                        var newFlight = new Flight
                        {
                            Revision = revision,
                            TrustedRoot = trustedRoot,
                            Waiters = 1
                        };

                        entry.Flight = newFlight;
                        newFlight.Task = Task.Run(
                            () => RunMeasurementAsync(key, entry, trustedRoot, revision, newFlight.Cts.Token));

                        flight = newFlight;
                    }
                }
            }

            if (cached is not null)
                return cached;

            try
            {
                return await flight.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (flight.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Lot został anulowany, bo odszedł ostatni czekający — zaczynamy nowy.
                continue;
            }
            finally
            {
                ReleaseFlight(entry, flight);
            }
        }

        return StorageSizeResult.Unavailable(key, StorageSizeErrorCode.IoError, "measurement_restart_limit");
    }

    /// <inheritdoc />
    public async Task<StorageRootMeasurement> MeasureManyAsync(
        IEnumerable<string> rootPaths,
        StorageMeasurementOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var (kept, dropped) = StoragePathNormalizer.DeduplicateRoots(rootPaths);

        var results = new List<StorageSizeResult>(kept.Count);
        if (kept.Count > 0)
        {
            var tasks = kept.Select(path => MeasureAsync(path, options, cancellationToken)).ToArray();
            results.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
        }

        long total = 0;
        var hasPartial = false;
        var hasUnavailable = false;

        foreach (var result in results)
        {
            if (result.HasValue)
            {
                total += result.LogicalBytes;
            }

            if (result.State == StorageSizeState.Partial)
                hasPartial = true;

            if (result.State == StorageSizeState.Unavailable)
                hasUnavailable = true;
        }

        var allUnavailable = results.Count > 0 && results.All(r => r.State == StorageSizeState.Unavailable);

        // Komplet + niedostępny = wynik niepełny (suma zaniżona), a nie „komplet”.
        var state = allUnavailable
            ? StorageSizeState.Unavailable
            : hasPartial || hasUnavailable
                ? StorageSizeState.Partial
                : StorageSizeState.Complete;

        return new StorageRootMeasurement
        {
            Roots = results,
            IncludedRootPaths = kept,
            DeduplicatedRootPaths = dropped,
            TotalLogicalBytes = total,
            State = state,
            PartialRootCount = results.Count(r => r.State == StorageSizeState.Partial),
            UnavailableRootCount = results.Count(r => r.State == StorageSizeState.Unavailable),
            MeasuredAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public StorageSizeResult? GetSnapshot(string rootPath)
    {
        var key = StoragePathNormalizer.TryNormalize(rootPath);
        if (string.IsNullOrEmpty(key))
            return null;

        if (!_cache.TryGetValue(key!, out var entry))
            return null;

        lock (entry.Sync)
        {
            return entry.Result;
        }
    }

    /// <inheritdoc />
    public StorageSizeResult GetDisplaySnapshot(string rootPath)
    {
        var key = StoragePathNormalizer.TryNormalize(rootPath);
        if (string.IsNullOrEmpty(key))
            return StorageSizeResult.Loading(rootPath ?? string.Empty);

        if (!_cache.TryGetValue(key!, out var entry))
            return StorageSizeResult.Loading(key!);

        lock (entry.Sync)
        {
            var cached = entry.Result;
            if (cached is null)
                return StorageSizeResult.Loading(key!);

            // Wynik niedostępny nigdy nie staje się „stale z zerem” — pozostaje niedostępny.
            if (!cached.HasMeasuredValue)
                return cached;

            var isStale = entry.Invalidated || IsExpired(cached, _defaultMaxCacheAge);
            return isStale ? cached with { State = StorageSizeState.Stale } : cached;
        }
    }

    /// <inheritdoc />
    public bool IsMeasurementInProgress(string rootPath)
    {
        var key = StoragePathNormalizer.TryNormalize(rootPath);
        if (string.IsNullOrEmpty(key))
            return false;

        if (!_cache.TryGetValue(key!, out var entry))
            return false;

        lock (entry.Sync)
        {
            var flight = entry.Flight;
            return flight is not null && !flight.Task.IsCompleted;
        }
    }

    /// <inheritdoc />
    public long GetRevision(string rootPath)
    {
        var key = StoragePathNormalizer.TryNormalize(rootPath);
        if (string.IsNullOrEmpty(key))
            return 0;

        if (!_cache.TryGetValue(key!, out var entry))
            return 0;

        lock (entry.Sync)
        {
            return entry.Revision;
        }
    }

    /// <inheritdoc />
    public void Invalidate(string rootPath)
    {
        var key = StoragePathNormalizer.TryNormalize(rootPath);
        if (string.IsNullOrEmpty(key))
            return;

        var entry = _cache.GetOrAdd(key!, static _ => new CacheEntry());

        lock (entry.Sync)
        {
            entry.Revision++;
            entry.Invalidated = true;
        }

        RaiseInvalidated(key, includeDescendants: false);
    }

    /// <inheritdoc />
    public void InvalidateUnder(string rootPath)
    {
        var key = StoragePathNormalizer.TryNormalize(rootPath);
        if (string.IsNullOrEmpty(key))
            return;

        var entry = _cache.GetOrAdd(key!, static _ => new CacheEntry());

        lock (entry.Sync)
        {
            entry.Revision++;
            entry.Invalidated = true;
        }

        RaiseInvalidated(key, includeDescendants: true);

        foreach (var pair in _cache)
        {
            if (ReferenceEquals(pair.Value, entry) || !StoragePathNormalizer.IsSubPath(key, pair.Key))
                continue;

            lock (pair.Value.Sync)
            {
                pair.Value.Revision++;
                pair.Value.Invalidated = true;
            }

            RaiseInvalidated(pair.Key, includeDescendants: false);
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        foreach (var pair in _cache)
        {
            lock (pair.Value.Sync)
            {
                pair.Value.Revision++;
                pair.Value.Invalidated = true;
            }

            RaiseInvalidated(pair.Key, includeDescendants: false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var pair in _cache)
        {
            lock (pair.Value.Sync)
            {
                var flight = pair.Value.Flight;
                pair.Value.Flight = null;

                if (flight is null)
                    continue;

                try
                {
                    flight.Cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // ignoruj
                }
            }
        }

        _scanGate.Dispose();
    }

    private void RaiseInvalidated(string key, bool includeDescendants)
    {
        try
        {
            Invalidated?.Invoke(this, new StorageInvalidatedEventArgs(key, includeDescendants));
        }
        catch
        {
            // Subskrybent nie może zepsuć unieważnienia.
        }
    }

    private bool TryGetFreshLocked(CacheEntry entry, TimeSpan maxAge, out StorageSizeResult result)
    {
        var cached = entry.Result;
        if (cached is null)
        {
            result = null!;
            return false;
        }

        if (entry.Invalidated)
        {
            result = null!;
            return false;
        }

        if (IsExpired(cached, maxAge))
        {
            result = null!;
            return false;
        }

        result = cached;
        return true;
    }

    private static bool IsExpired(StorageSizeResult cached, TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.Zero)
            return false;

        if (maxAge == TimeSpan.Zero)
            return true;

        return DateTimeOffset.UtcNow - cached.MeasuredAtUtc > maxAge;
    }

    private void ReleaseFlight(CacheEntry entry, Flight flight)
    {
        lock (entry.Sync)
        {
            flight.Waiters--;
            if (flight.Waiters > 0)
                return;

            if (ReferenceEquals(entry.Flight, flight))
                entry.Flight = null;

            try
            {
                flight.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignoruj
            }
        }
    }

    private async Task<StorageSizeResult> RunMeasurementAsync(
        string key,
        CacheEntry entry,
        string? trustedRoot,
        long revision,
        CancellationToken token)
    {
        StorageSizeResult result;

        try
        {
            await _scanGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                result = await Task.Run(() => ScanPath(key, trustedRoot, token), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _scanGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = StorageSizeResult.Unavailable(key, StorageSizeErrorCode.IoError, ex.Message);
        }

        result = result with { Revision = revision };

        // Publikacja pod tym samym lockiem co rewizja i unieważnienie — bez globalnego blokowania.
        lock (entry.Sync)
        {
            if (entry.Revision == revision)
            {
                entry.Result = result;
                entry.Invalidated = false;
                entry.TrustedRoot = trustedRoot;
            }
        }

        return result;
    }

    private StorageSizeResult ScanPath(string path, string? trustedRoot, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();

        // Kontrakt „no-follow”: ścieżka przez reparse point nigdy nie jest mierzona.
        if (StorageLinkBoundary.IsLinkPath(path, trustedRoot, out var linkPath))
        {
            stopwatch.Stop();
            return StorageSizeResult.Unavailable(path, StorageSizeErrorCode.PathIsLink, $"link_path:{linkPath}");
        }

        if (File.Exists(path))
        {
            try
            {
                var length = new FileInfo(path).Length;
                stopwatch.Stop();
                return new StorageSizeResult
                {
                    RootPath = path,
                    LogicalBytes = length,
                    State = StorageSizeState.Complete,
                    ErrorCode = StorageSizeErrorCode.None,
                    FileCount = 1,
                    DirectoryCount = 0,
                    HasMeasuredValue = true,
                    MeasuredAtUtc = DateTimeOffset.UtcNow,
                    Duration = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return StorageSizeResult.Unavailable(path, StorageSizeErrorCode.IoError, ex.Message);
            }
        }

        DirectoryInfo rootInfo;
        try
        {
            rootInfo = new DirectoryInfo(path);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return StorageSizeResult.Unavailable(path, StorageSizeErrorCode.PathInvalid, ex.Message);
        }

        if (!rootInfo.Exists)
        {
            stopwatch.Stop();
            return StorageSizeResult.Unavailable(path, StorageSizeErrorCode.PathNotFound, "directory_not_found");
        }

        long bytes = 0;
        var fileCount = 0;
        var directoryCount = 1;
        var skipped = 0;
        var skippedReparsePoints = 0;
        var errorCode = StorageSizeErrorCode.None;
        string? diagnostic = null;
        var rootFailed = false;

        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var current = pending.Pop();
            var isRoot = string.Equals(current, path, StringComparison.OrdinalIgnoreCase);

            IEnumerator<FileSystemInfo>? enumerator = null;
            try
            {
                enumerator = _probe.EnumerateEntries(current, token).GetEnumerator();

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    bool moved;
                    try
                    {
                        moved = enumerator.MoveNext();
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.AccessDenied;
                        diagnostic ??= ex.Message;
                        rootFailed = isRoot;
                        break;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.IoError;
                        diagnostic ??= "directory_missing";
                        rootFailed = isRoot;
                        break;
                    }
                    catch (IOException ex)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.IoError;
                        diagnostic ??= ex.Message;
                        rootFailed = isRoot;
                        break;
                    }

                    if (!moved)
                        break;

                    var entry = enumerator.Current;

                    FileAttributes attributes;
                    try
                    {
                        attributes = _probe.GetAttributes(entry);
                    }
                    catch (FileNotFoundException)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.IoError;
                        diagnostic ??= "entry_missing";
                        continue;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.AccessDenied;
                        diagnostic ??= ex.Message;
                        continue;
                    }
                    catch (IOException ex)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.IoError;
                        diagnostic ??= ex.Message;
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        // Junction / symlink — nigdy nie podążamy (pętle i obce drzewa).
                        skipped++;
                        skippedReparsePoints++;
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        directoryCount++;
                        pending.Push(entry.FullName);
                        continue;
                    }

                    if (entry is not FileInfo fileInfo)
                        continue;

                    try
                    {
                        bytes += _probe.GetFileLength(fileInfo);
                        fileCount++;
                    }
                    catch (FileNotFoundException)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.IoError;
                        diagnostic ??= "file_missing";
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.AccessDenied;
                        diagnostic ??= ex.Message;
                    }
                    catch (IOException ex)
                    {
                        skipped++;
                        errorCode = StorageSizeErrorCode.IoError;
                        diagnostic ??= ex.Message;
                    }
                }
            }
            finally
            {
                enumerator?.Dispose();
            }

            if (rootFailed)
                break;
        }

        stopwatch.Stop();

        if (rootFailed)
        {
            // Niedostępność korzenia nie jest zerem — pomiar jest niemożliwy.
            return StorageSizeResult.Unavailable(
                path,
                errorCode == StorageSizeErrorCode.None ? StorageSizeErrorCode.IoError : errorCode,
                diagnostic);
        }

        return new StorageSizeResult
        {
            RootPath = path,
            LogicalBytes = bytes,
            State = skipped > 0 ? StorageSizeState.Partial : StorageSizeState.Complete,
            ErrorCode = errorCode,
            FileCount = fileCount,
            DirectoryCount = directoryCount,
            SkippedEntryCount = skipped,
            SkippedReparsePointCount = skippedReparsePoints,
            HasMeasuredValue = true,
            MeasuredAtUtc = DateTimeOffset.UtcNow,
            Duration = stopwatch.Elapsed,
            DiagnosticMessage = diagnostic
        };
    }

    /// <summary>
    /// Sprawdza, czy ścieżka (albo jeden z jej przodków w zakresie zaufania) jest reparse pointem.
    /// Zasięg zaufania to granica, do której sprawdzamy przodków — powyżej niej decyzja należy do wywołującego.
    /// </summary>
    private static bool TryFindLinkInPath(string path, string? trustedRoot, out string? linkPath)
        => StorageLinkBoundary.IsLinkPath(path, trustedRoot, out linkPath);

    private static FileAttributes? TryGetAttributes(string path)
        => StorageLinkBoundary.TryGetAttributes(path);

    private sealed class CacheEntry
    {
        /// <summary>Lock wpisu — chroni rewizję, publikację wyniku, unieważnienie i lot pomiaru.</summary>
        public readonly object Sync = new();

        public StorageSizeResult? Result;
        public long Revision;
        public bool Invalidated;
        public string? TrustedRoot;
        public Flight? Flight;
    }

    private sealed class Flight
    {
        public CancellationTokenSource Cts { get; } = new();
        public Task<StorageSizeResult> Task { get; set; } = System.Threading.Tasks.Task.FromResult(
            StorageSizeResult.Unavailable(string.Empty, StorageSizeErrorCode.IoError, "not_started"));
        public int Waiters;
        public long Revision;

        /// <summary>Zasięg zaufania użyty przy tym pomiarze — loty o różnym zasięgu nie są wspólne.</summary>
        public string? TrustedRoot;
    }
}

/// <summary>Informacja o unieważnieniu pomiaru.</summary>
public sealed class StorageInvalidatedEventArgs : EventArgs
{
    /// <summary>Kanoniczna ścieżka, której dotyczy unieważnienie.</summary>
    public string RootPath { get; }

    /// <summary>Czy unieważnienie objęło też ścieżki zapisane pod korzeniem.</summary>
    public bool IncludesDescendants { get; }

    /// <summary>Tworzy argumenty zdarzenia.</summary>
    public StorageInvalidatedEventArgs(string rootPath, bool includesDescendants)
    {
        RootPath = rootPath;
        IncludesDescendants = includesDescendants;
    }
}

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SUSModder.Core.Api.Models;

namespace SUSModder.Core.Services;

/// <summary>Disposable technical cache, not a catalog/configuration repository.</summary>
public sealed class IconAssetCache : IDisposable
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private const long Budget = 100L * 1024 * 1024;
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly long _budget;
    private readonly Func<ReadOnlyMemory<byte>, bool> _decode;
    private readonly SemaphoreSlim _downloads = new(4);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _pending = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retry = new();
    private readonly Dictionary<string, string> _desired = new();
    private Dictionary<string, Entry> _entries = new();
    public sealed record Entry(string Hash, DateTimeOffset CheckedAt);

    public IconAssetCache(Func<ReadOnlyMemory<byte>, bool> decodeValidator, string? directory = null,
        HttpMessageHandler? handler = null, TimeProvider? timeProvider = null, long cacheBudgetBytes = Budget)
    {
        _decode = decodeValidator;
        _clock = timeProvider ?? TimeProvider.System;
        _budget = cacheBudgetBytes > 0 ? cacheBudgetBytes : throw new ArgumentOutOfRangeException(nameof(cacheBudgetBytes));
        _root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SUSModder", "cache", "icons");
        // Custom handlers are for tests; production never follows redirects.
        _http = new HttpClient(handler ?? CreateHttpHandler()) { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            var manifest = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(Path.Combine(_root, "manifest.json")));
            if (manifest is not null)
                _entries = manifest.Where(p => p.Value is not null && IsHash(p.Value.Hash)).ToDictionary(p => p.Key, p => p.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public static HttpClientHandler CreateHttpHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    /// <summary>
    /// Call with the complete active legacy URL set after a successful catalog/UI snapshot (not a page/subset).
    /// Removes only URL-keyed legacy assignments. Stable mod/global assignments and their last-good hashes
    /// remain protected. Obsolete files become ordinary oldest-file budget candidates, with a one-minute
    /// in-flight publication grace. Does not delete files or dispose bitmaps still used by views.
    /// </summary>
    public Task ReconcileLegacyAssignmentsAsync(IEnumerable<string> activeUrls, CancellationToken cancellationToken = default)
    {
        var active = activeUrls.Select(url => "legacy:" + url).ToHashSet(StringComparer.Ordinal);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                foreach (var key in _entries.Keys.Concat(_desired.Keys).Distinct().Where(key =>
                    key.StartsWith("legacy:", StringComparison.Ordinal) && !active.Contains(key)).ToArray())
                {
                    _entries.Remove(key);
                    // A late old download must not recreate a reconciled-away assignment.
                    _desired.Remove(key);
                }
                SaveManifest();
            }
        }, cancellationToken);
    }

    public static bool IsAllowedUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.IdnHost.Equals("susmodder.app", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.Equals("susmodder-cdn.ovh", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.Equals("api.susmodder-cdn.ovh", StringComparison.OrdinalIgnoreCase));

    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private string FileFor(string hash) => Path.Combine(_root, hash.ToLowerInvariant() + ".png");
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static bool IsBoundedPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 || bytes.Length > MaxBytes || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        return width is > 0 and <= 1024 && height is > 0 and <= 1024;
    }

    /// <summary>Null versioned reference is explicit deletion. Legacy callers supply legacyUrl instead.</summary>
    public async Task<string?> GetAsync(string assignmentKey, CatalogIconDto? reference, string? legacyUrl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = reference?.Url ?? legacyUrl;
        var identity = reference is null ? "legacy:" + url : "sha256:" + reference.Sha256?.ToLowerInvariant();
        lock (_gate)
        {
            _desired[assignmentKey] = identity;
            if (reference is null && legacyUrl is null)
            {
                _entries.Remove(assignmentKey);
                SaveManifest();
                return null;
            }
        }
        var previous = await Task.Run(() => ReadLastGood(assignmentKey), cancellationToken).ConfigureAwait(false);
        if (!IsAllowedUrl(url ?? "") || (reference is not null && (!IsHash(reference.Sha256)
            || string.IsNullOrWhiteSpace(reference.AssetId) || reference.ContentType != "image/png"
            || reference.SizeBytes is <= 0 or > MaxBytes))) return previous;
        if (reference?.Sha256 is { } expectedHash && ReadValidated(expectedHash) is { } existing)
            return Commit(assignmentKey, identity, expectedHash, existing);
        lock (_gate)
            if (reference is null && previous is not null && _entries.TryGetValue(assignmentKey, out var entry)
                && _clock.GetUtcNow() - entry.CheckedAt < TimeSpan.FromDays(1)) return previous;
        if (_retry.TryGetValue(identity, out var retryAt) && retryAt > _clock.GetUtcNow()) return previous;
        var pending = _pending.GetOrAdd(identity, _ => new Lazy<Task<string?>>(() => DownloadAsync(url!, reference, identity)));
        string? hash;
        var operation = pending.Value;
        try { hash = await operation.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (operation.IsCompleted) _pending.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(identity, pending));
            else _ = operation.ContinueWith(_ => _pending.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(identity, pending)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return hash is null ? ReadLastGood(assignmentKey) : Commit(assignmentKey, identity, hash, FileFor(hash));
    }

    private string? Commit(string key, string identity, string hash, string path)
    {
        lock (_gate)
        {
            if (_desired.GetValueOrDefault(key) != identity) return ReadLastGood(key);
            _entries[key] = new Entry(hash.ToLowerInvariant(), _clock.GetUtcNow());
            SaveManifest();
            return path;
        }
    }

    private string? ReadLastGood(string key)
    {
        lock (_gate) return _entries.TryGetValue(key, out var entry) ? ReadValidated(entry.Hash) : null;
    }

    private string? ReadValidated(string hash)
    {
        try
        {
            var path = FileFor(hash);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return null;
            var bytes = File.ReadAllBytes(path);
            return IsBoundedPng(bytes) && Hash(bytes).Equals(hash, StringComparison.OrdinalIgnoreCase) && _decode(bytes) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task<string?> DownloadAsync(string url, CatalogIconDto? reference, string identity)
    {
        var entered = false;
        try
        {
            await _downloads.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            entered = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxBytes) return Failed(identity);
            using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > MaxBytes) return Failed(identity);
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            var hash = Hash(bytes);
            if (!IsBoundedPng(bytes) || (reference is not null && (reference.SizeBytes != bytes.Length
                || !hash.Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase))) || !_decode(bytes)) return Failed(identity);
            lock (_gate)
            {
                Directory.CreateDirectory(_root);
                if (!MakeRoom(bytes.Length)) return Failed(identity);
                AtomicWrite(FileFor(hash), bytes);
            }
            _retry.TryRemove(identity, out _);
            return hash;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        { return Failed(identity); }
        finally { if (entered) _downloads.Release(); }
    }

    private string? Failed(string identity)
    {
        _retry[identity] = _clock.GetUtcNow().AddMinutes(1);
        return null;
    }

    private bool MakeRoom(int incoming)
    {
        var files = new DirectoryInfo(_root).GetFiles("*.png");
        var total = files.Sum(f => f.Length);
        var protectedHashes = _entries.Values.Select(e => e.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total + incoming <= _budget) break;
            // A completed shared download may not yet have committed its assignment.
            if (protectedHashes.Contains(Path.GetFileNameWithoutExtension(file.Name))
                || file.LastWriteTimeUtc > _clock.GetUtcNow().UtcDateTime.AddMinutes(-1)) continue;
            total -= file.Length;
            file.Delete();
        }
        return total + incoming <= _budget;
    }

    private void SaveManifest()
    {
        try
        {
            Directory.CreateDirectory(_root);
            AtomicWrite(Path.Combine(_root, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void AtomicWrite(string destination, byte[] bytes)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _http.Dispose();
        // Semaphores/CTS are intentionally not disposed while outstanding downloads unwind.
    }
}

/// <summary>Atomic, clock-testable throttle shared by memory-hit and cold legacy preload paths.</summary>
public sealed class LegacyIconRefreshSchedule(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _next = new();

    public bool TryBegin(string reference)
    {
        var now = _clock.GetUtcNow();
        while (true)
        {
            if (!_next.TryGetValue(reference, out var next))
            {
                if (_next.TryAdd(reference, now.AddMinutes(1))) return true;
            }
            else
            {
                if (next > now) return false;
                if (_next.TryUpdate(reference, now.AddMinutes(1), next)) return true;
            }
        }
    }

    public void Defer(string reference) => _next[reference] = _clock.GetUtcNow().AddMinutes(1);
}

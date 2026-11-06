using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Services;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Services;

/// <summary>Remote converter reads are memory-only. Hosts refresh bindings on IconAvailable.</summary>
public class ModIconPreloader
{
    private const int MemoryLimit = 64;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, WeakReference<Bitmap>> Images = new();
    private static readonly LinkedList<(string Key, Bitmap Image)> Recent = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Pending = new();
    private static readonly LegacyIconRefreshSchedule RefreshSchedule = new();
    private static readonly SemaphoreSlim DecodeSlots = new(4);
    // Even manifest initialization must not do disk I/O inside a converter call.
    private static readonly Task<IconAssetCache> Assets = Task.Run(() => new IconAssetCache(ValidateDecode));
    private static readonly CancellationTokenSource ShutdownToken = new();
    private static readonly VersionedIconRetryState VersionedRequests = new(AttemptVersionedAsync, lifetimeToken: ShutdownToken.Token);
    private static Bitmap? _placeholder;

    /// <summary>Raised on the UI thread; argument is the legacy reference or stable versioned assignment key.</summary>
    public static event Action<string>? IconAvailable;

    public static Bitmap? GetIcon(string? iconReference)
    {
        if (string.IsNullOrWhiteSpace(iconReference)) return Placeholder();
        if (TryGet(iconReference) is { } cached)
        {
            // Returning the bitmap must not bypass mutable-URL freshness forever.
            // QueueLegacy is memory-only here; disk/network checks stay on its background task.
            if (!IsBundledVanilla(iconReference) && IconAssetCache.IsAllowedUrl(iconReference)) QueueLegacy(iconReference);
            return cached;
        }
        if (IsBundledVanilla(iconReference) || !Uri.TryCreate(iconReference, UriKind.Absolute, out var uri)
            || uri.Scheme == "avares") return LoadBundled(iconReference) ?? Placeholder();
        // Do not perform even disk I/O from a remote converter miss.
        QueueLegacy(iconReference);
        return Placeholder();
    }

    public static Task PreloadIconsAsync(IEnumerable<string?> iconReferences, CancellationToken cancellationToken = default) =>
        Task.WhenAll(iconReferences.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().Select(r =>
            IsBundledVanilla(r!) || !Uri.TryCreate(r, UriKind.Absolute, out var uri) || uri.Scheme == "avares"
                ? Task.Run(() => { LoadBundled(r!); }, cancellationToken)
                : QueueLegacy(r!).WaitAsync(cancellationToken)));

    private static Task QueueLegacy(string reference)
    {
        if (Pending.TryGetValue(reference, out var existing)) return existing.Value;
        if (!RefreshSchedule.TryBegin(reference)) return Task.CompletedTask;
        var lazy = Pending.GetOrAdd(reference, key => new Lazy<Task>(() => Task.Run(async () =>
        {
            try
            {
                var assets = await Assets.ConfigureAwait(false);
                var path = await assets.GetAsync("legacy:" + key, null, key, ShutdownToken.Token).ConfigureAwait(false);
                if (path is not null) await DecodeAndPublishAsync(key, path).ConfigureAwait(false);
                // Legacy mutable URLs are rechecked in the disk layer after 24 hours.
                RefreshSchedule.Defer(key);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) when (ShutdownToken.IsCancellationRequested) { }
            finally { Pending.TryRemove(key, out _); }
        })));
        return lazy.Value;
    }

    /// <summary>Host supplies all currently active legacy URLs after a complete successful snapshot.</summary>
    public static async Task ReconcileLegacyAssignmentsAsync(IEnumerable<string?> activeIconReferences,
        CancellationToken cancellationToken = default)
    {
        var active = activeIconReferences.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!).ToArray();
        var assets = await Assets.ConfigureAwait(false);
        await assets.ReconcileLegacyAssignmentsAsync(active, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Call only for schema 1. Null explicitly removes the versioned assignment.</summary>
    public static async Task PreloadVersionedAsync(string assignmentKey, CatalogIconDto? reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = VersionedRequests.Register(assignmentKey, reference);
        if (reference is null)
        {
            VersionedRequests.ApplyIfCurrent(request, () =>
            {
                lock (Gate) { Images.Remove(assignmentKey); RemoveRecent(assignmentKey); }
            });
        }
        // Await only the first attempt, never the background retry loop.
        await VersionedRequests.Queue(request).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static Bitmap? GetVersionedIcon(string assignmentKey)
    {
        if (TryGet(assignmentKey) is { } cached) return cached;
        // Memory-only scheduling; uses the already-registered wanted generation, never Register.
        VersionedRequests.QueueMissing(assignmentKey);
        return Placeholder();
    }

    private static async Task<bool> AttemptVersionedAsync(VersionedIconRetryState.Request request, CancellationToken token)
    {
        var assets = await Assets.ConfigureAwait(false);
        // Guard the actual synchronous Core request registration, not just queue creation.
        var operation = VersionedRequests.StartIfCurrent(request,
            () => assets.GetAsync(request.Key, request.Reference, cancellationToken: token));
        if (operation is null) return false;
        var path = await operation.ConfigureAwait(false);
        if (request.Reference is null)
        {
            VersionedRequests.ApplyIfCurrent(request, () => Notify(request.Key));
            return true;
        }
        if (path is null) return false;
        var published = await DecodeAndPublishAsync(request.Key, path, request).ConfigureAwait(false);
        // A last-good fallback is displayable but does not satisfy the wanted version.
        return published && Path.GetFileNameWithoutExtension(path).Equals(request.Reference.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static Bitmap Placeholder()
    {
        lock (Gate)
        {
            if (_placeholder is not null) return _placeholder;
            var image = new WriteableBitmap(new Avalonia.PixelSize(64, 64), new Avalonia.Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var frame = image.Lock())
            {
                var pixels = new byte[frame.RowBytes * 64];
                for (var y = 0; y < 64; y++)
                    for (var x = 0; x < 64; x++)
                    {
                        if ((x - 32) * (x - 32) + (y - 32) * (y - 32) > 28 * 28) continue;
                        var offset = y * frame.RowBytes + x * 4;
                        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 120;
                        pixels[offset + 3] = 255;
                    }
                Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
            }
            return _placeholder = image;
        }
    }

    private static async Task<bool> DecodeAndPublishAsync(string key, string path, VersionedIconRetryState.Request? request = null)
    {
        await DecodeSlots.WaitAsync(ShutdownToken.Token).ConfigureAwait(false);
        try
        {
            if (request is not null && !VersionedRequests.IsCurrent(request)) return false;
            if (new FileInfo(path).Length > IconAssetCache.MaxBytes) return false;
            var bytes = await File.ReadAllBytesAsync(path, ShutdownToken.Token).ConfigureAwait(false);
            if (!IconAssetCache.IsBoundedPng(bytes)) return false;
            var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
            var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
            // Bound both dimensions, including extreme portrait aspect ratios. Never upscale.
            using var stream = new MemoryStream(bytes, false);
            var bitmap = width >= height
                ? Bitmap.DecodeToWidth(stream, (int)Math.Min(128u, width))
                : Bitmap.DecodeToHeight(stream, (int)Math.Min(128u, height));
            if (request is not null)
            {
                if (!VersionedRequests.ApplyIfCurrent(request, () => Store(key, bitmap)))
                {
                    bitmap.Dispose(); // Never exposed to any view.
                    return false;
                }
            }
            else Store(key, bitmap);
            Notify(key);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { System.Diagnostics.Debug.WriteLine("ICON_INVALID_IMAGE"); return false; }
        finally { DecodeSlots.Release(); }
    }

    private static bool ValidateDecode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), false);
            using var image = new Bitmap(stream);
            return image.PixelSize.Width is > 0 and <= 1024 && image.PixelSize.Height is > 0 and <= 1024;
        }
        catch (Exception) { return false; }
    }

    private static Bitmap? TryGet(string key)
    {
        lock (Gate)
        {
            if (!Images.TryGetValue(key, out var weak) || !weak.TryGetTarget(out var bitmap)) return null;
            Store(key, bitmap);
            return bitmap;
        }
    }

    private static void RemoveRecent(string key)
    {
        for (var node = Recent.First; node is not null; node = node.Next)
            if (node.Value.Key == key) { Recent.Remove(node); break; }
    }

    private static void Store(string key, Bitmap bitmap)
    {
        lock (Gate)
        {
            Images[key] = new WeakReference<Bitmap>(bitmap);
            RemoveRecent(key);
            Recent.AddFirst((key, bitmap));
            while (Recent.Count > MemoryLimit) Recent.RemoveLast();
            if (Images.Count > 512)
                foreach (var dead in Images.Where(p => !p.Value.TryGetTarget(out _)).Select(p => p.Key).ToArray()) Images.Remove(dead);
            // Evicted bitmaps can still be held by views. Never dispose them here.
        }
    }

    private static bool IsBundledVanilla(string reference) =>
        reference.Equals("Vanilla.png", StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(reference, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.EndsWith("/Vanilla.png", StringComparison.OrdinalIgnoreCase));

    private static Bitmap? LoadBundled(string reference)
    {
        lock (Gate)
        {
            if (TryGet(reference) is { } cached) return cached;
            try
            {
                var name = IsBundledVanilla(reference) ? "Vanilla.png" : reference.Replace('\\', '/').Split('/')[^1];
                using var stream = AssetLoader.Open(new Uri($"avares://SUSModder/Assets/{name}"));
                var bitmap = Bitmap.DecodeToWidth(stream, 128);
                Store(reference, bitmap);
                return bitmap;
            }
            catch (Exception) { return null; }
        }
    }

    private static void Notify(string key) => Dispatcher.UIThread.Post(() => IconAvailable?.Invoke(key));

    public static void ClearCache()
    {
        lock (Gate) { Images.Clear(); Recent.Clear(); }
        // Existing Image.Source references remain valid until collected with their views.
    }

    public static void Shutdown()
    {
        ShutdownToken.Cancel();
        _ = Assets.ContinueWith(task => { if (task.IsCompletedSuccessfully) task.Result.Dispose(); }, TaskScheduler.Default);
        ClearCache();
    }
}

/// <summary>
/// UI-independent retry coordinator. Only Register changes wanted data; queued retries capture and
/// validate that generation before requesting/publishing. Null is retained as a deletion tombstone.
/// Queue completes after the first attempt; failed non-null requests retry in the background at most
/// once per minute, four at a time, until satisfied, superseded or application shutdown.
/// </summary>
public sealed class VersionedIconRetryState
{
    public sealed record Request(string Key, CatalogIconDto? Reference, long Generation);
    private sealed class Assignment(Request request, CancellationTokenSource cancellation)
    {
        public Request Request { get; } = request;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? Worker;
        public TaskCompletionSource FirstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset NextAttempt;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Assignment> _wanted = new();
    private readonly SemaphoreSlim _slots = new(4);
    private readonly Func<Request, CancellationToken, Task<bool>> _attempt;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationToken _lifetimeToken;

    public VersionedIconRetryState(Func<Request, CancellationToken, Task<bool>> attempt,
        TimeProvider? timeProvider = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken lifetimeToken = default)
    {
        _attempt = attempt;
        _clock = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, _clock, token));
        _lifetimeToken = lifetimeToken;
    }

    public Request Register(string key, CatalogIconDto? reference)
    {
        Assignment? previous;
        Request request;
        lock (_gate)
        {
            _wanted.TryGetValue(key, out previous);
            request = new Request(key, reference, (previous?.Request.Generation ?? 0) + 1);
            _wanted[key] = new Assignment(request, CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken));
        }
        previous?.Cancellation.Cancel();
        return request;
    }

    public bool IsCurrent(Request request)
    {
        lock (_gate) return IsCurrentLocked(request);
    }

    private bool IsCurrentLocked(Request request) => _wanted.TryGetValue(request.Key, out var entry)
        && entry.Request.Generation == request.Generation && !entry.Cancellation.IsCancellationRequested;

    public bool ApplyIfCurrent(Request request, Action apply)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(request)) return false;
            apply();
            return true;
        }
    }

    public Task<string?>? StartIfCurrent(Request request, Func<Task<string?>> start)
    {
        lock (_gate) return IsCurrentLocked(request) ? start() : null;
    }

    public Task QueueMissing(string key)
    {
        lock (_gate)
            return _wanted.TryGetValue(key, out var entry) && entry.Request.Reference is not null
                ? Queue(entry.Request) : Task.CompletedTask;
    }

    public Task Queue(Request request)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(request)) return Task.CompletedTask;
            var entry = _wanted[request.Key];
            if (entry.Worker is null)
            {
                entry.FirstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var completion = entry.FirstAttempt;
                entry.Worker = Task.Run(() => RunAsync(entry, completion));
            }
            return entry.FirstAttempt.Task;
        }
    }

    private async Task RunAsync(Assignment entry, TaskCompletionSource completion)
    {
        var token = entry.Cancellation.Token;
        try
        {
            while (IsCurrent(entry.Request))
            {
                var remaining = entry.NextAttempt - _clock.GetUtcNow();
                if (remaining > TimeSpan.Zero) await _delay(remaining, token).ConfigureAwait(false);
                await _slots.WaitAsync(token).ConfigureAwait(false);
                var loaded = false;
                try
                {
                    if (!IsCurrent(entry.Request)) return;
                    loaded = await _attempt(entry.Request, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception) { System.Diagnostics.Debug.WriteLine("ICON_FETCH_FAILED"); }
                finally { _slots.Release(); }
                entry.NextAttempt = loaded ? DateTimeOffset.MinValue : _clock.GetUtcNow().AddMinutes(1);
                completion.TrySetResult();
                if (loaded || entry.Request.Reference is null) return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            lock (_gate) entry.Worker = null;
            completion.TrySetResult();
        }
    }
}

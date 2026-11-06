using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public sealed class IconAssetCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SUSModder-icon-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aFEkAAAAASUVORK5CYII=");
    private static CatalogIconDto Reference(byte[]? bytes = null, string url = "https://susmodder.app/assets/icons/test.png") => new()
    {
        AssetId = "icon_test", Url = url, ContentType = "image/png", SizeBytes = (bytes ?? Png).Length,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes ?? Png))
    };

    [Fact]
    public void AdditiveContractKeepsLegacyAndVersionedFieldsIndependent()
    {
        var legacy = JsonSerializer.Deserialize<CatalogItemDto>("""{"id":1,"iconUrl":"/icons/old.png"}""")!;
        Assert.Null(legacy.Icon);
        Assert.Equal("/icons/old.png", legacy.IconUrl);
        var json = JsonSerializer.Serialize(new CatalogItemDto { Id = 1, IconUrl = "/icons/old.png", Icon = Reference() });
        var versioned = JsonSerializer.Deserialize<CatalogItemDto>(json)!;
        Assert.Equal(Reference().Sha256, versioned.Icon!.Sha256);
        Assert.Equal("/icons/old.png", versioned.IconUrl);
        var metadata = JsonSerializer.Deserialize<CatalogMetaDto>("""{"iconsSchemaVersion":1,"iconsRevision":"42","icons":{"vanilla":null,"dll":null}}""")!;
        Assert.Equal(1, metadata.IconsSchemaVersion);
        Assert.NotNull(metadata.Icons);
        Assert.Null(metadata.Icons.Vanilla);
        Assert.Null(JsonSerializer.Deserialize<CatalogMetaDto>("{}")!.IconsSchemaVersion);
    }

    [Theory]
    [InlineData("http://susmodder.app/icons/a.png")]
    [InlineData("https://susmodder.app.attacker.test/a.png")]
    [InlineData("https://127.0.0.1/a.png")]
    [InlineData("https://susmodder.app:444/a.png")]
    [InlineData("https://user@susmodder.app/a.png")]
    [InlineData("file:///C:/a.png")]
    public async Task RejectsUnsafeUrlsWithoutHttp(string url)
    {
        var handler = new Handler(_ => Task.FromResult(Ok()));
        using var cache = Cache(handler);
        Assert.Null(await cache.GetAsync("mod:1", Reference(url: url)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HashNamedCacheSurvivesOfflineRestartAndFailedReplacement()
    {
        string? original;
        using (var cache = Cache(new Handler(_ => Task.FromResult(Ok()))))
            original = await cache.GetAsync("mod:1", Reference());
        Assert.NotNull(original);
        Assert.Equal(Reference().Sha256 + ".png", Path.GetFileName(original));
        using var offline = Cache(new Handler(_ => throw new HttpRequestException("offline")));
        var changed = Reference(Png.Concat(new byte[] { 1 }).ToArray());
        Assert.Equal(original, await offline.GetAsync("mod:1", changed));
        Assert.Equal(original, await offline.GetAsync("mod:1", Reference()));
        Assert.Null(await offline.GetAsync("mod:1", null));
        using var restarted = Cache(new Handler(_ => throw new HttpRequestException("offline")));
        Assert.Null(await restarted.GetAsync("mod:1", changed));
    }

    [Fact]
    public async Task DeduplicatesHashAndCallerCancellationDoesNotCancelOtherWaiters()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async _ => { started.SetResult(); await release.Task; return Ok(); });
        using var cache = Cache(handler);
        using var cancel = new CancellationTokenSource();
        var first = cache.GetAsync("mod:1", Reference(), cancellationToken: cancel.Token);
        await started.Task;
        var second = cache.GetAsync("mod:2", Reference());
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.NotNull(await second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RejectsRedirectAndBacksOff()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        { Headers = { Location = new Uri("https://127.0.0.1/a.png") } }));
        using var cache = Cache(handler);
        Assert.Null(await cache.GetAsync("mod:1", Reference()));
        Assert.Null(await cache.GetAsync("mod:1", Reference()));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("dimensions")]
    [InlineData("format")]
    [InlineData("large")]
    [InlineData("decode")]
    public async Task InvalidContentNeverPublishes(string failure)
    {
        var bytes = Png.ToArray();
        if (failure == "dimensions") { bytes[16] = 1; }
        if (failure == "format") bytes[0] = 0;
        if (failure == "large") bytes = new byte[IconAssetCache.MaxBytes + 1];
        var handler = new Handler(_ => Task.FromResult(Ok(bytes)));
        using var cache = new IconAssetCache(_ => failure != "decode", _root, handler);
        var reference = failure == "hash" ? Reference(Png.Concat(new byte[] { 1 }).ToArray()) : Reference(bytes);
        if (failure == "size") reference = new CatalogIconDto { AssetId = "a", Url = reference.Url,
            Sha256 = reference.Sha256, ContentType = "image/png", SizeBytes = 1 };
        Assert.Null(await cache.GetAsync("mod:1", reference));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*.png") : []);
    }

    [Fact]
    public async Task LateOldDownloadCannotReplaceNewAssignment()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = Cache(new Handler(async _ => { started.SetResult(); await release.Task; return Ok(); }));
        var old = cache.GetAsync("mod:1", Reference());
        await started.Task;
        Assert.Null(await cache.GetAsync("mod:1", null));
        release.SetResult();
        Assert.Null(await old);
        using var restarted = Cache(new Handler(_ => throw new HttpRequestException()));
        Assert.Null(await restarted.GetAsync("mod:1", Reference(Png.Concat(new byte[] { 1 }).ToArray())));
    }

    [Fact]
    public async Task DecoderFailureDoesNotDisplaceLastGood()
    {
        string? first;
        using (var cache = Cache(new Handler(_ => Task.FromResult(Ok())))) first = await cache.GetAsync("mod:1", Reference());
        var invalid = Png.Concat(new byte[] { 1 }).ToArray();
        using var changed = new IconAssetCache(bytes => bytes.Length == Png.Length, _root,
            new Handler(_ => Task.FromResult(Ok(invalid))));
        Assert.Equal(first, await changed.GetAsync("mod:1", Reference(invalid)));
    }

    [Fact]
    public async Task EnforcesStreamingLimitWithoutContentLength()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new UnknownLengthContent(new byte[IconAssetCache.MaxBytes + 1]) }));
        using var cache = Cache(handler);
        Assert.Null(await cache.GetAsync("mod:1", null, "https://susmodder.app/icons/legacy.png"));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*.png") : []);
    }

    [Fact]
    public async Task LimitsConcurrencyAndCancelsOnShutdown()
    {
        var fourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var handler = new Handler(async token =>
        {
            if (Interlocked.Increment(ref active) == 4) fourStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Ok();
        });
        using var cache = Cache(handler);
        var requests = Enumerable.Range(0, 10).Select(i => cache.GetAsync("mod:" + i, null,
            "https://susmodder.app/icons/" + i + ".png")).ToArray();
        await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, handler.Calls);
        cache.Dispose();
        await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task CorruptDiskFileIsRejectedAndRepaired()
    {
        string? path;
        using (var first = Cache(new Handler(_ => Task.FromResult(Ok())))) path = await first.GetAsync("mod:1", Reference());
        await File.WriteAllBytesAsync(path!, new byte[] { 0 });
        var handler = new Handler(_ => Task.FromResult(Ok()));
        using var cache = Cache(handler);
        Assert.Equal(path, await cache.GetAsync("mod:1", Reference()));
        Assert.Equal(Png, await File.ReadAllBytesAsync(path!));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    [Fact]
    public void ProductionHandlerDisablesCookiesAndRedirects()
    {
        using var handler = IconAssetCache.CreateHttpHandler();
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void MemoryHitRefreshScheduleAdmitsOnlyOneCheckPerMinute()
    {
        var clock = new ManualClock();
        var schedule = new LegacyIconRefreshSchedule(clock);
        var admitted = 0;
        Parallel.For(0, 50, _ => { if (schedule.TryBegin("cached-url")) Interlocked.Increment(ref admitted); });
        Assert.Equal(1, admitted);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(schedule.TryBegin("cached-url"));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(schedule.TryBegin("cached-url"));
        clock.Advance(TimeSpan.FromSeconds(30));
        schedule.Defer("cached-url");
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(schedule.TryBegin("cached-url"));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(schedule.TryBegin("cached-url"));
    }

    [Fact]
    public async Task MutableLegacyUrlRefreshesAfter24HoursWithoutRestart()
    {
        var clock = new ManualClock();
        var requests = 0;
        var changed = Png.Concat(new byte[] { 1 }).ToArray();
        var handler = new Handler(_ => Task.FromResult(Ok(Interlocked.Increment(ref requests) == 1 ? Png : changed)));
        using var cache = new IconAssetCache(_ => true, _root, handler, clock);
        const string url = "https://susmodder.app/icons/mutable.png";
        var original = await cache.GetAsync("legacy:" + url, null, url);
        clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(original, await cache.GetAsync("legacy:" + url, null, url));
        Assert.Equal(1, handler.Calls);
        clock.Advance(TimeSpan.FromHours(1));
        var updated = await cache.GetAsync("legacy:" + url, null, url);
        Assert.NotNull(updated);
        Assert.NotEqual(original, updated);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task FailedRefreshRetriesAfterBackoffAndKeepsLastGood()
    {
        var clock = new ManualClock();
        var calls = 0;
        var handler = new Handler(_ => Task.FromResult(Interlocked.Increment(ref calls) == 2
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok()));
        using var cache = new IconAssetCache(_ => true, _root, handler, clock);
        const string url = "https://susmodder.app/icons/mutable.png";
        var original = await cache.GetAsync("legacy:" + url, null, url);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(original, await cache.GetAsync("legacy:" + url, null, url));
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(original, await cache.GetAsync("legacy:" + url, null, url));
        Assert.Equal(2, handler.Calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(original, await cache.GetAsync("legacy:" + url, null, url));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task ReconciledObsoleteLegacyFileIsEvictableButActiveAndVersionedRemainProtected()
    {
        var clock = new ManualClock();
        var served = Png;
        using var cache = new IconAssetCache(_ => true, _root, new Handler(_ => Task.FromResult(Ok(served))),
            clock, cacheBudgetBytes: 3 * (Png.Length + 3));
        var stable = await cache.GetAsync("mod:1", Reference());
        const string obsoleteUrl = "https://susmodder.app/icons/obsolete.png";
        const string activeUrl = "https://susmodder.app/icons/active.png";
        const string nextUrl = "https://susmodder.app/icons/next.png";
        served = Png.Concat(new byte[] { 1 }).ToArray();
        var obsolete = await cache.GetAsync("legacy:" + obsoleteUrl, null, obsoleteUrl);
        served = Png.Concat(new byte[] { 1, 2 }).ToArray();
        var active = await cache.GetAsync("legacy:" + activeUrl, null, activeUrl);
        File.SetLastWriteTimeUtc(obsolete!, clock.GetUtcNow().UtcDateTime.AddMinutes(-2));
        await cache.ReconcileLegacyAssignmentsAsync([activeUrl, nextUrl]);
        // Reconciliation removes only protection, not a bitmap's disk file immediately.
        Assert.True(File.Exists(obsolete));
        served = Png.Concat(new byte[] { 1, 2, 3 }).ToArray();
        Assert.NotNull(await cache.GetAsync("legacy:" + nextUrl, null, nextUrl));
        Assert.False(File.Exists(obsolete));
        Assert.True(File.Exists(active));
        Assert.True(File.Exists(stable));
        using var offline = new IconAssetCache(_ => true, _root, new Handler(_ => throw new HttpRequestException()), clock);
        Assert.Equal(stable, await offline.GetAsync("mod:1", Reference(Png.Concat(new byte[] { 5 }).ToArray())));
        Assert.Equal(active, await offline.GetAsync("legacy:" + activeUrl, null, activeUrl));
    }

    [Fact]
    public async Task ReconciliationPreventsLateDownloadFromRestoringObsoleteLegacyAssignment()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = Cache(new Handler(async _ => { started.SetResult(); await release.Task; return Ok(); }));
        const string url = "https://susmodder.app/icons/obsolete.png";
        var pending = cache.GetAsync("legacy:" + url, null, url);
        await started.Task;
        await cache.ReconcileLegacyAssignmentsAsync([]);
        release.SetResult();
        Assert.Null(await pending);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "manifest.json")));
        Assert.False(document.RootElement.TryGetProperty("legacy:" + url, out _));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private IconAssetCache Cache(Handler handler) => new(_ => true, _root, handler);
    private static HttpResponseMessage Ok(byte[]? bytes = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? Png) };
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return respond(cancellationToken); }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

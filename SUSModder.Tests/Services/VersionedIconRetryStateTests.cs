using SUSModder.Core.Api.Models;
using SUSModder.Services;
using Xunit;

namespace SUSModder.Tests.Services;

public sealed class VersionedIconRetryStateTests
{
    private static CatalogIconDto Icon(string version) => new() { AssetId = version, Url = "https://susmodder.app/" + version + ".png" };

    [Fact]
    public async Task FailureRetriesUnchangedWantedReferenceAfterClockBackoffWithoutBlockingInitialPreload()
    {
        using var shutdown = new CancellationTokenSource();
        var clock = new Clock();
        var delayStarted = Signal();
        var releaseDelay = Signal();
        var succeeded = Signal();
        var calls = 0;
        var expected = Icon("v1");
        var state = new VersionedIconRetryState((request, _) =>
        {
            Assert.Same(expected, request.Reference);
            if (Interlocked.Increment(ref calls) == 1) return Task.FromResult(false);
            succeeded.SetResult();
            return Task.FromResult(true);
        }, clock, async (duration, token) =>
        {
            Assert.Equal(TimeSpan.FromMinutes(1), duration);
            delayStarted.SetResult();
            await releaseDelay.Task.WaitAsync(token);
        }, shutdown.Token);

        var wanted = state.Register("mod:1", expected);
        await state.Queue(wanted).WaitAsync(TimeSpan.FromSeconds(5));
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Includes the initial-memory-miss overlap and repeated converter reads during backoff.
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => state.QueueMissing("mod:1")));
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        releaseDelay.SetResult();
        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.True(state.IsCurrent(wanted));
        shutdown.Cancel();
    }

    [Fact]
    public async Task RemovalTombstoneStopsDelayedRetriesAndNeverFetchesOnMemoryMiss()
    {
        using var shutdown = new CancellationTokenSource();
        var delayed = Signal();
        var oldCalls = 0;
        var removals = 0;
        var state = new VersionedIconRetryState((request, _) =>
        {
            if (request.Reference is null) { Interlocked.Increment(ref removals); return Task.FromResult(true); }
            Interlocked.Increment(ref oldCalls);
            return Task.FromResult(false);
        }, delay: async (_, token) =>
        {
            delayed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, lifetimeToken: shutdown.Token);
        var old = state.Register("global:vanilla", Icon("old"));
        await state.Queue(old);
        await delayed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var removed = state.Register("global:vanilla", null);
        await state.Queue(removed);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => state.QueueMissing("global:vanilla")));
        // An already-queued old retry cannot request or apply its captured reference after removal.
        var started = false;
        Assert.Null(state.StartIfCurrent(old, () => { started = true; return Task.FromResult<string?>("old.png"); }));
        Assert.False(state.ApplyIfCurrent(old, () => started = true));
        await state.Queue(old);
        Assert.False(started);
        Assert.Equal(1, oldCalls);
        Assert.Equal(1, removals);
        Assert.True(state.IsCurrent(removed));
        shutdown.Cancel();
    }

    [Fact]
    public async Task QueuedOldGenerationCannotReregisterOverNewWantedReference()
    {
        using var shutdown = new CancellationTokenSource();
        var release = Signal();
        var fourStarted = Signal();
        var newLoaded = Signal();
        var blockers = 0;
        var oldCalls = 0;
        var current = Icon("new");
        var state = new VersionedIconRetryState(async (request, token) =>
        {
            if (request.Key.StartsWith("block:"))
            {
                if (Interlocked.Increment(ref blockers) == 4) fourStarted.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            else if (request.Reference?.AssetId == "old") Interlocked.Increment(ref oldCalls);
            else { Assert.Same(current, request.Reference); newLoaded.TrySetResult(); }
            return true;
        }, lifetimeToken: shutdown.Token);
        var initial = Enumerable.Range(0, 4).Select(i => state.Queue(state.Register("block:" + i, Icon("block")))).ToArray();
        await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = state.Register("mod:1", Icon("old"));
        var oldAttempt = state.Queue(old);
        var newer = state.Register("mod:1", current);
        var newAttempt = state.Queue(newer);
        Assert.False(state.ApplyIfCurrent(old, () => throw new Exception("stale decode published")));
        release.SetResult();
        await Task.WhenAll(initial.Append(oldAttempt).Append(newAttempt)).WaitAsync(TimeSpan.FromSeconds(5));
        await newLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, oldCalls);
        Assert.True(state.IsCurrent(newer));
        Assert.False(state.IsCurrent(old));
        shutdown.Cancel();
    }

    [Fact]
    public async Task UnknownOrDeletedAssignmentDoesNotStartRetryWorker()
    {
        var calls = 0;
        var state = new VersionedIconRetryState((_, _) => { Interlocked.Increment(ref calls); return Task.FromResult(true); });
        await state.QueueMissing("unknown");
        state.Register("mod:1", null);
        await state.QueueMissing("mod:1");
        Assert.Equal(0, calls);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Clock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}

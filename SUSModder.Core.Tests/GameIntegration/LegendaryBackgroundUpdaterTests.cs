using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.GameIntegration;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

/// <summary>
/// Aktualizacja legendary.exe w tle po starcie aplikacji (tylko tryb Epic).
/// </summary>
public sealed class LegendaryBackgroundUpdaterTests
{
    public LegendaryBackgroundUpdaterTests()
    {
        LegendaryBackgroundUpdater.ResetForTests();
        LegendaryBackgroundUpdater.ModeOverrideForTests(() => "epic");
    }

    [Fact]
    public async Task Schedule_SteamMode_DoesNothing()
    {
        LegendaryBackgroundUpdater.ModeOverrideForTests(() => "steam");

        var provider = new RecordingProvider();

        LegendaryBackgroundUpdater.Schedule("steam", provider);
        await Task.Delay(150);

        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Schedule_EpicMode_RefreshesInBackground()
    {
        var provider = new RecordingProvider();

        LegendaryBackgroundUpdater.Schedule("epic", provider);

        // Zadanie jest asynchroniczne — czekamy na wykonanie.
        for (int i = 0; i < 50 && provider.Calls == 0; i++)
            await Task.Delay(20);

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Schedule_DoesNotThrowWhenProviderFails()
    {
        var provider = new RecordingProvider { ThrowOnEnsure = true };

        LegendaryBackgroundUpdater.Schedule("epic", provider);
        await Task.Delay(150);

        // Wyjątek w tle nie może przerwać działania aplikacji.
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Schedule_NullProvider_DoesNotThrow()
    {
        LegendaryBackgroundUpdater.Schedule("epic", null);
        await Task.Delay(50);
    }

    [Fact]
    public async Task Schedule_WhenModeChangedToSteamBeforeExecution_SkipsUpdate()
    {
        // Tryb zmieniony na Steam PO zaplanowaniu, ale PRZED wykonaniem zadania —
        // aktualizacja nie może się wykonać (to był update Epic mimo wybranego Steam).
        LegendaryBackgroundUpdater.ModeOverrideForTests(() => "steam");

        var provider = new RecordingProvider();

        LegendaryBackgroundUpdater.Schedule("epic", provider);

        for (int i = 0; i < 50 && provider.Calls == 0; i++)
            await Task.Delay(20);

        await Task.Delay(100);
        Assert.Equal(0, provider.Calls);
    }

    private sealed class RecordingProvider : ILegendaryVersionProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool ThrowOnEnsure { get; init; }

        public Task<LegendaryVersionState> GetStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegendaryVersionState(true, null, null, null));

        public Task<IReadOnlyList<LegendaryReleaseInfo>> GetReleasesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegendaryReleaseInfo>>(Array.Empty<LegendaryReleaseInfo>());

        public Task<LegendarySwitchResult> SwitchAsync(
            LegendaryReleaseInfo? release,
            IProgress<string>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(new LegendarySwitchResult(false, LegendaryErrorCodes.Failed));

        public async Task<LegendarySwitchResult> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);

            if (ThrowOnEnsure)
                throw new InvalidOperationException("boom");

            return new LegendarySwitchResult(true);
        }

        public Task<LegendaryToolInspection> InspectInstalledAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegendaryToolInspection(LegendaryToolTrust.Missing, null, false));
    }
}

using SUSModder.Services;
using Xunit;

namespace SUSModder.Tests.Services;

// Kontrakt selektora wersji legendary.exe — tożsamość (Source, Tag, AssetName), mapa kodów błędów.
public sealed class LegendaryVersionContractTests
{
    private static LegendaryReleaseInfo Upstream(string tag) => new(LegendaryReleaseSource.Upstream, tag);
    private static LegendaryReleaseInfo Heroic(string tag) => new(LegendaryReleaseSource.Heroic, tag);

    [Fact]
    public void DisplayName_MakesSourceExplicit()
    {
        Assert.Equal("legendary-gl 0.21.1", Upstream("0.21.1").DisplayName);
        Assert.Equal("Heroic 0.20.43", Heroic("0.20.43").DisplayName);
    }

    [Fact]
    public void SameId_UsesSourceTagAndAsset_NotTagAlone()
    {
        // Ten sam tag z dwóch repozytoriów to dwa różne wydania.
        Assert.False(Upstream("0.21.1").SameId(Heroic("0.21.1")));
        Assert.True(Upstream("0.21.1").SameId(Upstream("0.21.1")));
        Assert.False(Upstream("0.21.1").SameId(null));

        // Ta sama nazwa assetu w innym repozytorium to nadal inne wydanie.
        const string UpstreamAsset = "legendary_windows_x64.exe";
        const string HeroicAsset = "legendary_windows_x86_64.exe";
        Assert.False(
            new LegendaryReleaseInfo(LegendaryReleaseSource.Upstream, "0.21.1", UpstreamAsset)
                .SameId(new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.21.1", HeroicAsset)));

        // PublishedAt nie wchodzi do tożsamości.
        Assert.True(new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset, "", DateTimeOffset.Parse("2026-04-22"))
            .SameId(new LegendaryReleaseInfo(LegendaryReleaseSource.Heroic, "0.20.43", HeroicAsset)));
    }

    [Theory]
    [InlineData(LegendaryErrorCodes.Offline, "Settings.LegendaryVersion.Errors.Offline")]
    [InlineData(LegendaryErrorCodes.GitHubUnavailable, "Settings.LegendaryVersion.Errors.GitHubUnavailable")]
    [InlineData(LegendaryErrorCodes.NoWindowsAsset, "Settings.LegendaryVersion.Errors.NoWindowsAsset")]
    [InlineData(LegendaryErrorCodes.ToolBusy, "Settings.LegendaryVersion.Errors.ToolBusy")]
    [InlineData(LegendaryErrorCodes.HashMismatch, "Settings.LegendaryVersion.Errors.HashMismatch")]
    [InlineData(LegendaryErrorCodes.Failed, "Settings.LegendaryVersion.Errors.Failed")]
    [InlineData("mystery_code", "Settings.LegendaryVersion.Errors.Unknown")]
    [InlineData(null, "Settings.LegendaryVersion.Errors.Unknown")]
    public void MapErrorKey_ReturnsLocaleKey(string? code, string expectedKey) =>
        Assert.Equal(expectedKey, LegendaryErrorCodes.MapErrorKey(code));

    [Theory]
    [InlineData(LegendaryPhaseCodes.Downloading, "Settings.LegendaryVersion.Phases.Downloading")]
    [InlineData(LegendaryPhaseCodes.Verifying, "Settings.LegendaryVersion.Phases.Verifying")]
    [InlineData(LegendaryPhaseCodes.Installing, "Settings.LegendaryVersion.Phases.Installing")]
    [InlineData("pobieranie", null)] // tekst do wyświetlenia nie jest kodem fazy — UI nie pokaże go wprost
    [InlineData("sending progress...", null)]
    [InlineData(null, null)]
    public void MapToKey_UnknownPhaseFallsBackToGeneric(string? code, string? expectedKey) =>
        Assert.Equal(expectedKey, LegendaryPhaseCodes.MapToKey(code));

    [Fact]
    public async Task CoreAdapter_MapsReleasesWithFullIdentity()
    {
        // Adapter musi przenieść tożsamość repo+tag+asset oraz sumę kontrolną.
        var provider = new FakeCoreProvider
        {
            Releases =
            {
                new SUSModder.Core.GameIntegration.LegendaryReleaseInfo(
                    SUSModder.Core.GameIntegration.LegendaryReleaseSource.Upstream,
                    "0.21.1",
                    "legendary_windows_x64.exe",
                    "6be77857dc0a6dd33ea7442ce8b5291944ec6da53757f81ca95b57ce794baf5b")
            }
        };

        var service = new CoreLegendaryVersionService(provider);
        var releases = await service.GetReleasesAsync();

        var release = Assert.Single(releases);
        Assert.Equal(LegendaryReleaseSource.Upstream, release.Source);
        Assert.Equal("0.21.1", release.Tag);
        Assert.Equal("legendary_windows_x64.exe", release.AssetName);
        Assert.Equal(
            "6be77857dc0a6dd33ea7442ce8b5291944ec6da53757f81ca95b57ce794baf5b",
            release.Sha256);
        Assert.Equal("legendary-gl 0.21.1", release.DisplayName);
    }

    [Fact]
    public async Task CoreAdapter_MapsApiFailureToStableErrorCode()
    {
        var provider = new FakeCoreProvider { ThrowOnReleases = true };
        var service = new CoreLegendaryVersionService(provider);

        // Wyjątek Core mapujemy na stabilny kod, nie przepuszczamy go do UI.
        var ex = await Assert.ThrowsAsync<SUSModder.Core.GameIntegration.LegendaryReleaseException>(
            () => service.GetReleasesAsync());
        Assert.Equal(LegendaryErrorCodes.GitHubUnavailable, ex.ErrorCode);
    }

    private sealed class FakeCoreProvider : SUSModder.Core.GameIntegration.ILegendaryVersionProvider
    {
        public List<SUSModder.Core.GameIntegration.LegendaryReleaseInfo> Releases { get; } = new();

        public bool ThrowOnReleases { get; init; }

        public Task<SUSModder.Core.GameIntegration.LegendaryVersionState> GetStateAsync(
            CancellationToken ct = default) =>
            Task.FromResult(new SUSModder.Core.GameIntegration.LegendaryVersionState(true, null, null, null));

        public Task<IReadOnlyList<SUSModder.Core.GameIntegration.LegendaryReleaseInfo>> GetReleasesAsync(
            CancellationToken ct = default)
        {
            if (ThrowOnReleases)
                throw new SUSModder.Core.GameIntegration.LegendaryReleaseException(
                    LegendaryErrorCodes.GitHubUnavailable,
                    "GitHub niedostępny");

            return Task.FromResult<IReadOnlyList<SUSModder.Core.GameIntegration.LegendaryReleaseInfo>>(Releases);
        }

        public Task<SUSModder.Core.GameIntegration.LegendarySwitchResult> SwitchAsync(
            SUSModder.Core.GameIntegration.LegendaryReleaseInfo? release,
            IProgress<string>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(new SUSModder.Core.GameIntegration.LegendarySwitchResult(true));

        public Task<SUSModder.Core.GameIntegration.LegendarySwitchResult> EnsureInstalledAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult(new SUSModder.Core.GameIntegration.LegendarySwitchResult(true));

        public Task<SUSModder.Core.GameIntegration.LegendaryToolInspection> InspectInstalledAsync(
            CancellationToken ct = default) =>
            Task.FromResult(new SUSModder.Core.GameIntegration.LegendaryToolInspection(
                SUSModder.Core.GameIntegration.LegendaryToolTrust.Missing, null, false));
    }
}

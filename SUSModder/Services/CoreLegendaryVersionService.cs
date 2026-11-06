using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreGameIntegration = SUSModder.Core.GameIntegration;

namespace SUSModder.Services;

/// <summary>
/// Adapter warstwy UI na serwis Core (<see cref="SUSModder.Core.GameIntegration.LegendaryVersionService"/>).
/// </summary>
/// <remarks>
/// Core nie zna typów UI, więc mapowanie wyników jest jedynym miejscem, w którym oba światy się stykają.
/// Wyjątek: <see cref="CoreGameIntegration.LegendaryReleaseException"/> jest przekładany na
/// stabilny kod błędu, który UI lokalizuje.
/// </remarks>
public sealed class CoreLegendaryVersionService : ILegendaryVersionService
{
    private readonly CoreGameIntegration.ILegendaryVersionProvider _provider;

    public CoreLegendaryVersionService(CoreGameIntegration.ILegendaryVersionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc/>
    public async Task<LegendaryVersionState> GetStateAsync(CancellationToken ct = default)
    {
        var state = await _provider.GetStateAsync(ct);
        return new LegendaryVersionState(
            state.UsePinned,
            Map(state.Selected),
            Map(state.Pinned),
            Map(state.Installed));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LegendaryReleaseInfo>> GetReleasesAsync(CancellationToken ct = default)
    {
        var releases = await _provider.GetReleasesAsync(ct);
        var result = new List<LegendaryReleaseInfo>(releases.Count);
        foreach (var release in releases)
            result.Add(Map(release)!);
        return result;
    }

    /// <inheritdoc/>
    public async Task<LegendarySwitchResult> SwitchAsync(
        LegendaryReleaseInfo? release,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _provider.SwitchAsync(ToCore(release), progress, ct);
            return new LegendarySwitchResult(result.Success, result.ErrorCode, Map(result.Installed));
        }
        catch (CoreGameIntegration.LegendaryReleaseException ex)
        {
            return new LegendarySwitchResult(false, ex.ErrorCode);
        }
    }

    /// <summary>Mapuje wydanie UI na kontrakt Core (zachowuje tożsamość repo+tag+asset).</summary>
    private static CoreGameIntegration.LegendaryReleaseInfo? ToCore(LegendaryReleaseInfo? release) =>
        release is null
            ? null
            : new CoreGameIntegration.LegendaryReleaseInfo(
                (CoreGameIntegration.LegendaryReleaseSource)(int)release.Source,
                release.Tag,
                release.AssetName,
                release.Sha256,
                release.PublishedAt);

    /// <summary>Mapuje wydanie Core na kontrakt UI (zachowuje tożsamość repo+tag+asset).</summary>
    private static LegendaryReleaseInfo? Map(CoreGameIntegration.LegendaryReleaseInfo? release) =>
        release is null
            ? null
            : new LegendaryReleaseInfo(
                (LegendaryReleaseSource)(int)release.Source,
                release.Tag,
                release.AssetName,
                release.Sha256,
                release.PublishedAt);
}

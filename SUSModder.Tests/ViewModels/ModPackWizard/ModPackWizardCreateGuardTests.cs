using System.Collections.Generic;
using SUSModder.Core.Models;
using SUSModder.ViewModels.ModPackWizard;

namespace SUSModder.Tests.ViewModels.ModPackWizard;

/// <summary>
/// Testy strażnika ponawiania CreatePack: niepewna porażka (sieć/5xx/odpowiedź nie do
/// odczytu) NIE może prowadzić do automatycznego drugiego POST-a bez sprawdzenia listy.
/// </summary>
public class ModPackWizardCreateGuardTests
{
    private static DateTimeOffset Now => new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static ModPackListEntry Entry(
        string code,
        string name,
        int fullModId = 7,
        string version = "1.2",
        bool active = true,
        DateTimeOffset? created = default) => new()
        {
            PackCode = code,
            ModName = name,
            FullModId = fullModId,
            FullModVersion = version,
            Active = active,
            CreatedAt = created ?? Now.AddMinutes(-2)
        };

    [Theory]
    [InlineData("PACK_LIMIT_REACHED")]
    [InlineData("INVALID_CREATOR_HASH")]
    [InlineData("BAD_REQUEST")]
    [InlineData("UNAUTHORIZED")]
    [InlineData("FORBIDDEN")]
    [InlineData("PAYLOAD_TOO_LARGE")]
    public void DefiniteRejections_DoNotRequireProbe(string code)
    {
        Assert.True(ModPackWizardCreateGuard.IsDefiniteRejection(code));
        Assert.False(ModPackWizardCreateGuard.RequiresProbeBeforeRetry(code));
    }

    [Theory]
    [InlineData("NETWORK_ERROR")]
    [InlineData("INVALID_RESPONSE")]
    [InlineData("INTERNAL SERVER ERROR")]
    [InlineData("GATEWAY_TIMEOUT")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOMETHING_UNKNOWN")]
    public void UncertainFailures_RequireProbeBeforeRetry(string? code)
    {
        Assert.False(ModPackWizardCreateGuard.IsDefiniteRejection(code));
        Assert.True(ModPackWizardCreateGuard.RequiresProbeBeforeRetry(code));
    }

    [Fact]
    public void TryMatch_FindsFreshPackByNameIdAndVersion()
    {
        var packs = new List<ModPackListEntry>
        {
            Entry("OLD123", "Moje Miasto", created: Now.AddMinutes(-45)),
            Entry("NEW456", "moje miasto ") // wielkość liter + trim — dane użytkownika bywają brudne
        };

        var match = ModPackWizardCreateGuard.TryMatchExistingPack(packs, "Moje Miasto", 7, "1.2", Now);
        Assert.NotNull(match);
        Assert.Equal("NEW456", match!.PackCode);
    }

    [Fact]
    public void TryMatch_RejectsStaleInactiveAndDifferentIdentity()
    {
        var packs = new List<ModPackListEntry>
        {
            Entry("STALE1", "X", created: Now.AddMinutes(-90)),
            Entry("GONE01", "X", active: false),
            Entry("OTHER1", "X", fullModId: 8),
            Entry("VER001", "X", version: "9.9"),
            new ModPackListEntry { PackCode = "NOCREA", ModName = "X", FullModId = 7, FullModVersion = "1.2", Active = true, CreatedAt = null }
        };

        Assert.Null(ModPackWizardCreateGuard.TryMatchExistingPack(packs, "X", 7, "1.2", Now));
    }

    [Fact]
    public void TryMatch_LatestVersionValue_MatchesAnyServerVersion()
    {
        var packs = new List<ModPackListEntry> { Entry("LAT001", "Zestaw A", version: "1.2") };
        var match = ModPackWizardCreateGuard.TryMatchExistingPack(packs, "Zestaw A", 7, "latest", Now);
        Assert.NotNull(match);
    }

    [Fact]
    public void TryMatch_CustomFullIdZero_MatchesByNameAndVersionOnly()
    {
        var packs = new List<ModPackListEntry>
        {
            Entry("CUST01", "Custom Mod", fullModId: 7, version: "custom"),
            Entry("CUST02", "Custom Mod", fullModId: 0, version: "custom")
        };
        var match = ModPackWizardCreateGuard.TryMatchExistingPack(packs, "Custom Mod", 0, "custom", Now);
        // fullModId=0 → nie filtrujemy po id, ale nowszy wpis (CUST02 ustawiony drugi, ten sam czas)
        Assert.NotNull(match);
    }

    [Fact]
    public void TryMatch_NoNameOrEmptyList_ReturnsNull()
    {
        Assert.Null(ModPackWizardCreateGuard.TryMatchExistingPack(new List<ModPackListEntry>(), "X", 7, "1.2", Now));
        Assert.Null(ModPackWizardCreateGuard.TryMatchExistingPack(
            new List<ModPackListEntry> { Entry("A", "Inna nazwa") }, "X", 7, "1.2", Now));
    }

    [Fact]
    public void InconclusiveProbe_Detected()
    {
        Assert.True(ModPackWizardCreateGuard.IsProbeInconclusive(null));
        Assert.True(ModPackWizardCreateGuard.IsProbeInconclusive(new ModPackListResult { Success = false }));
        Assert.False(ModPackWizardCreateGuard.IsProbeInconclusive(new ModPackListResult { Success = true }));
    }
}

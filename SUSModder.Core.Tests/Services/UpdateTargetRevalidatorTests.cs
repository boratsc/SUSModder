using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Testy rewalidacji zatwierdzonego celu aktualizacji (3.1-06).
/// Zatwierdzona wersja nie może się po cichu zmienić.
/// </summary>
public sealed class UpdateTargetRevalidatorTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenVersionAndGameVersionMatch()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var target = CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1");

        var result = UpdateTargetRevalidator.Validate(preview, target);

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal("update_preview.target_valid", result.ErrorCode);
    }

    [Fact]
    public void Validate_ReturnsTargetChanged_WhenVersionDiffers()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var target = CreateTarget(10, "Town of Us", "2.1.0", "2025.6.1");

        var result = UpdateTargetRevalidator.Validate(preview, target);

        Assert.Equal(UpdateTargetValidationStatus.TargetChanged, result.Status);
        Assert.True(result.RequiresConfirmation);
        Assert.Equal("2.0.0", result.ApprovedVersion);
        Assert.Equal("2.1.0", result.AvailableVersion);
        Assert.Equal("update_preview.target_changed", result.ErrorCode);
    }

    [Fact]
    public void Validate_ReturnsTargetChanged_WhenGameVersionDiffers()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var target = CreateTarget(10, "Town of Us", "2.0.0", "2025.9.28");

        var result = UpdateTargetRevalidator.Validate(preview, target);

        Assert.Equal(UpdateTargetValidationStatus.TargetChanged, result.Status);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public void Validate_IgnoresEmptyGameVersionFromCatalog()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var target = CreateTarget(10, "Town of Us", "2.0.0", string.Empty);

        var result = UpdateTargetRevalidator.Validate(preview, target);

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
    }

    [Fact]
    public void Validate_ReturnsTargetMissing_WhenTargetIsAbsent()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");

        var result = UpdateTargetRevalidator.Validate(preview, null);

        Assert.Equal(UpdateTargetValidationStatus.TargetMissing, result.Status);
        Assert.True(result.RequiresConfirmation);
        Assert.Equal("update_preview.target_missing", result.ErrorCode);
    }

    [Fact]
    public async Task RevalidateAsync_ReturnsValid_WhenCatalogStillOffersApprovedVersion()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(preview, () => Task.FromResult<List<ModConfiguration>?>(catalog));

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task RevalidateAsync_ReturnsTargetChanged_WhenCatalogOffersNewerVersion()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.1.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(preview, () => Task.FromResult<List<ModConfiguration>?>(catalog));

        Assert.Equal(UpdateTargetValidationStatus.TargetChanged, result.Status);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public async Task RevalidateAsync_ReturnsTargetMissing_WhenModIsNotInCatalog()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(11, "Other Mod", "1.0.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(preview, () => Task.FromResult<List<ModConfiguration>?>(catalog));

        Assert.Equal(UpdateTargetValidationStatus.TargetMissing, result.Status);
    }

    [Fact]
    public async Task RevalidateAsync_ReturnsCatalogUnavailable_WhenCatalogCannotBeLoaded()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");

        var result = await UpdateTargetRevalidator.RevalidateAsync(preview, () => Task.FromResult<List<ModConfiguration>?>(null));

        Assert.Equal(UpdateTargetValidationStatus.CatalogUnavailable, result.Status);
        Assert.True(result.RequiresConfirmation);
        Assert.Equal("update_preview.catalog_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task RevalidateAsync_ReturnsCatalogUnavailable_WhenCatalogThrows()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");

        var result = await UpdateTargetRevalidator.RevalidateAsync(
            preview,
            () => throw new InvalidOperationException("offline"));

        Assert.Equal(UpdateTargetValidationStatus.CatalogUnavailable, result.Status);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public async Task RevalidateAsync_FallsBackToName_WhenModIdIsUnknown()
    {
        var preview = CreatePreview("2.0.0", "2025.6.1");
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(0, "Town of Us", "2.0.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(preview, () => Task.FromResult<List<ModConfiguration>?>(catalog));

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task RevalidateAsync_ByVersion_ReturnsValid_WhenSingleCatalogEntryMatches()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(
            "2.0.0",
            "2025.6.1",
            () => Task.FromResult<List<ModConfiguration>?>(catalog));

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
        Assert.False(result.RequiresConfirmation);
    }

    [Fact]
    public async Task RevalidateAsync_ByVersion_ReturnsTargetMissing_WhenVersionIsNotInCatalog()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.1.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(
            "2.0.0",
            "2025.6.1",
            () => Task.FromResult<List<ModConfiguration>?>(catalog));

        Assert.Equal(UpdateTargetValidationStatus.TargetMissing, result.Status);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public void Validate_ByVersion_ReturnsTargetChanged_WhenGameVersionDiffers()
    {
        var target = CreateTarget(10, "Town of Us", "2.0.0", "2025.9.28");

        var result = UpdateTargetRevalidator.Validate("2.0.0", "2025.6.1", target);

        Assert.Equal(UpdateTargetValidationStatus.TargetChanged, result.Status);
        Assert.Equal("2.0.0", result.ApprovedVersion);
        Assert.Equal("2025.9.28", result.AvailableAmongVersion);
    }

    [Fact]
    public async Task RevalidateAsync_ByModId_MatchesTheRightMod_WhenVersionsAreShared()
    {
        // Dwa mody o tej samej wersji — identyfikacja po Id, nie po wersji.
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1"),
            CreateTarget(11, "Other Mod", "2.0.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(
            () => Task.FromResult<List<ModConfiguration>?>(catalog),
            11,
            "Other Mod",
            "2.0.0",
            "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task RevalidateAsync_ByModId_DetectsDrift_ForTheRightMod()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1"),
            CreateTarget(11, "Other Mod", "2.1.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(
            () => Task.FromResult<List<ModConfiguration>?>(catalog),
            11,
            "Other Mod",
            "2.0.0",
            "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.TargetChanged, result.Status);
        Assert.Equal("2.1.0", result.AvailableVersion);
    }

    [Fact]
    public async Task RevalidateAsync_ByModId_ReturnsTargetMissing_WhenModIsGone()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1")
        };

        var result = await UpdateTargetRevalidator.RevalidateAsync(
            () => Task.FromResult<List<ModConfiguration>?>(catalog),
            11,
            "Other Mod",
            "2.0.0",
            "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.TargetMissing, result.Status);
    }

    [Fact]
    public void ValidateAgainstCatalog_ReturnsValid_WhenPairMatches()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1")
        };

        var result = UpdateTargetRevalidator.ValidateAgainstCatalog(
            catalog, 10, "Town of Us", "2.0.0", "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.Valid, result.Status);
    }

    [Fact]
    public void ValidateAgainstCatalog_ReturnsTargetChanged_WhenVersionDiffers()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.1.0", "2025.6.1")
        };

        var result = UpdateTargetRevalidator.ValidateAgainstCatalog(
            catalog, 10, "Town of Us", "2.0.0", "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.TargetChanged, result.Status);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public void ValidateAgainstCatalog_ReturnsCatalogUnavailable_WhenCatalogIsMissing()
    {
        var result = UpdateTargetRevalidator.ValidateAgainstCatalog(
            null, 10, "Town of Us", "2.0.0", "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.CatalogUnavailable, result.Status);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public void ValidateAgainstCatalog_ReturnsTargetMissing_WhenModIsNotInCatalog()
    {
        var catalog = new List<ModConfiguration>
        {
            CreateTarget(10, "Town of Us", "2.0.0", "2025.6.1")
        };

        var result = UpdateTargetRevalidator.ValidateAgainstCatalog(
            catalog, 11, "Other Mod", "2.0.0", "2025.6.1");

        Assert.Equal(UpdateTargetValidationStatus.TargetMissing, result.Status);
    }

    [Theory]
    [InlineData(UpdateTargetValidationStatus.Valid, UpdateTargetApproval.Decision.Approved)]
    [InlineData(UpdateTargetValidationStatus.TargetChanged, UpdateTargetApproval.Decision.RequiresConfirmation)]
    [InlineData(UpdateTargetValidationStatus.TargetMissing, UpdateTargetApproval.Decision.Aborted)]
    [InlineData(UpdateTargetValidationStatus.CatalogUnavailable, UpdateTargetApproval.Decision.Aborted)]
    public void Decide_MapsValidationStatus_ToOperationalDecision(
        UpdateTargetValidationStatus status,
        UpdateTargetApproval.Decision expected)
    {
        var validation = new UpdateTargetValidationResult { Status = status };

        Assert.Equal(expected, UpdateTargetApproval.Decide(validation));
    }

    [Fact]
    public void Decide_TreatsMissingValidationAsAborted()
    {
        Assert.Equal(UpdateTargetApproval.Decision.Aborted, UpdateTargetApproval.Decide(null));
    }

    private static UpdatePreview CreatePreview(string targetVersion, string targetAmongVersion) => new()
    {
        ModId = 10,
        ModName = "Town of Us",
        Platform = "steam",
        CurrentVersion = "1.0.0",
        TargetVersion = targetVersion,
        TargetAmongVersion = targetAmongVersion,
        IsInstalled = true,
        HasInstallationMap = true,
        ConfigStatus = UpdateConfigStatus.NotGuaranteed,
        TargetKey = $"mod:10|platform:steam|version:{targetVersion}|among:{targetAmongVersion}"
    };

    private static ModConfiguration CreateTarget(int id, string name, string version, string amongVersion) => new()
    {
        Id = id,
        ModName = name,
        ModType = "full",
        ModVersion = version,
        AmongVersion = amongVersion
    };
}

using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

public class UpdatePreviewStableTargetTests
{
    [Fact]
    public void Refresh_UsesStableReleaseForSummaryAndValidation()
    {
        var entry = new ModConfiguration
        {
            Id = 13, ModName = "Town of Us Mira", ModVersion = "1.7.9", IsBeta = true,
            LatestStableVersion = "1.7.5",
            AvailableReleases = [new ModRelease { Version = "1.7.5", AmongVersion = "2026-8-18" }]
        };
        var original = new ModUpdateInfo
        {
            ModName = entry.ModName, CurrentVersion = "1.7.3", NewVersion = "1.7.5",
            LocalMod = new ModConfiguration { Id = 13 }
        };

        var refreshed = MainWindowViewModel.RefreshUpdateInfoFromCatalog(original, [entry]);

        Assert.NotNull(refreshed);
        Assert.Equal("1.7.5", refreshed.NewVersion);
        Assert.Equal("2026-8-18", refreshed.RemoteMod.AmongVersion);
        Assert.Equal(UpdateTargetValidationStatus.Valid,
            MainWindowViewModel.ValidateApprovedTargetAgainstCatalog([entry], refreshed, refreshed.NewVersion, refreshed.RemoteMod.AmongVersion).Status);
        Assert.Equal("1.7.9", entry.ModVersion);
    }

    [Fact]
    public void Refresh_RejectsUnconfirmedStableRelease()
    {
        var entry = new ModConfiguration { Id = 13, IsBeta = true, ModVersion = "1.7.9" };
        var original = new ModUpdateInfo { LocalMod = new ModConfiguration { Id = 13 } };
        Assert.Null(MainWindowViewModel.RefreshUpdateInfoFromCatalog(original, [entry]));
    }
}

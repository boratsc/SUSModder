using SUSModder.Core.Models;
using SUSModder.Core.Services;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class CompatibilityMergerTests
{
    [Fact]
    public void PickBestFromEntries_PrefersCurrentVersion_W_Over_Stale_F()
    {
        var entries = new[]
        {
            new CompatibilityEntry
            {
                Id = 1,
                Status = "F",
                IsCurrentVersion = false,
                TestedDate = "2025-01-01",
                DllMod = new CompatibilityModInfo { Id = 5, Name = "Dll" }
            },
            new CompatibilityEntry
            {
                Id = 2,
                Status = "W",
                IsCurrentVersion = true,
                TestedDate = "2025-06-01",
                DllMod = new CompatibilityModInfo { Id = 5, Name = "Dll" }
            }
        };

        var picked = CompatibilityMerger.PickBestFromEntries(entries);

        Assert.NotNull(picked);
        Assert.Equal("W", picked.StatusCode);
        Assert.Equal(CompatibilityStatus.Works, picked.Status);
    }

    [Fact]
    public void PickBestFromEntries_IgnoresStale_F_When_No_Current_Entry()
    {
        var entries = new[]
        {
            new CompatibilityEntry
            {
                Id = 1,
                Status = "F",
                IsCurrentVersion = false,
                DllMod = new CompatibilityModInfo { Id = 5, Name = "Dll" }
            },
            new CompatibilityEntry
            {
                Id = 2,
                Status = "W",
                IsCurrentVersion = false,
                DllMod = new CompatibilityModInfo { Id = 5, Name = "Dll" }
            }
        };

        var picked = CompatibilityMerger.PickBestFromEntries(entries);

        Assert.Null(picked);
    }

    [Fact]
    public void PickBestFromEntries_Does_Not_Upgrade_W_To_F_From_Stale_Rows()
    {
        var entries = new[]
        {
            new CompatibilityEntry
            {
                Id = 1,
                Status = "W",
                IsCurrentVersion = true,
                TestedDate = "2025-06-01",
                DllMod = new CompatibilityModInfo { Id = 5, Name = "Dll" }
            },
            new CompatibilityEntry
            {
                Id = 2,
                Status = "F",
                IsCurrentVersion = false,
                TestedDate = "2025-01-01",
                DllMod = new CompatibilityModInfo { Id = 5, Name = "Dll" }
            }
        };

        var picked = CompatibilityMerger.PickBestFromEntries(entries);

        Assert.NotNull(picked);
        Assert.Equal("W", picked.StatusCode);
    }

    [Theory]
    [InlineData("F", CompatibilityStatus.Favorite)]
    [InlineData("W", CompatibilityStatus.Works)]
    [InlineData("NT", CompatibilityStatus.NotTested)]
    [InlineData(" works ", CompatibilityStatus.Works)]
    public void FromApiCode_Parses_Status_Codes(string code, CompatibilityStatus expected)
    {
        var info = CompatibilityMerger.FromEntry(new CompatibilityEntry { Status = code, IsCurrentVersion = true });
        Assert.Equal(expected, info.Status);
    }
}

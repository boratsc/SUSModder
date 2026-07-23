using System.Text.Json;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModPackInstallerCustomFullAmongVersionTests
{
    [Fact]
    public void TryBuildCustomFullModConfig_UsesAmongVersionFromArtifact()
    {
        var pack = new ModPack { PackCode = "TEST-CODE-1234" };
        var artifact = new ModPackCustomArtifact
        {
            DisplayName = "Custom ToU",
            FileName = "overlay.zip",
            Version = "1.0.0",
            AmongVersion = "2024.6.18",
            DownloadUrl = "https://cdn.example/overlay.zip",
            SourceKind = "github_full",
            ModType = "full",
            Status = "clean"
        };

        var ok = ModPackInstaller.TryBuildCustomFullModConfig(pack, artifact, out var config, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(config);
        Assert.Equal("2024-6-18", config!.AmongVersion);
        Assert.Equal("Custom ToU", config.ModName);
        Assert.Equal("https://cdn.example/overlay.zip", config.GitHubRepoOrLink);
    }

    [Fact]
    public void TryBuildCustomFullModConfig_FallsBackToPackMetadata()
    {
        var pack = new ModPack
        {
            PackCode = "TEST-CODE-1234",
            Metadata = JsonSerializer.SerializeToElement(new { amongVersion = "2025-3-25" })
        };
        var artifact = new ModPackCustomArtifact
        {
            DisplayName = "Custom",
            FileName = "overlay.zip",
            DownloadUrl = "https://cdn.example/overlay.zip",
            SourceKind = "github_full",
            ModType = "full"
        };

        var ok = ModPackInstaller.TryBuildCustomFullModConfig(pack, artifact, out var config, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("2025-3-25", config!.AmongVersion);
    }

    [Fact]
    public void TryBuildCustomFullModConfig_MissingAmongVersion_Fails()
    {
        var pack = new ModPack { PackCode = "TEST-CODE-1234" };
        var artifact = new ModPackCustomArtifact
        {
            DisplayName = "Custom",
            FileName = "overlay.zip",
            DownloadUrl = "https://cdn.example/overlay.zip",
            SourceKind = "github_full",
            ModType = "full"
        };

        var ok = ModPackInstaller.TryBuildCustomFullModConfig(pack, artifact, out var config, out var error);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Equal("custom_full_among_version_missing", error);
    }

    [Fact]
    public void TryReadAmongVersionFromMetadata_ReadsCamelAndSnakeCase()
    {
        Assert.True(ModPackService.TryReadAmongVersionFromMetadata(
            JsonSerializer.SerializeToElement(new { among_version = "2024-8-4" }),
            out var snake));
        Assert.Equal("2024-8-4", snake);

        Assert.True(ModPackService.TryReadAmongVersionFromMetadata(
            JsonSerializer.SerializeToElement(new { amongVersion = "2024-8-4" }),
            out var camel));
        Assert.Equal("2024-8-4", camel);
    }
}

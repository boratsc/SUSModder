using System.Text.Json;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Testy trwałości znacznika beta w Installation Map (3.1-11).
/// Stare mapy bez pola <c>isBeta</c> muszą pozostać czytelne i oznaczać kanał nieznany/stabilny.
/// </summary>
public class InstallationMapBetaTests : IDisposable
{
    private readonly string _tempDir;

    public InstallationMapBetaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesBetaFlag()
    {
        var map = new InstallationMap
        {
            Platform = "steam",
            FullMod = new FullModInstallation
            {
                ModId = 13,
                ModName = "Town of Us Mira",
                ModVersion = "1.6.3-beta2",
                AmongVersion = "2026-3-31",
                InstallPath = _tempDir,
                IsBeta = true,
                PinnedInstallVersion = "1.6.3-beta2"
            }
        };

        await InstallationMapManager.SaveInstallationMapAsync(_tempDir, map);
        var loaded = await InstallationMapManager.LoadInstallationMapAsync(_tempDir);

        Assert.NotNull(loaded);
        Assert.True(loaded!.FullMod.IsBeta);
        Assert.Equal("1.6.3-beta2", loaded.FullMod.PinnedInstallVersion);
    }

    [Fact]
    public async Task LegacyMapWithoutBetaField_ReadsAsStable()
    {
        var legacyJson = """
            {
              "version": "1.0",
              "installedAt": "2026-06-29T20:37:33.887Z",
              "installedBy": "SUSModder v3.0.0",
              "platform": "steam",
              "fullMod": {
                "modId": 13,
                "modName": "Town of Us Mira",
                "modVersion": "1.6.2",
                "amongVersion": "2026-3-31",
                "installPath": "C:\\Mods\\Mira",
                "installedFrom": "unknown",
                "lastUpdated": "2026-06-29T20:37:33.887Z"
              },
              "installedDlls": []
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, ".susmodder-install.json"),
            legacyJson);

        var loaded = await InstallationMapManager.LoadInstallationMapAsync(_tempDir);

        Assert.NotNull(loaded);
        Assert.False(loaded!.FullMod.IsBeta);
        Assert.Equal("1.6.2", loaded.FullMod.ModVersion);
    }

    [Fact]
    public void SerializedMap_UsesStableJsonPropertyName()
    {
        var map = new InstallationMap
        {
            FullMod = new FullModInstallation { ModId = 7, IsBeta = true }
        };

        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Contains("\"isBeta\":true", json);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

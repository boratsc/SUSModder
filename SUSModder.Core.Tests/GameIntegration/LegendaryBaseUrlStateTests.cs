using System;
using System.IO;
using SUSModder.Core.GameIntegration;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

public class LegendaryBaseUrlStateTests
{
    private const string AppId = "963137e4c29d4c79a81323b8fab03a40";
    private const string BaseUrl = "https://cdn.example.com/Builds/Org/o-example/buildhash/default";

    [Fact]
    public void ResolveConfigDirectory_PrefersLegendaryConfigPath()
    {
        var directory = LegendaryBaseUrlState.ResolveConfigDirectory(
            @"C:\legendary-config", @"C:\xdg", @"C:\Users\tester");

        Assert.Equal(@"C:\legendary-config", directory);
    }

    [Fact]
    public void ResolveConfigDirectory_UsesXdgConfigHome()
    {
        var directory = LegendaryBaseUrlState.ResolveConfigDirectory(
            null, @"C:\xdg", @"C:\Users\tester");

        Assert.Equal(Path.Combine(@"C:\xdg", "legendary"), directory);
    }

    [Fact]
    public void ResolveConfigDirectory_FallsBackToUserProfile()
    {
        var directory = LegendaryBaseUrlState.ResolveConfigDirectory(
            "  ", null, @"C:\Users\tester");

        Assert.Equal(Path.Combine(@"C:\Users\tester", ".config", "legendary"), directory);
    }

    [Fact]
    public void HasBaseUrls_ReturnsTrue_WhenMetadataContainsBaseUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}","base_urls":["{{BaseUrl}}"]}""");

        Assert.True(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenMetadataBaseUrlsAreEmpty()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}","base_urls":[]}""");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenMetadataHasNoBaseUrlsProperty()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}"}""");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsTrue_WhenInstalledJsonContainsBaseUrls()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, $$"""{"app_name":"{{AppId}}","base_urls":[]}""");
        sandbox.WriteInstalled("{\"" + AppId + "\":{\"base_urls\":[\"" + BaseUrl + "\"]}}");

        Assert.True(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenInstalledJsonBelongsToOtherApp()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteInstalled("""{"other-app":{"base_urls":["https://cdn.example.com"]}}""");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenConfigDirectoryDoesNotExist()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(missingDirectory, AppId));
    }

    [Fact]
    public void HasBaseUrls_ReturnsFalse_WhenMetadataIsMalformed()
    {
        using var sandbox = new ConfigSandbox();
        sandbox.WriteMetadata(AppId, "{ not a json ]");

        Assert.False(LegendaryBaseUrlState.HasBaseUrls(sandbox.Directory, AppId));
    }

    private sealed class ConfigSandbox : IDisposable
    {
        public ConfigSandbox()
        {
            Directory = Path.Combine(Path.GetTempPath(), "susmodder-legendary-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "metadata"));
        }

        public string Directory { get; }

        public void WriteMetadata(string appId, string json)
            => File.WriteAllText(Path.Combine(Directory, "metadata", appId + ".json"), json);

        public void WriteInstalled(string json)
            => File.WriteAllText(Path.Combine(Directory, "installed.json"), json);

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}

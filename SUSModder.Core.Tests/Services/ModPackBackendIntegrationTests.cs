using System.Net;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;
using Xunit;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// "Dry" backend integration tests for modpack platform independence.
/// These tests hit public API endpoints (no app token required) and verify
/// that the backend serves both Steam and Epic variants for the same mod/version.
/// </summary>
public class ModPackBackendIntegrationTests
{
    private static ISUSModderApiClient CreateClient()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("../../../../SUSModder/appsettings.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();
        return new SUSModderApiClient(config, new TestDiagnosticsOutput());
    }



    [Fact]
    public async Task Backend_DownloadUrl_ResolvesForSteamAndEpic_ForSameVersion()
    {
        using var client = CreateClient();

        var catalog = await client.GetCatalogAsync(new CatalogQuery { Limit = 200 });
        Assert.True(catalog.IsSuccess);
        Assert.NotNull(catalog.Data);

        var fullMod = catalog.Data!
            .FirstOrDefault(m => string.Equals(m.Type, "full", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(fullMod);

        var version = !string.IsNullOrWhiteSpace(fullMod!.CurrentVersion)
            ? fullMod.CurrentVersion
            : "latest";

        var steamUrl = client.BuildModDownloadUrl(fullMod.Id, version, "steam");
        var epicUrl = client.BuildModDownloadUrl(fullMod.Id, version, "epic");

        Assert.False(string.IsNullOrWhiteSpace(steamUrl));
        Assert.False(string.IsNullOrWhiteSpace(epicUrl));
        Assert.Contains("platform=steam", steamUrl);
        Assert.Contains("platform=epic", epicUrl);

        // HEAD requests to confirm the URLs are reachable without downloading payloads.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var steamHead = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, steamUrl));
        var epicHead = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, epicUrl));

        Assert.True(
            steamHead.StatusCode == HttpStatusCode.OK || steamHead.StatusCode == HttpStatusCode.Redirect,
            $"Steam HEAD returned {steamHead.StatusCode} for {steamUrl}");
        Assert.True(
            epicHead.StatusCode == HttpStatusCode.OK || epicHead.StatusCode == HttpStatusCode.Redirect,
            $"Epic HEAD returned {epicHead.StatusCode} for {epicUrl}");
    }

    [Fact]
    public async Task Backend_ResolvesPinnedVersions_ForSteamAndEpic()
    {
        using var client = CreateClient();
        SUSModderApiClientProvider.SetDefault(client);
        try
        {
            var catalog = await client.GetCatalogAsync(new CatalogQuery { Limit = 200 });
            Assert.True(catalog.IsSuccess);
            Assert.NotNull(catalog.Data);

            var fullMods = catalog.Data!
                .Where(m => string.Equals(m.Type, "full", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            Assert.NotEmpty(fullMods);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            foreach (var catalogMod in fullMods)
            {
                var version = !string.IsNullOrWhiteSpace(catalogMod.CurrentVersion)
                    ? catalogMod.CurrentVersion
                    : "latest";

                var modConfig = new SUSModder.Core.Configuration.ModConfiguration
                {
                    Id = catalogMod.Id,
                    ModName = catalogMod.Name,
                    ModVersion = version
                };

                var steamResolution = await SUSModder.Core.Utilities.ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "steam");
                var epicResolution = await SUSModder.Core.Utilities.ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "epic");

                Assert.False(string.IsNullOrWhiteSpace(steamResolution.Url),
                    $"Steam URL unresolved for {catalogMod.Name} ({catalogMod.Id}) v{version}");
                Assert.False(string.IsNullOrWhiteSpace(epicResolution.Url),
                    $"Epic URL unresolved for {catalogMod.Name} ({catalogMod.Id}) v{version}");

                var steamHead = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, steamResolution.Url));
                var epicHead = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, epicResolution.Url));

                Assert.True(
                    steamHead.StatusCode == HttpStatusCode.OK || steamHead.StatusCode == HttpStatusCode.Redirect,
                    $"Steam HEAD returned {steamHead.StatusCode} for {catalogMod.Name} v{version}");
                Assert.True(
                    epicHead.StatusCode == HttpStatusCode.OK || epicHead.StatusCode == HttpStatusCode.Redirect,
                    $"Epic HEAD returned {epicHead.StatusCode} for {catalogMod.Name} v{version}");
            }
        }
        finally
        {
            SUSModderApiClientProvider.ResetForTests();
        }
    }
}

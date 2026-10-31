using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Tests.Api;

/// <summary>
/// Testy kontraktu wersji beta w katalogu (3.1-11). Backend produkcyjny zwraca
/// addytywne pola <c>isBeta</c>, <c>latestStableVersion</c>, <c>latestBetaVersion</c>
/// i <c>availableReleases</c>; klient nie może ich zgubić ani wymyślać przy braku.
/// </summary>
public class CatalogBetaMappingTests
{
    [Fact]
    public async Task CatalogList_ReadsBetaFieldsAndReleases()
    {
        var api = CreateApi("""
            {"data":[{"id":13,"name":"Town of Us Mira","type":"full","currentVersion":"1.7.3",
              "isBeta":false,"latestStableVersion":"1.7.3","latestBetaVersion":"1.6.3-beta2",
              "availableReleases":[
                {"releaseId":"v208","version":"1.7.3","isBeta":false,"amongVersion":"2026-8-18","isCurrent":true},
                {"releaseId":"v178","version":"1.6.3-beta2","isBeta":true,"amongVersion":"2026-3-31","isCurrent":false}
              ]}]}
            """);

        var result = await api.GetCatalogAsync();

        var item = Assert.Single(result.Data!);
        Assert.False(item.IsBeta);
        Assert.Equal("1.7.3", item.LatestStableVersion);
        Assert.Equal("1.6.3-beta2", item.LatestBetaVersion);
        Assert.Equal(2, item.AvailableReleases!.Count);
        Assert.True(item.AvailableReleases[1].IsBeta);
        Assert.Equal("v178", item.AvailableReleases[1].ReleaseId);
    }

    [Fact]
    public async Task CatalogList_WithoutBetaFields_StaysInLegacyMode()
    {
        var api = CreateApi("""
            {"data":[{"id":1,"name":"Town of Us","type":"full","currentVersion":"6.0.0"}]}
            """);

        var result = await api.GetCatalogAsync();

        var item = Assert.Single(result.Data!);
        Assert.Null(item.IsBeta);
        Assert.Null(item.LatestStableVersion);
        Assert.Null(item.LatestBetaVersion);
        Assert.Null(item.AvailableReleases);
    }

    [Fact]
    public async Task Mapper_CopiesBetaMetadataAndReleaseVariants()
    {
        var api = CreateApi("""
            {"data":[{"id":7,"name":"Syzyfowy ToU","type":"full","currentVersion":"7.2.7",
              "isBeta":false,"latestStableVersion":"7.2.7","latestBetaVersion":"7.2.0-rc7",
              "availableReleases":[
                {"releaseId":"v218","version":"7.2.7","isBeta":false,"amongVersion":"2026-3-31","isCurrent":true,
                 "variants":[{"platform":"steam","architecture":"x86","version":"7.2.7","downloadUrl":"https://example.test/a.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","fileSizeBytes":42}]},
                {"releaseId":"v122","version":"7.2.0-rc7","isBeta":true,"amongVersion":"2025-11-18","isCurrent":false,"variants":[]}
              ]}]}
            """);

        var result = await api.GetCatalogAsync();
        var config = CatalogMapper.ToModConfiguration(Assert.Single(result.Data!), api);

        Assert.False(config.IsBeta);
        Assert.Equal("7.2.7", config.LatestStableVersion);
        Assert.Equal("7.2.0-rc7", config.LatestBetaVersion);
        Assert.Equal(2, config.AvailableReleases.Count);

        var beta = config.AvailableReleases.Single(r => r.IsBeta);
        Assert.Equal("v122", beta.ReleaseId);
        Assert.Equal("2025-11-18", beta.AmongVersion);
        Assert.Empty(beta.Variants);

        var stable = config.AvailableReleases.Single(r => !r.IsBeta);
        var variant = Assert.Single(stable.Variants);
        Assert.Equal("steam", variant.Platform);
        Assert.Equal("x86", variant.Architecture);
        Assert.Equal(42, variant.FileSizeBytes);
    }

    [Fact]
    public void VersionHistoryEntry_KeepsBetaFlag()
    {
        var entry = new CatalogVersionEntryDto
        {
            VersionId = 178,
            Version = "1.6.3-beta2",
            AmongVersion = "2026-3-31",
            IsBeta = true
        };

        var history = CatalogMapper.ToModVersionHistory(entry, modId: 13);

        Assert.True(history.IsBeta);
        Assert.Equal("1.6.3-beta2", history.ModVersion);
        Assert.Equal(13, history.ModId);
    }

    [Fact]
    public async Task CatalogDetail_ExposesReleasesWithVariants()
    {
        var api = CreateApi("""
            {"data":{"id":7,"name":"Syzyfowy ToU","currentVersion":"7.2.7",
              "latestStableVersion":"7.2.7","latestBetaVersion":"7.2.0-rc7",
              "availableReleases":[
                {"releaseId":"v122","version":"7.2.0-rc7","isBeta":true,"amongVersion":"2025-11-18","isCurrent":false,
                 "variants":[{"platform":"steam","architecture":"x86","version":"7.2.0-rc7"}]}
              ]}}
            """);

        var result = await api.GetCatalogModDetailAsync(7);

        var detail = result.Data!;
        Assert.Equal("7.2.0-rc7", detail.LatestBetaVersion);
        var release = Assert.Single(detail.AvailableReleases!);
        Assert.True(release.IsBeta);
        Assert.Equal("7.2.0-rc7", Assert.Single(release.Variants!).Version);
    }

    private static SUSModderApiClient CreateApi(string json)
    {
        var httpClient = new HttpClient(new StubHandler(json));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://example.test/v2",
                ["Configuration:BaseUrl"] = "https://example.test"
            })
            .Build();

        return new SUSModderApiClient(configuration, new NullDiagnostics(), httpClient);
    }

    private sealed class NullDiagnostics : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }
}

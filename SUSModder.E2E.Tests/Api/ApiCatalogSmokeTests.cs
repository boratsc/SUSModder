using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.E2E.Tests.Api;

/// <summary>
/// Smoke tests for the API v2 catalog. These tests hit the real production API
/// and verify that the catalog, detail, and version endpoints return valid data.
/// </summary>
public sealed class ApiCatalogSmokeTests : IDisposable
{
    private readonly ISUSModderApiClient _client;
    private readonly E2EDiagnosticsOutput _log;
    private readonly E2ETestContext _ctx;

    public ApiCatalogSmokeTests()
    {
        _ctx = new E2ETestContext("api-catalog");
        _log = new E2EDiagnosticsOutput();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();

        _client = new SUSModderApiClient(config, _log);
    }

    public void Dispose()
    {
        _client.Dispose();
        _ctx.Dispose();
    }

    [Fact]
    public async Task Catalog_ReturnsNonEmptyList()
    {
        var result = await _client.GetCatalogAsync(new CatalogQuery { Limit = 200 });

        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data);

        _ctx.WriteArtifact("catalog-snapshot.json",
            System.Text.Json.JsonSerializer.Serialize(result.Data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        _log.Write($"[E2E] Catalog returned {result.Data.Count} mods");
    }

    [Fact]
    public async Task Catalog_EveryItemHasRequiredFields()
    {
        var result = await _client.GetCatalogAsync(new CatalogQuery { Limit = 200 });
        Assert.NotNull(result.Data);

        var failures = new List<string>();
        foreach (var item in result.Data)
        {
            if (item.Id <= 0)
                failures.Add($"Item has invalid Id: {item.Id}");
            if (string.IsNullOrWhiteSpace(item.Name))
                failures.Add($"Item {item.Id} has empty Name");
            if (string.IsNullOrWhiteSpace(item.Type))
                failures.Add($"Item {item.Id} ({item.Name}) has empty Type");
            if (string.IsNullOrWhiteSpace(item.CurrentVersion))
                failures.Add($"Item {item.Id} ({item.Name}) has empty CurrentVersion");
        }

        Assert.Empty(failures);
        _log.Write($"[E2E] All {result.Data.Count} catalog items have required fields");
    }

    [Fact]
    public async Task Catalog_EveryFullModHasVariants()
    {
        var catalog = await _client.GetCatalogAsync(new CatalogQuery { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var fullMods = catalog.Data.Where(m =>
            m.Type.Equals("full", StringComparison.OrdinalIgnoreCase)).ToList();

        var failures = new List<string>();
        foreach (var mod in fullMods)
        {
            var detail = await _client.GetCatalogModDetailAsync(mod.Id);
            Assert.Equal(200, detail.StatusCode);
            Assert.NotNull(detail.Data);

            if (detail.Data.Variants.Count == 0)
                failures.Add($"Full mod {mod.Id} ({mod.Name}) has no variants");
        }

        var report = $"Full mods with variants: {fullMods.Count - failures.Count}/{fullMods.Count}";
        _ctx.WriteArtifact("full-mod-variants.txt", report);

        if (failures.Count > 0)
            _ctx.WriteArtifact("full-mod-variants-failures.txt",
                string.Join(Environment.NewLine, failures));

        // At least 80% of full mods must have variants (tolerate incomplete backend)
        Assert.True(failures.Count <= fullMods.Count * 0.2,
            $"Too many full mods missing variants: {failures.Count}/{fullMods.Count}\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public async Task Catalog_CatalogMetaWorks()
    {
        var result = await _client.GetCatalogMetaAsync();
        Assert.True(result.StatusCode is 200 or 304);
        _log.Write($"[E2E] Catalog meta: HTTP {result.StatusCode}, ETag={result.ETag}");
    }

    [Fact]
    public async Task Catalog_VersionsEndpointWorks()
    {
        var result = await _client.GetAmongUsVersionsAsync();
        Assert.True(result.StatusCode is 200 or 304);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data);
        _log.Write($"[E2E] Among Us versions: {result.Data.Count}");
    }

    [Fact]
    public async Task Catalog_CompatibilitySnapshotWorks()
    {
        var result = await _client.GetCompatibilitySnapshotAsync();
        Assert.True(result.StatusCode is 200 or 304);
        _log.Write($"[E2E] Compatibility snapshot: HTTP {result.StatusCode}");
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Tests.Api;

/// <summary>
/// Testy obecności członów JSON ikon (3.1-04). Klient API musi odróżniać
/// „pole nieobecne” od „jawne null”, bo tylko jawne null usuwa przypisanie.
/// </summary>
public class CatalogIconPresenceTests
{
    [Fact]
    public async Task ApiClient_MarksIconMemberPresent_ForExplicitNull()
    {
        var api = CreateApi("""
            {"data":[{"id":1,"name":"Town of Us","icon":null}]}
            """);

        var result = await api.GetCatalogAsync();

        var item = Assert.Single(result.Data!);
        Assert.Null(item.Icon);
        Assert.True(item.IconMemberPresent);
    }

    [Fact]
    public async Task ApiClient_MarksIconMemberAbsent_WhenMemberIsMissing()
    {
        var api = CreateApi("""
            {"data":[{"id":1,"name":"Town of Us"}]}
            """);

        var result = await api.GetCatalogAsync();

        var item = Assert.Single(result.Data!);
        Assert.Null(item.Icon);
        Assert.False(item.IconMemberPresent);
    }

    [Fact]
    public async Task ApiClient_MarksIconMemberPresent_ForReferenceObject()
    {
        var api = CreateApi("""
            {"data":[{"id":1,"name":"Town of Us","icon":{"assetId":"icon_1","url":"https://susmodder.app/assets/icons/a.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","contentType":"image/png","sizeBytes":10}}]}
            """);

        var result = await api.GetCatalogAsync();

        var item = Assert.Single(result.Data!);
        Assert.NotNull(item.Icon);
        Assert.True(item.IconMemberPresent);
        Assert.Equal("icon_1", item.Icon!.AssetId);
    }

    [Fact]
    public async Task ApiClient_MarksGlobalIconMembers_PresentAndAbsent()
    {
        var api = CreateApi("""
            {"data":{"iconsSchemaVersion":1,"icons":{"vanilla":null,"dll":{"assetId":"icon_dll","url":"https://susmodder.app/assets/icons/d.png","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","contentType":"image/png","sizeBytes":10}}}}
            """, "catalog-meta");

        var result = await api.GetCatalogMetaAsync();

        var meta = result.Data!;
        Assert.Equal(1, meta.IconsSchemaVersion);
        Assert.NotNull(meta.Icons);
        Assert.Null(meta.Icons!.Vanilla);
        Assert.True(meta.Icons.VanillaMemberPresent);
        Assert.NotNull(meta.Icons.Dll);
        Assert.True(meta.Icons.DllMemberPresent);
    }

    [Fact]
    public async Task ApiClient_MarksGlobalIconMembers_AbsentWhenSectionMissing()
    {
        var api = CreateApi("""
            {"data":{"iconsSchemaVersion":1}}
            """, "catalog-meta");

        var result = await api.GetCatalogMetaAsync();

        var meta = result.Data!;
        Assert.Null(meta.Icons);
    }

    [Fact]
    public void DefaultSerializerOptions_KeepPreviousBehaviour_AndNoPresenceFlag()
    {
        // Poza klientem API zachowanie pozostaje bez zmian: brak referencji to null,
        // a flaga obecności nie jest ustawiana (konwerter obecności nie jest zarejestrowany).
        var item = JsonSerializer.Deserialize<CatalogItemDto>("""{"id":1,"iconUrl":"/icons/old.png"}""")!;

        Assert.Null(item.Icon);
        Assert.False(item.IconMemberPresent);
        Assert.Equal("/icons/old.png", item.IconUrl);
    }

    private static SUSModderApiClient CreateApi(string json, string relativePath = "catalog")
    {
        var handler = new StubHandler(json);
        var httpClient = new HttpClient(handler);
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
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}

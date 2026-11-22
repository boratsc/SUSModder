using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Tests.Support;

namespace SUSModder.Core.Tests.Api;

/// <summary>
/// Testy paginacji katalogu w kliencie API. Ucięta migawka (druga strona błędna)
/// nie może być zwracana jako lista modów — konsumenci (ModRepository,
/// ConfigRepository) nadstaliby nią lokalny katalog.
/// </summary>
public class CatalogPaginationTests
{
    [Fact]
    public async Task GetCatalogAsModConfigurationsAsync_SecondPageFails_ReturnsEmptyList()
    {
        using var api = CreateApi(out var handler, request =>
            request.Uri.Query.Contains("offset=200")
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, Page(startId: 1, items: 200, total: 250)));

        var mods = await api.GetCatalogAsModConfigurationsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(mods);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetCatalogAsModConfigurationsAsync_AllPagesSucceed_ReturnsCompleteList()
    {
        using var api = CreateApi(out var handler, request =>
            request.Uri.Query.Contains("offset=200")
                ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, Page(startId: 201, items: 50, total: 250))
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, Page(startId: 1, items: 200, total: 250)));

        var mods = await api.GetCatalogAsModConfigurationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(250, mods.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    private static SUSModderApiClient CreateApi(
        out FakeHttpMessageHandler handler,
        Func<RecordedRequest, HttpResponseMessage> responder)
    {
        handler = new FakeHttpMessageHandler(responder);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://example.test/v2"
            })
            .Build();

        return new SUSModderApiClient(configuration, new Mock<IDiagnosticsOutput>().Object, new HttpClient(handler));
    }

    private static string Page(int startId, int items, int total) =>
        JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(startId, items).Select(id =>
                new { id, name = $"Mod {id}", type = "full", currentVersion = "1.0" }),
            meta = new { total, limit = 200 }
        });
}

using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Tests.Support;

namespace SUSModder.Core.Tests.Api;

/// <summary>
/// Authorization (app token) jedzie wyłącznie po HTTPS — decyzja użytkownika 2026-11-24.
/// Po http:// nagłówek jest po cichu pomijany, a samo żądanie idzie dalej
/// (X-User-Hash i body bez zmian).
/// </summary>
public class ApiClientAuthorizationSchemeTests
{
    [Theory]
    [InlineData("https://example.test/v2", true, true)]
    [InlineData("https://example.test/v2", false, false)]
    [InlineData("http://example.test/v2", true, false)]
    [InlineData("http://example.test/v2", false, false)]
    public async Task SendAsync_Authorization_OnlyOverHttps(string baseUrl, bool includeAuthToken, bool expectAuthorization)
    {
        using var api = CreateApi(baseUrl, out var handler);
        var userHash = new string('a', 64);
        const string body = "{\"creatorHash\":\"test\"}";

        using var response = await api.SendAsync(new SusModderApiRequest
        {
            Method = HttpMethod.Post,
            RelativePath = "modpacks",
            IncludeAuthToken = includeAuthToken,
            UserHash = userHash,
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recorded = handler.LastRequest;
        Assert.Equal(expectAuthorization, recorded.HasHeader("Authorization"));
        if (expectAuthorization)
            Assert.Equal(SecretProvider.GetDownloadToken(), recorded.GetHeader("Authorization"));

        // Guard nie zmienia pozostałych zachowań żądania.
        Assert.Equal(userHash, recorded.GetHeader("X-User-Hash"));
        Assert.Equal(body, recorded.Body);
    }

    private static SUSModderApiClient CreateApi(string baseUrl, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = baseUrl
            })
            .Build();

        return new SUSModderApiClient(configuration, new Mock<IDiagnosticsOutput>().Object, new HttpClient(handler));
    }
}

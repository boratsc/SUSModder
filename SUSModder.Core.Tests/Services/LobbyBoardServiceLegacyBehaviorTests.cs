using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Services;
using SUSModder.Core.Tests.Support;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Regresja: wprowadzenie globalnego komunikatora nie zmienia zachowania
/// starego Lobby Board (kanał per mod). Test chroni przed przypadkowym
/// przekierowaniem legacy na modId = 1.
/// </summary>
public class LobbyBoardServiceLegacyBehaviorTests
{
    private const string BaseUrl = "https://api.test.invalid/v2";
    private const string UserHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static (LobbyBoardService Service, FakeHttpMessageHandler Handler) CreateService(
        Func<RecordedRequest, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = BaseUrl
            })
            .Build();

        var log = new TestDiagnosticsOutput();
        var apiClient = new SUSModderApiClient(configuration, log, httpClient);
        var service = new LobbyBoardService(configuration, log, new FixedHardwareIdProvider(UserHash), apiClient);

        return (service, handler);
    }

    [Fact]
    public async Task PublishMessageAsync_KeepsCallerModId()
    {
        var (service, handler) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"success":true,"id":"e1"}"""));

        await service.PublishMessageAsync("abcdefghij", 42, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(handler.LastRequest.Body!);
        Assert.Equal(42, document.RootElement.GetProperty("modId").GetInt32());
    }

    [Fact]
    public async Task GetEntriesAsync_KeepsModFilter()
    {
        var (service, handler) = CreateService(_ =>
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"success":true,"entries":[]}"""));

        await service.GetEntriesAsync(modId: 42, ct: TestContext.Current.CancellationToken);

        Assert.Contains("modId=42", handler.LastRequest.Uri.Query, StringComparison.Ordinal);
    }

    private sealed class FixedHardwareIdProvider : IHardwareIdProvider
    {
        private readonly string _hash;

        public FixedHardwareIdProvider(string hash) => _hash = hash;

        public string GetAnonymousUserHash() => _hash;
    }
}

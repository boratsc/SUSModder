using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModPackServiceListAndDeleteTests
{
    private const string PackCode = "ABCD-EFGH-JKLM";
    private static readonly string CreatorHash = new('c', 64);

    [Fact]
    public async Task ListOwnPacksDetailedAsync_ParsesListAndLimitEnvelope()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, $$"""
        {
          "data": {
            "packs": [
              {
                "id": "p1",
                "packCode": "ABCD-EFGH-JKLM",
                "modName": "Town of Us",
                "fullModId": 1,
                "fullModVersion": "6.0.0",
                "ttlDays": 30,
                "vtStatus": "clean",
                "dllCount": 2,
                "externalDllCount": 0,
                "createdAt": "2026-06-22T12:00:00Z",
                "expiresAt": "2026-07-22T12:00:00Z",
                "active": true
              },
              {
                "id": "p2",
                "packCode": "WXYZ-ABCD-1234",
                "modName": "Mira",
                "fullModId": 13,
                "fullModVersion": "1.6.3b",
                "ttlDays": 7,
                "vtStatus": "clean",
                "dllCount": 0,
                "externalDllCount": 1,
                "createdAt": "2026-06-21T08:00:00Z",
                "expiresAt": "2026-06-28T08:00:00Z",
                "active": true
              }
            ],
            "activeCount": 2,
            "maxAllowed": 10
          }
        }
        """));
        var service = CreateService(api.Object);

        var result = await service.ListOwnPacksDetailedAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.Packs.Count);
        Assert.Equal(2, result.ActiveCount);
        Assert.Equal(10, result.MaxAllowed);
        Assert.Equal("ABCD-EFGH-JKLM", result.Packs[0].PackCode);
        Assert.Equal("Town of Us", result.Packs[0].ModName);
        Assert.Equal(13, result.Packs[1].FullModId);
        Assert.True(result.Packs[0].Active);
        Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal("modpacks", requests[0].RelativePath);
        Assert.Equal(CreatorHash, requests[0].Query!["creatorHash"]);
    }

    [Fact]
    public async Task ListOwnPacksDetailedAsync_ParsesFlatListResponse()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, """
        {
          "success": true,
          "packs": [
            {
              "packCode": "TEST-AAAA-BBBB",
              "modName": "Solo",
              "fullModId": 7,
              "fullModVersion": "1.0",
              "ttlDays": 30,
              "vtStatus": "clean",
              "dllCount": 0,
              "externalDllCount": 0,
              "active": true
            }
          ],
          "activeCount": 1,
          "maxAllowed": 10
        }
        """));
        var service = CreateService(api.Object);

        var result = await service.ListOwnPacksDetailedAsync();

        Assert.True(result.Success);
        Assert.Single(result.Packs);
        Assert.Equal("TEST-AAAA-BBBB", result.Packs[0].PackCode);
        Assert.Equal(7, result.Packs[0].FullModId);
        Assert.Equal(1, result.ActiveCount);
    }

    [Fact]
    public async Task ListOwnPacksDetailedAsync_ReturnsEmptyListOn404()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"NOT_FOUND\"}}"));
        var service = CreateService(api.Object);

        var result = await service.ListOwnPacksDetailedAsync();

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
        Assert.Empty(result.Packs);
    }

    [Fact]
    public async Task ListOwnPacksAsync_ReturnsPacksOnly()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, """
        {
          "data": {
            "packs": [
              { "packCode": "AAA-BBBB-CCCC", "modName": "X", "fullModId": 1, "fullModVersion": "1.0", "active": true }
            ]
          }
        }
        """));
        var service = CreateService(api.Object);

        var packs = await service.ListOwnPacksAsync();

        Assert.Single(packs);
        Assert.Equal("AAA-BBBB-CCCC", packs[0].PackCode);
    }

    [Fact]
    public async Task DeletePackDetailedAsync_SendsCreatorHashInBody_AndParsesSuccess()
    {
        var requests = new List<SusModderApiRequest>();
        string? capturedBody = null;
        var api = CreateApi(requests, req =>
        {
            // HttpClient dispose'uje Content; czytamy go synchronicznie przed utratą.
            if (req.Content != null)
                capturedBody = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, "{\"success\":true}");
        });
        var service = CreateService(api.Object);

        var result = await service.DeletePackDetailedAsync(PackCode);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Single(requests);
        Assert.Equal(HttpMethod.Delete, requests[0].Method);
        Assert.Equal($"modpacks/{PackCode}", requests[0].RelativePath);
        Assert.NotNull(capturedBody);
        Assert.Contains(CreatorHash, capturedBody!);
    }

    [Fact]
    public async Task DeletePackDetailedAsync_ParsesErrorCode_OnNotOwner()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"NOT_PACK_OWNER\",\"message\":\"x\"}}"));
        var service = CreateService(api.Object);

        var result = await service.DeletePackDetailedAsync(PackCode);

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("NOT_PACK_OWNER", result.ErrorCode);
    }

    [Fact]
    public async Task DeletePackDetailedAsync_ReturnsInvalidCode_OnMalformedInput()
    {
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.OK, "{}"));
        var service = CreateService(api.Object);

        var result = await service.DeletePackDetailedAsync("not-a-valid-code");

        Assert.False(result.Success);
        Assert.Equal("INVALID_PACK_CODE", result.ErrorCode);
    }

    [Fact]
    public async Task DeletePackAsync_KeepsBackwardsCompatibleBoolReturn()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{\"success\":true}"));
        var service = CreateService(api.Object);

        var ok = await service.DeletePackAsync(PackCode);

        Assert.True(ok);
    }

    [Fact]
    public void Constructor_NormalizesLegacyGuidFallbackTo64Hex()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{}"));
        var rawGuid = Guid.NewGuid().ToString("N");
        Assert.Equal(32, rawGuid.Length);

        var config = new ConfigurationBuilder().Build();
        var log = new Mock<IDiagnosticsOutput>();
        var hardware = new Mock<IHardwareIdProvider>();
        hardware.Setup(x => x.GetAnonymousUserHash()).Returns(rawGuid);
        var service = new ModPackService(config, log.Object, hardware.Object, api.Object);

        Assert.Equal(64, service.CreatorHash.Length);
        Assert.True(AnonymousUserHash.IsValid(service.CreatorHash));
        Assert.NotEqual(rawGuid, service.CreatorHash);
    }

    [Fact]
    public async Task CreatePackAsync_SendsNormalizedCreatorHash_WhenHardwareReturnsGuidFallback()
    {
        var requests = new List<SusModderApiRequest>();
        string? capturedBody = null;
        var api = CreateApi(requests, request =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, """
            {
              "data": {
                "packCode": "ABCD-EFGH-JKLM",
                "shareUrl": "https://example.test/p/ABCD-EFGH-JKLM",
                "expiresAt": "2026-08-01T00:00:00Z"
              }
            }
            """);
        });
        var rawGuid = "2a4bb6c85cd24aa280fd33dabe6cf6e8";
        var config = new ConfigurationBuilder().Build();
        var log = new Mock<IDiagnosticsOutput>();
        var hardware = new Mock<IHardwareIdProvider>();
        hardware.Setup(x => x.GetAnonymousUserHash()).Returns(rawGuid);
        var service = new ModPackService(config, log.Object, hardware.Object, api.Object);

        var result = await service.CreatePackAsync(new ModPackCreateRequest
        {
            FullModId = 1,
            FullModVersion = "1.0.0",
            ModName = "Test"
        });

        Assert.True(result.Success);
        Assert.Single(requests);
        Assert.Equal(service.CreatorHash, requests[0].UserHash);
        Assert.Contains(service.CreatorHash, capturedBody);
        Assert.DoesNotContain(rawGuid, capturedBody);
    }

    private static ModPackService CreateService(ISUSModderApiClient apiClient)
    {
        var config = new ConfigurationBuilder().Build();
        var log = new Mock<IDiagnosticsOutput>();
        var hardware = new Mock<IHardwareIdProvider>();
        hardware.Setup(x => x.GetAnonymousUserHash()).Returns(CreatorHash);
        return new ModPackService(config, log.Object, hardware.Object, apiClient);
    }

    private static Mock<ISUSModderApiClient> CreateApi(
        List<SusModderApiRequest> requests,
        Func<SusModderApiRequest, HttpResponseMessage> responder)
    {
        var api = new Mock<ISUSModderApiClient>();
        api.SetupGet(x => x.BaseUrl).Returns("https://api.test/v2");
        api.SetupGet(x => x.StaticAssetsBaseUrl).Returns("https://cdn.test");
        api.Setup(x => x.SendAsync(It.IsAny<SusModderApiRequest>(), It.IsAny<CancellationToken>()))
            .Returns<SusModderApiRequest, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(responder(request));
            });
        return api;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

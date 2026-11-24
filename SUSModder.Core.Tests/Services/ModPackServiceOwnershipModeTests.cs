using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModPackServiceOwnershipModeTests
{
    private const string PackCode = "ABCD-EFGH-JKLM";
    private static readonly string CreatorHash = new('c', 64);
    private static readonly string OwnerToken = new('d', 64);

    [Theory]
    [InlineData("""{"ownershipMode":"legacy"}""", "legacy")]
    [InlineData("""{"ownershipMode":"ownerToken"}""", "ownerToken")]
    [InlineData("""{"ownershipMode":"OWNERTOKEN"}""", "ownerToken")]
    [InlineData("""{"ownershipMode":"Legacy"}""", "legacy")]
    [InlineData("""{}""", null)]
    [InlineData("""{"ownershipMode":null}""", null)]
    [InlineData("""{"ownershipMode":"bogus"}""", null)]
    [InlineData("""{"ownershipMode":123}""", null)]
    public async Task ListOwnPacksDetailedAsync_ParsesOwnershipMode(string fields, string? expected)
    {
        var properties = fields == "{}" ? "" : "," + fields.Trim('{', '}');
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.OK,
            "{\"success\":true,\"packs\":[{\"packCode\":\"" + PackCode + "\"" + properties + "}]}"));
        var result = await CreateService(api.Object).ListOwnPacksDetailedAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(result.Packs);
        Assert.Equal(expected, entry.OwnershipMode);
    }

    [Theory]
    [InlineData("""{"ownershipMode":"legacy"}""", "legacy")]
    [InlineData("""{"ownershipMode":"ownerToken"}""", "ownerToken")]
    [InlineData("""{"ownershipMode":"OWNERTOKEN"}""", "ownerToken")]
    [InlineData("""{"ownershipMode":"Legacy"}""", "legacy")]
    [InlineData("""{}""", null)]
    [InlineData("""{"ownershipMode":null}""", null)]
    [InlineData("""{"ownershipMode":"bogus"}""", null)]
    [InlineData("""{"ownershipMode":123}""", null)]
    public async Task GetPackAsync_ParsesOwnershipMode(string fields, string? expected)
    {
        var properties = fields == "{}" ? "" : "," + fields.Trim('{', '}');
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.OK,
            "{\"success\":true,\"pack\":{\"packCode\":\"" + PackCode + "\"" + properties + "}}"));
        var result = await CreateService(api.Object).GetPackAsync(PackCode, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expected, result.OwnershipMode);
    }

    [Fact]
    public async Task GetPackAsync_DataEnvelope_ParsesOwnershipMode()
    {
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.OK,
            "{\"data\":{\"pack\":{\"packCode\":\"" + PackCode + "\",\"ownershipMode\":\"OWNERTOKEN\"}}}"));
        var result = await CreateService(api.Object).GetPackAsync(PackCode, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("ownerToken", result.OwnershipMode);
    }

    [Fact]
    public void HasOwnerToken_StoredToken_ReturnsTrue_WithoutApiCall()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var requests = new List<SusModderApiRequest>();
        var service = CreateService(CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);

            Assert.True(service.HasOwnerToken(PackCode));
            Assert.True(service.HasOwnerToken(PackCode.ToLowerInvariant())); // normalize
            Assert.Empty(requests);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public void HasOwnerToken_NoStore_ReturnsFalse_WithoutApiCall()
    {
        var storePath = TestStorePath();
        var requests = new List<SusModderApiRequest>();
        var service = CreateService(CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        Assert.False(service.HasOwnerToken(PackCode));
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("za-krotki")]
    [InlineData("ABCD-EFGH-JKL")]
    public void HasOwnerToken_InvalidCode_ReturnsFalse(string? code)
    {
        var service = CreateService(CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.OK, "{}")).Object, TestStorePath());

        Assert.False(service.HasOwnerToken(code!));
    }

    [Fact]
    public void HasOwnerToken_CorruptedStore_ReturnsFalse()
    {
        var storePath = TestStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, "{uszkodzony-json");
        var service = CreateService(CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        try
        {
            Assert.False(service.HasOwnerToken(PackCode));
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public void GetOwnerTokenState_MissingFileOrEntry_ReturnsMissing()
    {
        var storePath = TestStorePath();
        var service = CreateService(CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        // Plik nie istnieje.
        Assert.Equal(OwnerTokenState.Missing, service.GetOwnerTokenState(PackCode));

        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        try
        {
            // Plik istnieje, ale wpis innej paczki.
            PackOwnerTokenStore.Save(storePath, "WXYZ-ABCD-2345", OwnerToken);
            Assert.Equal(OwnerTokenState.Missing, service.GetOwnerTokenState(PackCode));
            Assert.Equal(OwnerTokenState.Available, service.GetOwnerTokenState("WXYZ-ABCD-2345"));
        }
        finally { DeleteStorePath(storePath); }
    }

    [Theory]
    [InlineData("{uszkodzony-json")]
    [InlineData("{\"ABCD-EFGH-JKLM\":\"!!!not-base64!!!\"}")]
    public void GetOwnerTokenState_CorruptedFileOrEntry_ReturnsUnreadable(string fileContent)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, fileContent);
        var service = CreateService(CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        try
        {
            Assert.Equal(OwnerTokenState.Unreadable, service.GetOwnerTokenState(PackCode));
            Assert.False(service.HasOwnerToken(PackCode)); // fail-closed zostaje bool
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void GetOwnerTokenStates_MixedCodes_CorrectStates_WithoutApiCall()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        var unreadableCode = "MNPR-STUV-WXYZ";
        var missingCode = "2345-6789-ABCD";
        var map = new Dictionary<string, string>
        {
            [PackCode] = ProtectForTest(OwnerToken),
            [unreadableCode] = "!!!not-base64!!!"
        };
        File.WriteAllText(storePath, System.Text.Json.JsonSerializer.Serialize(map));
        var requests = new List<SusModderApiRequest>();
        var service = CreateService(CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        try
        {
            var states = service.GetOwnerTokenStates(new[] { PackCode, unreadableCode, missingCode, "abcd-efgh-jklm" });

            Assert.Equal(OwnerTokenState.Available, states[PackCode]);
            Assert.Equal(OwnerTokenState.Unreadable, states[unreadableCode]);
            Assert.Equal(OwnerTokenState.Missing, states[missingCode]);
            Assert.Equal(OwnerTokenState.Available, states[PackCode]); // znormalizowany klucz duplikatu
            Assert.Empty(requests);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public void GetOwnerTokenStates_CorruptedFile_AllUnreadable()
    {
        var storePath = TestStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, "{uszkodzony-json");
        var service = CreateService(CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.OK, "{}")).Object, storePath);

        try
        {
            var states = service.GetOwnerTokenStates(new[] { PackCode, "WXYZ-ABCD-2345" });
            Assert.All(states.Values, s => Assert.Equal(OwnerTokenState.Unreadable, s));
        }
        finally { DeleteStorePath(storePath); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ProtectForTest(string token) =>
        Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(token), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    private static ModPackService CreateService(ISUSModderApiClient apiClient, string? storePath = null)
    {
        var config = new ConfigurationBuilder().Build();
        var log = new Mock<IDiagnosticsOutput>();
        var hardware = new Mock<IHardwareIdProvider>();
        hardware.Setup(x => x.GetAnonymousUserHash()).Returns(CreatorHash);
        // Izolacja od realnego magazynu użytkownika — tymczasowa ścieżka per test.
        storePath ??= TestStorePath();
        return new ModPackService(config, log.Object, hardware.Object, apiClient, storePath);
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

    private static string TestStorePath() =>
        Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"), "pack-owner-tokens");

    private static void DeleteStorePath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

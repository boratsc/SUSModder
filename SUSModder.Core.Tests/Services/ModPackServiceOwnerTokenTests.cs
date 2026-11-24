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

public class ModPackServiceOwnerTokenTests
{
    private const string PackCode = "ABCD-EFGH-JKLM";
    private static readonly string CreatorHash = new('c', 64);
    private static readonly string OwnerToken = new('d', 64);

    [Fact]
    public async Task CreatePackAsync_SavesOwnerToken_BeforeDllUpload()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var dllPath = CreateTempDll();
        var requests = new List<SusModderApiRequest>();
        var tokenSavedBeforeUpload = false;
        string? uploadForm = null;
        var api = CreateApi(requests, req =>
        {
            if (req.RelativePath == "modpacks")
            {
                return JsonResponse(HttpStatusCode.OK, $$"""
                { "data": { "packCode": "{{PackCode}}", "ownerToken": "{{OwnerToken}}" } }
                """);
            }

            tokenSavedBeforeUpload = PackOwnerTokenStore.TryGet(storePath, PackCode, out var saved) && saved == OwnerToken;
            uploadForm = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.Created, """
            { "data": { "dllEntry": { "fileName": "x.dll", "sha256": "abc", "vtStatus": "scanning" } } }
            """);
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.CreatePackAsync(new ModPackCreateRequest
            {
                FullModId = 1,
                ExternalDllFilePaths = [dllPath]
            }, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(OwnerToken, result.OwnerToken);
            Assert.True(tokenSavedBeforeUpload);
            Assert.True(PackOwnerTokenStore.TryGet(storePath, PackCode, out var stored));
            Assert.Equal(OwnerToken, stored);
            Assert.Equal(2, requests.Count);
            Assert.Contains(OwnerToken, uploadForm!);
            Assert.DoesNotContain(CreatorHash, uploadForm!);
        }
        finally
        {
            File.Delete(dllPath);
            DeleteStorePath(storePath);
        }
    }

    [Fact]
    public async Task CreatePackAsync_StoreFailure_SkipsUploads_AndReturnsOwnerTokenStoreFailed()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        // Magazyn wskazuje na istniejący katalog — Save rzuci IOException.
        var parent = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(parent, "store-is-dir");
        Directory.CreateDirectory(storePath);
        var dllPath = CreateTempDll();
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, $$"""
        { "data": { "packCode": "{{PackCode}}", "ownerToken": "{{OwnerToken}}" } }
        """));
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.CreatePackAsync(new ModPackCreateRequest
            {
                FullModId = 1,
                ExternalDllFilePaths = [dllPath]
            }, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("OWNER_TOKEN_STORE_FAILED", result.ErrorCode);
            Assert.Equal(PackCode, result.PackCode);
            Assert.Single(requests); // tylko POST /modpacks, zero uploadów
        }
        finally
        {
            File.Delete(dllPath);
            Directory.Delete(parent, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("za-krotki")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")] // nie-hex
    public async Task CreatePackAsync_MissingOrInvalidToken_UsesLegacyPath(string? token)
    {
        var storePath = TestStorePath();
        var dllPath = CreateTempDll();
        var requests = new List<SusModderApiRequest>();
        string? uploadForm = null;
        var tokenField = token == null ? "" : $",\"ownerToken\":\"{token}\"";
        var api = CreateApi(requests, req =>
        {
            if (req.RelativePath == "modpacks")
                return JsonResponse(HttpStatusCode.OK, $"{{\"data\":{{\"packCode\":\"{PackCode}\"{tokenField}}}}}");

            uploadForm = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.Created, """
            { "data": { "dllEntry": { "fileName": "x.dll", "sha256": "abc", "vtStatus": "scanning" } } }
            """);
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.CreatePackAsync(new ModPackCreateRequest
            {
                FullModId = 1,
                ExternalDllFilePaths = [dllPath]
            }, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Null(result.OwnerToken);
            Assert.False(PackOwnerTokenStore.TryGet(storePath, PackCode, out _));
            Assert.Contains(CreatorHash, uploadForm!);
        }
        finally
        {
            File.Delete(dllPath);
            DeleteStorePath(storePath);
        }
    }

    [Fact]
    public async Task DeletePackDetailedAsync_UsesOwnerToken_WhenStored()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        string? capturedBody = null;
        var api = CreateApi(new List<SusModderApiRequest>(), req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, "{\"success\":true}");
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Contains(OwnerToken, capturedBody!);
            Assert.DoesNotContain(CreatorHash, capturedBody!);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task FinalizePackAsync_UsesOwnerToken_WhenStored()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        string? capturedBody = null;
        var api = CreateApi(new List<SusModderApiRequest>(), req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, """
            { "data": { "status": "ready", "installable": true } }
            """);
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Contains(OwnerToken, capturedBody!);
            Assert.DoesNotContain(CreatorHash, capturedBody!);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task UploadCustomDllAsync_UsesOwnerToken_WhenStored()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var dllPath = CreateTempDll();
        string? uploadForm = null;
        var api = CreateApi(new List<SusModderApiRequest>(), req =>
        {
            uploadForm = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.Created, """
            { "data": { "dllEntry": { "fileName": "x.dll", "sha256": "abc", "vtStatus": "scanning" } } }
            """);
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.UploadCustomDllAsync(PackCode, dllPath, TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Contains(OwnerToken, uploadForm!);
            Assert.DoesNotContain(CreatorHash, uploadForm!);
        }
        finally
        {
            File.Delete(dllPath);
            DeleteStorePath(storePath);
        }
    }

    [Fact]
    public async Task DeclareGitHubCustomModAsync_UsesOwnerToken_WhenStored()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        string? capturedBody = null;
        var api = CreateApi(new List<SusModderApiRequest>(), req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.Accepted, """
            { "data": { "customArtifact": { "artifactId": "gh-1", "status": "scanning" } } }
            """);
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.DeclareGitHubCustomModAsync(PackCode, new ModPackCustomGithubModRequest
            {
                DisplayName = "GitHub DLL",
                GithubUrl = "https://github.com/owner/repo/releases/download/v1/mod.dll"
            }, TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Contains(OwnerToken, capturedBody!);
            Assert.DoesNotContain(CreatorHash, capturedBody!);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task FinalizePackAsync_403WithoutToken_ReturnsPackNotManageable()
    {
        var storePath = TestStorePath();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"FORBIDDEN\",\"message\":\"x\"}}"));
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("PACK_NOT_MANAGEABLE", result.ErrorCode);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task FinalizePackAsync_403WithoutToken_StableServerCode_NotOverwritten()
    {
        var storePath = TestStorePath();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"NOT_PACK_OWNER\",\"message\":\"x\"}}"));
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("NOT_PACK_OWNER", result.ErrorCode);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task FinalizePackAsync_403WithToken_KeepsBackendErrorCode()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"NOT_PACK_OWNER\",\"message\":\"x\"}}"));
        var service = CreateService(api.Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("NOT_PACK_OWNER", result.ErrorCode);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task DeletePackDetailedAsync_403WithoutToken_ForbiddenCode_ReturnsPackNotManageable()
    {
        var storePath = TestStorePath();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"FORBIDDEN\",\"message\":\"x\"}}"));
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("PACK_NOT_MANAGEABLE", result.ErrorCode);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("plain text error")]
    public async Task WritePaths_403WithoutToken_NoServerCode_ReturnsPackNotManageable(string body)
    {
        var storePath = TestStorePath();
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.Forbidden, body));
        var service = CreateService(api.Object, storePath);

        try
        {
            var delete = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);
            Assert.False(delete.Success);
            Assert.Equal("PACK_NOT_MANAGEABLE", delete.ErrorCode);

            var finalize = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);
            Assert.False(finalize.Success);
            Assert.Equal("PACK_NOT_MANAGEABLE", finalize.ErrorCode);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task DeletePackDetailedAsync_403WithToken_KeepsBackendErrorCode()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"NOT_PACK_OWNER\",\"message\":\"x\"}}"));
        var service = CreateService(api.Object, storePath);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("NOT_PACK_OWNER", result.ErrorCode);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task CreatePackAsync_UppercaseToken_StoredAndUsedVerbatim()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var dllPath = CreateTempDll();
        var uppercaseToken = new string('D', 64);
        var requests = new List<SusModderApiRequest>();
        string? uploadForm = null;
        string? deleteBody = null;
        var api = CreateApi(requests, req =>
        {
            if (req.RelativePath == "modpacks")
                return JsonResponse(HttpStatusCode.OK, $$"""
                { "data": { "packCode": "{{PackCode}}", "ownerToken": "{{uppercaseToken}}" } }
                """);
            if (req.Method == HttpMethod.Delete)
            {
                deleteBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse(HttpStatusCode.OK, "{\"success\":true}");
            }
            uploadForm = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.Created, """
            { "data": { "dllEntry": { "fileName": "x.dll", "sha256": "abc", "vtStatus": "scanning" } } }
            """);
        });
        var service = CreateService(api.Object, storePath);

        try
        {
            var result = await service.CreatePackAsync(new ModPackCreateRequest
            {
                FullModId = 1,
                ExternalDllFilePaths = [dllPath]
            }, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(uppercaseToken, result.OwnerToken);
            Assert.True(service.HasOwnerToken(PackCode));
            Assert.True(PackOwnerTokenStore.TryGet(storePath, PackCode, out var stored));
            Assert.Equal(uppercaseToken, stored); // dokładnie jak z serweru, bez normalizacji case
            Assert.Contains(uppercaseToken, uploadForm!);

            var delete = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);
            Assert.True(delete.Success);
            Assert.Contains(uppercaseToken, deleteBody!);
        }
        finally
        {
            File.Delete(dllPath);
            DeleteStorePath(storePath);
        }
    }

    [Fact]
    public async Task WritePaths_TokenInBody_CreatorHashInUserHashHeader()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var dllPath = CreateTempDll();
        var requests = new List<SusModderApiRequest>();
        var captured = new List<(SusModderApiRequest Request, string? Body)>();
        var api = CreateApi(requests, req =>
        {
            captured.Add((req, req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));
            if (req.RelativePath.EndsWith("/finalize"))
                return JsonResponse(HttpStatusCode.OK, """{ "data": { "status": "ready", "installable": true } }""");
            if (req.RelativePath.EndsWith("/custom-github-mods"))
                return JsonResponse(HttpStatusCode.Accepted, """{ "data": { "customArtifact": { "artifactId": "gh-1", "status": "scanning" } } }""");
            if (req.RelativePath.EndsWith("/dlls"))
                return JsonResponse(HttpStatusCode.Created, """{ "data": { "dllEntry": { "fileName": "x.dll", "sha256": "abc", "vtStatus": "scanning" } } }""");
            return JsonResponse(HttpStatusCode.OK, "{\"success\":true}");
        });
        var service = CreateService(api.Object, storePath);
        var ct = TestContext.Current.CancellationToken;

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);

            Assert.True((await service.DeletePackDetailedAsync(PackCode, ct)).Success);
            Assert.True((await service.FinalizePackAsync(PackCode, ct)).Success);
            Assert.NotNull(await service.UploadExternalDllAsync(PackCode, dllPath, ct));
            Assert.NotNull(await service.UploadCustomDllAsync(PackCode, dllPath, ct));
            Assert.NotNull(await service.DeclareGitHubCustomModAsync(PackCode, new ModPackCustomGithubModRequest
            {
                GithubUrl = "https://github.com/owner/repo/releases/download/v1/mod.dll"
            }, ct));

            Assert.Equal(5, captured.Count);
            foreach (var (request, body) in captured)
            {
                // X-User-Hash = tożsamość grupująca (CreatorHash); token wyłącznie w polu creatorHash body/formy.
                Assert.Equal(CreatorHash, request.UserHash);
                Assert.Contains(OwnerToken, body!);
                Assert.DoesNotContain(CreatorHash, body!);
            }
        }
        finally
        {
            File.Delete(dllPath);
            DeleteStorePath(storePath);
        }
    }

    [Theory]
    [InlineData("""{"error":{"code":"FORBIDDEN","message":"x"},"ownerToken":"{0}"}""")]
    [InlineData("""{"error":{"code":"FORBIDDEN","message":"x"},"owner_token":"{0}"}""")]
    [InlineData("""{"error":{"code":"FORBIDDEN","message":"x"},"OwnerToken":"{0}"}""")]
    [InlineData("token {0} odrzucony")]
    public async Task DeleteFailure_TokenNeverInLogOrErrorMessage(string bodyTemplate)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var logs = new List<string>();
        var body = bodyTemplate.Replace("{0}", OwnerToken);
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.Forbidden, body));
        var service = CreateService(api.Object, storePath, logs);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            var allLogs = string.Join("\n", logs);
            Assert.DoesNotContain(OwnerToken, allLogs);
            Assert.Contains("[redacted]", allLogs);
            if (result.ErrorMessage != null)
                Assert.DoesNotContain(OwnerToken, result.ErrorMessage);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task DeleteFailure_CreatorHashValue_RedactedInLogAndError()
    {
        var storePath = TestStorePath();
        var logs = new List<string>();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.BadRequest, $"{{\"error\":{{\"code\":\"X\",\"message\":\"hash {CreatorHash} odrzucony\"}}}}"));
        var service = CreateService(api.Object, storePath, logs);

        try
        {
            var result = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.DoesNotContain(CreatorHash, string.Join("\n", logs));
            Assert.DoesNotContain(CreatorHash, result.ErrorMessage!);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task FinalizeFailure_ErrorMessageWithToken_Redacted()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        var logs = new List<string>();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.Forbidden, $"{{\"error\":{{\"code\":\"SOME_CODE\",\"message\":\"token {OwnerToken} niewazny\"}}}}"));
        var service = CreateService(api.Object, storePath, logs);

        try
        {
            PackOwnerTokenStore.Save(storePath, PackCode, OwnerToken);
            var result = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.DoesNotContain(OwnerToken, result.ErrorMessage!);
            Assert.Contains("[redacted]", result.ErrorMessage!);
            Assert.DoesNotContain(OwnerToken, string.Join("\n", logs));
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task CreateFailure_TokenInBody_RedactedInLogAndError()
    {
        var storePath = TestStorePath();
        var logs = new List<string>();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.BadRequest, $"{{\"error\":{{\"code\":\"X\",\"message\":\"m\"}},\"ownerToken\":\"{OwnerToken}\"}}"));
        var service = CreateService(api.Object, storePath, logs);

        try
        {
            var result = await service.CreatePackAsync(new ModPackCreateRequest { FullModId = 1 }, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.DoesNotContain(OwnerToken, string.Join("\n", logs));
            Assert.DoesNotContain(OwnerToken, result.ErrorMessage!);
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task CreateUnexpectedResponse_SnakeCaseToken_RedactedInLog()
    {
        var storePath = TestStorePath();
        var logs = new List<string>();
        var api = CreateApi(new List<SusModderApiRequest>(), _ =>
            JsonResponse(HttpStatusCode.OK, $"{{\"data\":{{\"owner_token\":\"{OwnerToken}\"}}}}"));
        var service = CreateService(api.Object, storePath, logs);

        try
        {
            var result = await service.CreatePackAsync(new ModPackCreateRequest { FullModId = 1 }, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("INVALID_RESPONSE", result.ErrorCode);
            Assert.DoesNotContain(OwnerToken, string.Join("\n", logs));
            Assert.Contains("[redacted]", string.Join("\n", logs));
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task DeleteAndFinalize_UnreadableStore_OwnerTokenUnreadable_WithoutRequest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, "{uszkodzony-json");
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{}"));
        var service = CreateService(api.Object, storePath);

        try
        {
            var delete = await service.DeletePackDetailedAsync(PackCode, TestContext.Current.CancellationToken);
            Assert.False(delete.Success);
            Assert.Equal("OWNER_TOKEN_UNREADABLE", delete.ErrorCode);

            var finalize = await service.FinalizePackAsync(PackCode, TestContext.Current.CancellationToken);
            Assert.False(finalize.Success);
            Assert.Equal("OWNER_TOKEN_UNREADABLE", finalize.ErrorCode);

            Assert.Empty(requests); // fail-closed: zero żądań z hashem czatu
        }
        finally { DeleteStorePath(storePath); }
    }

    [Fact]
    public async Task UploadPaths_UnreadableStore_ReturnNull_WithoutRequest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("DPAPI wymaga Windows.");
        var storePath = TestStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        File.WriteAllText(storePath, "{uszkodzony-json");
        var dllPath = CreateTempDll();
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, "{}"));
        var service = CreateService(api.Object, storePath);
        var ct = TestContext.Current.CancellationToken;

        try
        {
            Assert.Null(await service.UploadExternalDllAsync(PackCode, dllPath, ct));
            Assert.Null(await service.UploadCustomDllAsync(PackCode, dllPath, ct));
            Assert.Null(await service.DeclareGitHubCustomModAsync(PackCode, new ModPackCustomGithubModRequest
            {
                GithubUrl = "https://github.com/owner/repo/releases/download/v1/mod.dll"
            }, ct));
            Assert.Empty(requests);
        }
        finally
        {
            File.Delete(dllPath);
            DeleteStorePath(storePath);
        }
    }

    private static ModPackService CreateService(ISUSModderApiClient apiClient, string storePath, List<string>? logMessages = null)
    {
        var config = new ConfigurationBuilder().Build();
        var log = new Mock<IDiagnosticsOutput>();
        if (logMessages != null)
            log.Setup(x => x.Write(It.IsAny<string>())).Callback<string>(logMessages.Add);
        var hardware = new Mock<IHardwareIdProvider>();
        hardware.Setup(x => x.GetAnonymousUserHash()).Returns(CreatorHash);
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

    private static string CreateTempDll()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "custom.dll");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
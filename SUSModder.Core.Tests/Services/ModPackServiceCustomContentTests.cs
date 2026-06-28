using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModPackServiceCustomContentTests
{
    private const string PackCode = "ABCD-EFGH-JKLM";
    private static readonly string Sha256 = new('a', 64);

    [Fact]
    public async Task UploadCustomDllAsync_PostsToV2DllEndpoint_AndParsesReturnedArtifact()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.Created, $$"""
        {
          "data": {
            "dllEntry": {
              "fileName": "custom.dll",
              "sha256": "{{Sha256}}",
              "fileSize": 1234,
              "vtStatus": "clean",
              "downloadUrl": "https://cdn.example/custom.dll"
            }
          }
        }
        """));
        var service = CreateService(api.Object);
        var filePath = CreateTempDll("custom.dll", [1, 2, 3]);

        try
        {
            var result = await service.UploadCustomDllAsync(PackCode, filePath);

            Assert.NotNull(result);
            Assert.Equal("uploaded_dll", result.SourceKind);
            Assert.Equal("custom.dll", result.FileName);
            Assert.Equal(Sha256, result.Sha256);
            Assert.Equal("clean", result.Status);
            Assert.Single(requests);
            Assert.Equal(HttpMethod.Post, requests[0].Method);
            Assert.Equal("modpacks/ABCD-EFGH-JKLM/dlls", requests[0].RelativePath);
            Assert.False(requests[0].IncludeAuthToken);
            Assert.IsType<MultipartFormDataContent>(requests[0].Content);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task UploadExternalDllAsync_ParsesV2DataWrappedDllResponse()
    {
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.Created, $$"""
        {
          "data": {
            "dllEntry": {
              "fileName": "returned.dll",
              "sha256": "{{Sha256}}",
              "fileSize": 77,
              "vtStatus": "scanning"
            }
          }
        }
        """));
        var service = CreateService(api.Object);
        var filePath = CreateTempDll("returned.dll", [4, 5, 6]);

        try
        {
            var result = await service.UploadExternalDllAsync(PackCode, filePath);

            Assert.NotNull(result);
            Assert.Equal("returned.dll", result.FileName);
            Assert.Equal(Sha256, result.Sha256);
            Assert.Equal("scanning", result.VtStatus);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GetExternalDllStatusAsync_CallsV2StatusEndpoint_AndParsesCleanStatus()
    {
        var requests = new List<SusModderApiRequest>();
        var api = CreateApi(requests, _ => JsonResponse(HttpStatusCode.OK, $$"""
        {
          "data": {
            "status": "clean",
            "downloadAvailable": true,
            "dllEntry": {
              "fileName": "scan.dll",
              "sha256": "{{Sha256}}",
              "fileSize": 99,
              "vtStatus": "clean"
            }
          }
        }
        """));
        var service = CreateService(api.Object);

        var result = await service.GetExternalDllStatusAsync(PackCode, Sha256);

        Assert.True(result.Success);
        Assert.Equal("clean", result.Status);
        Assert.True(result.DownloadAvailable);
        Assert.NotNull(result.DllEntry);
        Assert.Equal("scan.dll", result.DllEntry.FileName);
        Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal($"modpacks/ABCD-EFGH-JKLM/dlls/{Sha256}/status", requests[0].RelativePath);
    }

    [Fact]
    public async Task GetPackAsync_ParsesStatusInstallableAndCustomArtifacts_FromV2Preview()
    {
        var api = CreateApi(new List<SusModderApiRequest>(), _ => JsonResponse(HttpStatusCode.OK, $$"""
        {
          "data": {
            "packCode": "{{PackCode}}",
            "fullMod": { "id": 1, "version": "5.0.0" },
            "status": "ready",
            "installable": true,
            "customArtifacts": [
              {
                "artifactId": "artifact-1",
                "sourceKind": "uploaded_dll",
                "modType": "dll",
                "displayName": "Custom DLL",
                "fileName": "custom.dll",
                "sha256": "{{Sha256}}",
                "fileSize": 42,
                "status": "clean",
                "downloadUrl": "https://cdn.example/custom.dll",
                "dllInstallPath": "BepInEx/plugins"
              }
            ]
          }
        }
        """));
        var service = CreateService(api.Object);

        var pack = await service.GetPackAsync(PackCode);

        Assert.NotNull(pack);
        Assert.Equal("ready", pack.Status);
        Assert.True(pack.Installable);
        Assert.Single(pack.CustomArtifacts);
        Assert.Equal("artifact-1", pack.CustomArtifacts[0].ArtifactId);
        Assert.Equal("clean", pack.CustomArtifacts[0].Status);
        Assert.Equal("BepInEx/plugins", pack.CustomArtifacts[0].DllInstallPath);
    }

    [Fact]
    public async Task DeclareGitHubCustomModAsync_AndFinalizePackAsync_UseV2CustomArtifactEndpoints()
    {
        var requests = new List<SusModderApiRequest>();
        var responses = new Queue<HttpResponseMessage>([
            JsonResponse(HttpStatusCode.Accepted, """
            {
              "data": {
                "customArtifact": {
                  "artifactId": "gh-1",
                  "sourceKind": "github_dll",
                  "modType": "dll",
                  "displayName": "GitHub DLL",
                  "status": "scanning"
                }
              }
            }
            """),
            JsonResponse(HttpStatusCode.OK, """
            {
              "data": {
                "status": "ready",
                "installable": true,
                "shareUrl": "https://susmodder.app/pack/ABCD-EFGH-JKLM"
              }
            }
            """)
        ]);
        var api = CreateApi(requests, _ => responses.Dequeue());
        var service = CreateService(api.Object);

        var declared = await service.DeclareGitHubCustomModAsync(PackCode, new ModPackCustomGithubModRequest
        {
            DisplayName = "GitHub DLL",
            GithubUrl = "https://github.com/owner/repo/releases/download/v1/mod.dll"
        });
        var finalized = await service.FinalizePackAsync(PackCode);

        Assert.NotNull(declared);
        Assert.Equal("gh-1", declared.ArtifactId);
        Assert.Equal("scanning", declared.Status);
        Assert.True(finalized.Success);
        Assert.Equal("ready", finalized.Status);
        Assert.True(finalized.Installable);
        Assert.Equal(2, requests.Count);
        Assert.Equal("modpacks/ABCD-EFGH-JKLM/custom-github-mods", requests[0].RelativePath);
        Assert.Equal("modpacks/ABCD-EFGH-JKLM/finalize", requests[1].RelativePath);
    }

    private static ModPackService CreateService(ISUSModderApiClient apiClient)
    {
        var config = new ConfigurationBuilder().Build();
        var log = new Mock<IDiagnosticsOutput>();
        var hardware = new Mock<IHardwareIdProvider>();
        hardware.Setup(x => x.GetAnonymousUserHash()).Returns(new string('c', 64));
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

    private static string CreateTempDll(string fileName, byte[] content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "susmodder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }
}

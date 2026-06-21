using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Utilities;

public class ModDownloadUrlBuilderTests
{
    [Fact]
    public void GetDllFileName_SourceUrlEndsWithVersion_FallsBackToModNameDll()
    {
        var mod = new ModConfiguration
        {
            ModName = "AleLuduMod",
            GitHubRepoOrLink = "https://api.susmodder-cdn.ovh/v2/downloads/mod/5/1.1.2?platform=steam&arch=x86"
        };

        var fileName = ModDownloadUrlBuilder.GetDllFileName(mod, "steam");

        Assert.Equal("AleLuduMod.dll", fileName);
    }

    [Fact]
    public void GetDllFileName_SourceUrlHasDllFile_UsesSourceFileName()
    {
        var mod = new ModConfiguration
        {
            ModName = "AleLuduMod",
            GitHubRepoOrLink = "https://example.com/releases/AleLudu.dll"
        };

        var fileName = ModDownloadUrlBuilder.GetDllFileName(mod, "steam");

        Assert.Equal("AleLudu.dll", fileName);
    }

    [Fact]
    public async Task ResolveWithHashAsync_Epic_PrefersX64Variant_AndPropagatesArchInUrl()
    {
        // Arrange: Town of Us Mira scenario - Epic ships only x64, Steam ships only x86.
        var mod = new ModConfiguration
        {
            Id = 13,
            ModName = "Town of Us Mira",
            ModVersion = "1.6.3b"
        };

        var detail = new CatalogModDetailDto
        {
            Id = 13,
            CurrentVersion = "1.6.3b",
            Variants = new List<CatalogModVariantDto>
            {
                new() { Platform = "steam", Architecture = "x86", Version = "1.6.3b" },
                new() { Platform = "epic", Architecture = "x64", Version = "1.6.3b",
                        Sha256 = "bb2fa2516c7b3b1338ce5ca38e445bf122457492c09c30d34b54f5fa02df3108" }
            }
        };

        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi.SetupGet(x => x.BaseUrl).Returns("https://api.example/v2");
        mockApi
            .Setup(x => x.GetCatalogModDetailAsync(13, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<CatalogModDetailDto>
            {
                StatusCode = 200,
                Data = detail
            });
        mockApi
            .Setup(x => x.BuildModDownloadUrl(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((int id, string version, string platform, string arch) =>
                $"https://api.example/v2/downloads/mod/{id}/{version}?platform={platform}&arch={arch}");

        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            // Act
            var result = await ModDownloadUrlBuilder.ResolveWithHashAsync(mod, "epic");

            // Assert
            Assert.Equal("x64", ExtractQueryParam(result.Url, "arch"));
            Assert.Equal("epic", ExtractQueryParam(result.Url, "platform"));
            Assert.Equal("bb2fa2516c7b3b1338ce5ca38e445bf122457492c09c30d34b54f5fa02df3108",
                result.ExpectedSha256);
        }
        finally
        {
            if (previousDefault is null)
                SUSModderApiClientProvider.ResetForTests();
            else
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    [Fact]
    public async Task ResolveWithHashAsync_Steam_FallsBackToX86_WhenX64Missing()
    {
        var mod = new ModConfiguration
        {
            Id = 13,
            ModName = "Town of Us Mira",
            ModVersion = "1.6.3b"
        };

        var detail = new CatalogModDetailDto
        {
            Id = 13,
            CurrentVersion = "1.6.3b",
            Variants = new List<CatalogModVariantDto>
            {
                new() { Platform = "steam", Architecture = "x86", Version = "1.6.3b" }
            }
        };

        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi.SetupGet(x => x.BaseUrl).Returns("https://api.example/v2");
        mockApi
            .Setup(x => x.GetCatalogModDetailAsync(13, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<CatalogModDetailDto>
            {
                StatusCode = 200,
                Data = detail
            });
        mockApi
            .Setup(x => x.BuildModDownloadUrl(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((int id, string version, string platform, string arch) =>
                $"https://api.example/v2/downloads/mod/{id}/{version}?platform={platform}&arch={arch}");

        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            var result = await ModDownloadUrlBuilder.ResolveWithHashAsync(mod, "steam");

            Assert.Equal("x86", ExtractQueryParam(result.Url, "arch"));
        }
        finally
        {
            if (previousDefault is not null)
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    [Fact]
    public async Task ResolveWithHashAsync_Epic_FallsBackToSteamX86_WhenNoEpicVariant()
    {
        // ToU - Wygon scenario: catalog has only a steam/x86 build, but the user
        // is on Epic. The dll payload is platform-agnostic, so we should serve the
        // steam/x86 URL with platform=epic rather than failing with a 404.
        var mod = new ModConfiguration
        {
            Id = 2,
            ModName = "ToU - Wygon",
            ModVersion = "2.0.0"
        };

        var detail = new CatalogModDetailDto
        {
            Id = 2,
            CurrentVersion = "2.0.0",
            Variants = new List<CatalogModVariantDto>
            {
                new() { Platform = "steam", Architecture = "x86", Version = "2.0.0",
                        Sha256 = "40e7f6ebc732d7124151e2fdaa2e12a6de017308022a818440a0929d2565f976" }
            }
        };

        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi.SetupGet(x => x.BaseUrl).Returns("https://api.example/v2");
        mockApi
            .Setup(x => x.GetCatalogModDetailAsync(2, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<CatalogModDetailDto>
            {
                StatusCode = 200,
                Data = detail
            });
        mockApi
            .Setup(x => x.BuildModDownloadUrl(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((int id, string version, string platform, string arch) =>
                $"https://api.example/v2/downloads/mod/{id}/{version}?platform={platform}&arch={arch}");

        var previousDefault = SUSModderApiClientProvider.TryGetDefault();
        SUSModderApiClientProvider.SetDefault(mockApi.Object);
        try
        {
            var result = await ModDownloadUrlBuilder.ResolveWithHashAsync(mod, "epic");

            // The fallback variant is the only available x86 build, propagated as arch.
            Assert.Equal("x86", ExtractQueryParam(result.Url, "arch"));
            // The query is still addressed as epic (the client is on Epic); the backend
            // is expected to honor this by serving the shared steam/x86 build.
            Assert.Equal("epic", ExtractQueryParam(result.Url, "platform"));
            Assert.Equal("40e7f6ebc732d7124151e2fdaa2e12a6de017308022a818440a0929d2565f976",
                result.ExpectedSha256);
        }
        finally
        {
            if (previousDefault is not null)
                SUSModderApiClientProvider.SetDefault(previousDefault);
        }
    }

    private static string? ExtractQueryParam(string url, string name)
    {
        var idx = url.IndexOf('?');
        if (idx < 0) return null;
        var query = url[(idx + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}

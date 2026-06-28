using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using Moq;
using Xunit;
using System.Text.Json;

namespace SUSModder.Core.Tests.Services;

public class ModChangelogServiceTests
{
    private static IDiagnosticsOutput NoOpLog => new Mock<IDiagnosticsOutput>().Object;

    [Fact]
    public void EnsureQuoted_AlreadyQuoted_ReturnsSame()
    {
        var result = ModChangelogService.EnsureQuoted("\"abc123\"");
        Assert.Equal("\"abc123\"", result);
    }

    [Fact]
    public void EnsureQuoted_NotQuoted_AddsQuotes()
    {
        var result = ModChangelogService.EnsureQuoted("abc123");
        Assert.Equal("\"abc123\"", result);
    }

    [Fact]
    public void EnsureQuoted_NullOrEmpty_ReturnsSame()
    {
        Assert.Null(ModChangelogService.EnsureQuoted(null!));
        Assert.Equal(string.Empty, ModChangelogService.EnsureQuoted(string.Empty));
        Assert.Equal("   ", ModChangelogService.EnsureQuoted("   "));
    }

    [Fact]
    public async Task GetChangelogAsync_ApiReturns404_ReturnsEmptyResult()
    {
        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi
            .Setup(x => x.GetCatalogChangelogAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), default))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 404,
                Data = null,
                Error = new SusModderApiError { Code = "NOT_FOUND", Message = "No changelog" }
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);
        var result = await service.GetChangelogAsync(1, "pl");

        Assert.True(result.IsEmpty);
        Assert.Empty(result.Entries);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task GetChangelogAsync_ApiReturnsSuccess_HasEntries()
    {
        var entries = new List<CatalogChangelogEntryDto>
        {
            new()
            {
                Id = 1,
                ModId = 1,
                Version = "5.4.0",
                ReleaseName = "v5.4.0",
                Body = "Fixed bugs",
                Language = "pl"
            }
        };

        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi
            .Setup(x => x.GetCatalogChangelogAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), default))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 200,
                Data = entries,
                ETag = "abc123"
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);
        var result = await service.GetChangelogAsync(1, "pl");

        Assert.False(result.IsEmpty);
        Assert.Single(result.Entries);
        Assert.Equal("5.4.0", result.Entries[0].Version);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task GetChangelogAsync_ApiReturns304_UsesCachedData()
    {
        var entries = new List<CatalogChangelogEntryDto>
        {
            new()
            {
                Id = 1,
                ModId = 1,
                Version = "5.4.0",
                ReleaseName = "v5.4.0",
                Body = "Cached body",
                Language = "pl"
            }
        };

        var mockApi = new Mock<ISUSModderApiClient>();
        // First call: 200 with entries
        mockApi
            .SetupSequence(x => x.GetCatalogChangelogAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), default))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 200,
                Data = entries,
                ETag = "etag1"
            })
            // Second call: 304
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 304,
                ETag = "etag1"
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);

        // First call: populate cache
        var result1 = await service.GetChangelogAsync(1, "pl");
        Assert.Single(result1.Entries);

        // Second call: should get 304 and return cached data
        var result2 = await service.GetChangelogAsync(1, "pl");
        Assert.Single(result2.Entries);
        Assert.Equal("Cached body", result2.Entries[0].Body);
    }

    [Fact]
    public async Task GetChangelogAsync_ApiReturnsError_ReturnsErrorResult()
    {
        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi
            .Setup(x => x.GetCatalogChangelogAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), default))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 500,
                Error = new SusModderApiError { Code = "INTERNAL_ERROR", Message = "Server error" }
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);
        var result = await service.GetChangelogAsync(1, "pl");

        Assert.NotNull(result.ErrorCode);
        Assert.Equal("INTERNAL_ERROR", result.ErrorCode);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void CatalogChangelogEntryDto_DeserializesFromJson_WithStringId()
    {
        var json = """
        {
            "id": "123",
            "modId": 1,
            "version": "5.4.0",
            "releaseName": "v5.4.0",
            "body": "Test body",
            "language": "pl",
            "requestedLanguage": "pl",
            "fallbackLanguage": null,
            "translationStatus": "auto",
            "releaseUrl": "https://github.com/test/releases/tag/v5.4.0",
            "publishedAt": "2025-01-15T12:00:00Z",
            "source": "github"
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        var entry = JsonSerializer.Deserialize<CatalogChangelogEntryDto>(json, options);

        Assert.NotNull(entry);
        Assert.Equal(123, entry.Id);
        Assert.Equal(1, entry.ModId);
        Assert.Equal("5.4.0", entry.Version);
        Assert.Equal("Test body", entry.Body);
        Assert.Equal("pl", entry.Language);
        Assert.NotNull(entry.PublishedAt);
    }

    [Fact]
    public void CatalogChangelogEntryDto_DeserializesFromJson_WithFallbackLanguage()
    {
        var json = """
        {
            "id": 1,
            "modId": 1,
            "version": "5.4.0",
            "releaseName": "v5.4.0",
            "body": "Test",
            "language": "pl",
            "requestedLanguage": "pl",
            "fallbackLanguage": "en",
            "translationStatus": "auto"
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        var entry = JsonSerializer.Deserialize<CatalogChangelogEntryDto>(json, options);

        Assert.NotNull(entry);
        Assert.Equal("en", entry.FallbackLanguage);
    }
}

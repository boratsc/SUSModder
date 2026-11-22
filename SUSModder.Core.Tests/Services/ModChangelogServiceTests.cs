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

    [Theory]
    // Pełny ETag z cache (mocny lub słaby) musi trafiać do klienta bez zmian —
    // doklejanie cudzysłowów powodowało 200 zamiast 304.
    [InlineData(4001, "W/\"abc\"")]
    [InlineData(4002, "\"abc\"")]
    public async Task GetChangelogAsync_PassesCachedEtagUnchanged(int modId, string etag)
    {
        var entries = new List<CatalogChangelogEntryDto>
        {
            new() { Id = 1, ModId = modId, Version = "5.4.0", Body = "Cached body", Language = "pl" }
        };
        var sentIfNoneMatch = new List<string?>();

        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi
            .Setup(x => x.GetCatalogChangelogAsync(modId, "pl", 5, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((int _, string __, int ___, string? ifNoneMatch, CancellationToken ____) =>
            {
                sentIfNoneMatch.Add(ifNoneMatch);
                return Task.FromResult(sentIfNoneMatch.Count == 1
                    ? new SusModderApiResult<List<CatalogChangelogEntryDto>>
                    {
                        StatusCode = 200,
                        Data = entries,
                        ETag = etag
                    }
                    : new SusModderApiResult<List<CatalogChangelogEntryDto>>
                    {
                        StatusCode = 304,
                        ETag = etag
                    });
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);

        var first = await service.GetChangelogAsync(modId, "pl", cancellationToken: TestContext.Current.CancellationToken);
        var second = await service.GetChangelogAsync(modId, "pl", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(sentIfNoneMatch[0]);
        Assert.Equal(etag, Assert.Single(sentIfNoneMatch.Skip(1)));
        Assert.Equal(etag, second.ETag);
        Assert.Single(second.Entries);
        Assert.Equal("Cached body", second.Entries[0].Body);
        Assert.False(first.IsEmpty);
    }

    [Fact]
    public async Task GetChangelogAsync_ApiReturns404_ReturnsEmptyResult()
    {
        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi
            .Setup(x => x.GetCatalogChangelogAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 404,
                Data = null,
                Error = new SusModderApiError { Code = "NOT_FOUND", Message = "No changelog" }
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);
        var result = await service.GetChangelogAsync(1, "pl", cancellationToken: TestContext.Current.CancellationToken);

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
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 200,
                Data = entries,
                ETag = "abc123"
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);
        var result = await service.GetChangelogAsync(1, "pl", cancellationToken: TestContext.Current.CancellationToken);

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
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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
        var result1 = await service.GetChangelogAsync(1, "pl", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(result1.Entries);

        // Second call: should get 304 and return cached data
        var result2 = await service.GetChangelogAsync(1, "pl", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(result2.Entries);
        Assert.Equal("Cached body", result2.Entries[0].Body);
    }

    [Fact]
    public async Task GetChangelogAsync_ApiReturnsError_ReturnsErrorResult()
    {
        var mockApi = new Mock<ISUSModderApiClient>();
        mockApi
            .Setup(x => x.GetCatalogChangelogAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SusModderApiResult<List<CatalogChangelogEntryDto>>
            {
                StatusCode = 500,
                Error = new SusModderApiError { Code = "INTERNAL_ERROR", Message = "Server error" }
            });

        var service = new ModChangelogService(mockApi.Object, NoOpLog);
        var result = await service.GetChangelogAsync(1, "pl", cancellationToken: TestContext.Current.CancellationToken);

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

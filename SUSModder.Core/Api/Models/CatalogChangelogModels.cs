using System.Text.Json.Serialization;

namespace SUSModder.Core.Api.Models;

public sealed class CatalogChangelogEntryDto
{
    // Backend returns id as string in production (per smoke test).
    // JsonNumberHandling.AllowReadingFromString on the client serializer handles this.
    public long Id { get; init; }

    [JsonPropertyName("modId")]
    public int ModId { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("releaseName")]
    public string ReleaseName { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [JsonPropertyName("requestedLanguage")]
    public string RequestedLanguage { get; init; } = string.Empty;

    [JsonPropertyName("fallbackLanguage")]
    public string? FallbackLanguage { get; init; }

    [JsonPropertyName("translationStatus")]
    public string TranslationStatus { get; init; } = string.Empty;

    [JsonPropertyName("translationProvider")]
    public string? TranslationProvider { get; init; }

    [JsonPropertyName("translationModel")]
    public string? TranslationModel { get; init; }

    [JsonPropertyName("releaseUrl")]
    public string? ReleaseUrl { get; init; }

    [JsonPropertyName("githubRepo")]
    public string? GithubRepo { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("fetchedAt")]
    public DateTimeOffset? FetchedAt { get; init; }

    [JsonPropertyName("translatedAt")]
    public DateTimeOffset? TranslatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Implementacja komunikacji z Clair API (endpointy /api/susmodder/*).
/// Używana do pobierania konfiguracji OAuth, listy serwerów i credentials SUSTATS.
/// </summary>
public class ClairDiscordService : IClairDiscordService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly IConfiguration _configuration;
    private readonly IDiagnosticsOutput _diagnosticsOutput;

    public ClairDiscordService(
        IConfiguration configuration,
        IDiagnosticsOutput diagnosticsOutput)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _diagnosticsOutput = diagnosticsOutput ?? throw new ArgumentNullException(nameof(diagnosticsOutput));
    }

    /// <inheritdoc />
    public async Task<ClairOAuthConfig> GetOAuthConfigAsync()
    {
        var url = BuildApiUrl("config");
        _diagnosticsOutput.Write($"[ClairDiscord] Fetching OAuth config from {url}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyDefaultHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Clair API config returned {response.StatusCode}");

        var config = JsonSerializer.Deserialize<ClairOAuthConfig>(body, JsonOptions)
            ?? throw new InvalidOperationException("Empty config from Clair API");
        return config;
    }

    /// <inheritdoc />
    public async Task<List<DiscordGuildInfo>> GetAccessibleGuildsAsync(string accessToken)
    {
        var url = BuildApiUrl("guilds");
        _diagnosticsOutput.Write($"[ClairDiscord] Fetching guilds from {url}");

        var payload = JsonSerializer.Serialize(new { discord_access_token = accessToken }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        ApplyDefaultHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Clair API guilds returned {response.StatusCode}");

        var guildsResponse = JsonSerializer.Deserialize<GuildsResponse>(body, JsonOptions);
        return guildsResponse?.Guilds ?? new List<DiscordGuildInfo>();
    }

    /// <inheritdoc />
    public async Task<SustatsCredentials> GetCredentialsAsync(string accessToken, string guildId)
    {
        var url = BuildApiUrl("credentials");
        _diagnosticsOutput.Write($"[ClairDiscord] Fetching credentials from {url}");

        var payloadObj = new CredentialsRequest
        {
            DiscordAccessToken = accessToken,
            GuildId = guildId
        };
        var payload = JsonSerializer.Serialize(payloadObj, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        ApplyDefaultHeaders(request);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if ((int)response.StatusCode == 401)
            throw new UnauthorizedAccessException("Token Discord jest nieprawidłowy lub wygasł.");
        if ((int)response.StatusCode == 403)
            throw new UnauthorizedAccessException("Nie masz dostępu do SUSTATS na tym serwerze.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Clair API credentials returned {response.StatusCode}");

        var credsResponse = JsonSerializer.Deserialize<CredentialsResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Empty credentials response");
        return credsResponse.Credentials
            ?? throw new InvalidOperationException("No credentials in response");
    }

    #region URL Builders

    private string BuildApiUrl(string path)
    {
        var baseUrl = _configuration.GetSection("Configuration")["ClairApiBaseUrl"]
            ?? "https://clairbot.app";
        var endpoint = _configuration.GetSection("Configuration")["ClairApiSusmodderEndpoint"]
            ?? "/susmodder";
        return $"{baseUrl.TrimEnd('/')}{endpoint}/{path}";
    }

    #endregion

    #region HTTP

    private static void ApplyDefaultHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("User-Agent", "SUSModder/1.0");
        request.Headers.Add("Accept", "application/json");
    }

    #endregion

    #region JSON

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region DTOs

    private class GuildsResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("guilds")]
        public List<DiscordGuildInfo> Guilds { get; set; } = new();
    }

    private class CredentialsRequest
    {
        [JsonPropertyName("discord_access_token")]
        public string DiscordAccessToken { get; set; } = string.Empty;

        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; } = string.Empty;
    }

    private class CredentialsResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("credentials")]
        public SustatsCredentials? Credentials { get; set; }
    }

    #endregion
}

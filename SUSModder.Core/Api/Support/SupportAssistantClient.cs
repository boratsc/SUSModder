using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Api.Support;

/// <summary>
/// Klient do API v2 support: /query, /feedback, /report-metadata, /knowledge/meta, /health.
/// Używa ISUSModderApiClient lub osobnego HttpClient.
/// </summary>
public sealed class SupportAssistantClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly IDiagnosticsOutput _log;
    private readonly TimeSpan _queryTimeout = TimeSpan.FromSeconds(15);
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SupportAssistantClient(
        string baseUrl,
        IDiagnosticsOutput log,
        HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _log = log ?? throw new ArgumentNullException(nameof(log));

        if (httpClient is null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    /// <summary>
    /// Pobiera metadane KB (wersja, języki, kategorie, liczba artykułów).
    /// </summary>
    public async Task<SupportKnowledgeMeta?> GetKnowledgeMetaAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/knowledge/meta");
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<SupportApiEnvelope<SupportKnowledgeMeta>>(body, JsonOptions);
            return envelope?.Data;
        }
        catch (Exception ex)
        {
            _log.Write($"[SupportClient] GetKnowledgeMeta failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Wysyła zapytanie do API support.
    /// </summary>
    /// <returns>Pełna odpowiedź lub null gdy błąd/timeout.</returns>
    public async Task<SupportQueryResponse?> QueryAsync(
        SupportQueryRequest request,
        string? userHash = null,
        CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_queryTimeout);

            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/query")
            {
                Content = content
            };

            if (!string.IsNullOrWhiteSpace(userHash))
                httpRequest.Headers.TryAddWithoutValidation("X-User-Hash", userHash);

            using var response = await _httpClient.SendAsync(httpRequest, cts.Token);

            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(body);
                _log.Write($"[SupportClient] Query failed: HTTP {(int)response.StatusCode} {error}");
                return null;
            }

            var envelope = JsonSerializer.Deserialize<SupportApiEnvelope<SupportQueryResponse>>(body, JsonOptions);
            return envelope?.Data;
        }
        catch (OperationCanceledException)
        {
            _log.Write("[SupportClient] Query timed out or cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            _log.Write($"[SupportClient] Query failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Wysyła feedback (pomogło / nie pomogło).
    /// </summary>
    public async Task<bool> SendFeedbackAsync(
        SupportFeedbackRequest request,
        string? userHash = null,
        CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/feedback")
            {
                Content = content
            };

            if (!string.IsNullOrWhiteSpace(userHash))
                httpRequest.Headers.TryAddWithoutValidation("X-User-Hash", userHash);

            using var response = await _httpClient.SendAsync(httpRequest, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.Write($"[SupportClient] Feedback failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Zgłasza metadane o wygenerowanym raporcie diagnostycznym.
    /// </summary>
    public async Task<bool> SendReportMetadataAsync(
        SupportReportMetadataRequest request,
        string? userHash = null,
        CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/report-metadata")
            {
                Content = content
            };

            if (!string.IsNullOrWhiteSpace(userHash))
                httpRequest.Headers.TryAddWithoutValidation("X-User-Hash", userHash);

            using var response = await _httpClient.SendAsync(httpRequest, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.Write($"[SupportClient] ReportMetadata failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sprawdza stan usługi support (health).
    /// </summary>
    public async Task<SupportHealthInfo?> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/health");
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<SupportApiEnvelope<SupportHealthInfo>>(body, JsonOptions);
            return envelope?.Data;
        }
        catch (Exception ex)
        {
            _log.Write($"[SupportClient] Health check failed: {ex.Message}");
            return null;
        }
    }

    private static string? TryParseError(string body)
    {
        try
        {
            var err = JsonSerializer.Deserialize<SupportApiError>(body, JsonOptions);
            return err?.Error?.Message ?? body[..Math.Min(body.Length, 200)];
        }
        catch
        {
            return body[..Math.Min(body.Length, 100)];
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient?.Dispose();
    }

    // Internal envelope types for deserialization
    private sealed class SupportApiEnvelope<T>
    {
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private sealed class SupportApiError
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public SupportApiErrorBody? Error { get; set; }
    }

    private sealed class SupportApiErrorBody
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}

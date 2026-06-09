using System.Text.Json.Serialization;

namespace SUSModder.Core.Api.Models;

public sealed class SusModderApiResult<T>
{
    public int StatusCode { get; init; }
    public bool IsNotModified => StatusCode == 304;
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public string? ETag { get; init; }
    public T? Data { get; init; }
    public SusModderApiMeta? Meta { get; init; }
    public SusModderApiError? Error { get; init; }
}

public sealed class SusModderApiMeta
{
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonPropertyName("offset")]
    public int? Offset { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}

public sealed class SusModderApiError
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class SusModderApiException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public SusModderApiException(string errorCode, string message, int statusCode)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
